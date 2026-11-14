using System.Linq;
using SheepGame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SheepGame.EditorTools
{
    // Moving the game onto a new backdrop has happened four times now — a pasture, an
    // apron, a harbour, a car park — and every time the work is the same two jobs: get the
    // rig into the new scene, then sit the board on whatever patch of ground the scene
    // offers. Both are here rather than done by hand, because the second one has to be
    // repeated every time the ground moves or the artwork under it is replaced.
    public static class BoardSceneSetup
    {
        const string RigScene = "Assets/Toon Planes Pack/Scenes/Demo_Scene.unity";

        // Everything the puzzle needs and nothing of the setting it came from: no belt, no
        // fence, no runway, no lighting. A new scene brings its own world.
        //
        // "MainSystemn" is the container the rig was gathered into when it moved onto the
        // plane pack's demo scene; the loose names are what older scenes still use.
        static readonly string[] RigRoots =
            { "MainSystemn", "Board", "GameManager", "UICanvas", "EventSystem" };

        // Markers that only mean something to the theme they were placed for.
        static readonly string[] LeaveBehind =
            { "RunwayThreshold", "RunwayEnd", "BeltOutfeed 1", "BeltOutfeed 2" };

        [MenuItem("SheepGame/Import Game Rig Into Scene")]
        public static void ImportRig()
        {
            var scene = SceneManager.GetActiveScene();
            if (Object.FindFirstObjectByType<Board>() != null)
            {
                Debug.LogWarning($"BoardSceneSetup: {scene.name} already has a Board; nothing imported.");
                return;
            }

            var source = EditorSceneManager.OpenScene(RigScene, OpenSceneMode.Additive);
            foreach (var root in source.GetRootGameObjects())
            {
                if (!RigRoots.Contains(root.name)) continue;
                // Moved, not copied: a copy of a prefab instance comes across as loose
                // objects. The source scene is closed without saving, so it keeps its own.
                SceneManager.MoveGameObjectToScene(root, scene);
            }
            EditorSceneManager.CloseScene(source, true);

            var board = Object.FindFirstObjectByType<Board>();
            if (board == null)
            {
                Debug.LogError($"BoardSceneSetup: no Board found in {RigScene}");
                return;
            }

            foreach (var name in LeaveBehind)
            {
                var stray = board.transform.Find(name);
                if (stray != null) Object.DestroyImmediate(stray.gameObject);
            }

            board.outfeeds = new Transform[0];
            board.beltHeight = 0f;
            BindCamera(board);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"BoardSceneSetup: rig imported into {scene.name}. " +
                      "Select the ground the board should sit on and run Fit Board To Ground.");
        }

        [MenuItem("SheepGame/Bind Camera To Board")]
        public static void BindCameraToBoard()
        {
            var board = Object.FindFirstObjectByType<Board>();
            if (board == null)
            {
                Debug.LogError("BoardSceneSetup: no Board in this scene.");
                return;
            }

            BindCamera(board);
            EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        }

        // The camera keeps the pitch, yaw and lens the scene was framed with — that is the
        // shot the scene was built around. Only the distance is the framer's business.
        static void BindCamera(Board board)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("BoardSceneSetup: no MainCamera in this scene; wire the board's camera by hand.");
                return;
            }

            var framer = cam.GetComponent<CameraFramer>();
            if (framer == null) framer = cam.gameObject.AddComponent<CameraFramer>();
            framer.target = cam;

            board.raycastCamera = cam;
            board.cameraFramer = framer;
        }

        [MenuItem("SheepGame/Fit Board To Ground")]
        public static void FitBoardToSelection()
        {
            if (Selection.activeGameObject == null)
            {
                Debug.LogError("BoardSceneSetup: select the ground object the board should sit on.");
                return;
            }
            FitBoardTo(Selection.activeGameObject);
        }

        // Sizes the whole playfield — cells, the lane around them and the pieces standing
        // on them — to a patch of ground already in the scene, so the artwork decides the
        // scale instead of the board being dropped on top at whatever size it last had.
        public static void FitBoardTo(GameObject ground)
        {
            var board = Object.FindFirstObjectByType<Board>();
            if (board == null)
            {
                Debug.LogError("BoardSceneSetup: no Board in this scene — import the rig first.");
                return;
            }
            if (!TryGroundRect(ground, out Bounds rect, out float surfaceY))
            {
                Debug.LogError($"BoardSceneSetup: {ground.name} has no renderers to measure.");
                return;
            }

            // The apron is the field plus one margin of lane on each side, and the margin
            // is kept equal to a cell so the lane is exactly one piece wide. Whichever axis
            // runs out first sets the cell; the other keeps the slack, which on a landscape
            // car park leaves its painted bays showing down both sides.
            //
            // The board's own yaw is respected rather than reset: standing a portrait field
            // on its side is how it is made to fill a landscape yard, and it is worth real
            // cell size — 9x11 in this car park goes from 1.85 to 2.18 by turning it.
            int columns = Mathf.Max(1, board.fieldSize.x) + 2;
            int rows = Mathf.Max(1, board.fieldSize.y) + 2;
            if (Mathf.Abs(Mathf.Sin(board.transform.eulerAngles.y * Mathf.Deg2Rad)) > 0.5f)
                (columns, rows) = (rows, columns);

            float cell = Mathf.Min(rect.size.x / columns, rect.size.z / rows);

            board.cellSize = cell;
            board.roadMargin = cell;
            board.pieceFill = 0.82f;

            // Everything that was a hand-tuned distance is a distance on the old board, so
            // each one is restated as a share of a cell rather than carried across.
            board.hopDistance = cell * 0.9f;
            board.hopArc = cell * 0.6f;
            board.taxiSpeed = cell * 2.6f;
            board.takeoffSpeed = cell * 2.6f;

            board.transform.position = new Vector3(rect.center.x, surfaceY, rect.center.z);

            // Both are measurements of a conveyor fitted to the board's old size, and both
            // are wildly wrong at any other size — a hop of the old belt's height would now
            // clear a whole cell. Cleared rather than converted: whether this setting even
            // has a belt is a question for the theme, and SheepGame > Fit Baggage Belt puts
            // them back the moment there is one to measure.
            board.beltHeight = 0f;
            board.outfeeds = new Transform[0];

            // Painted markings sit a hair over the ground they are painted on: enough not
            // to fight it for depth, little enough that pieces standing at the board plane
            // do not look like they are hovering.
            if (board.apron == null) board.apron = board.GetComponentInChildren<ApronSurface>();
            if (board.apron != null)
            {
                var t = board.apron.transform;
                t.localPosition = new Vector3(0f, cell * 0.02f, 0f);
                board.apron.cornerRadius = cell * 1.5f;
                board.apron.shoreWidth = cell * 0.4f;
                board.apron.shoreHeight = cell * 0.06f;
                board.apron.RebuildPreview();
            }

            // The search for a distance starts at this floor and only ever moves outwards,
            // so a floor left over from a board eight times this size pins the camera too
            // far back and the fit silently gives up short of the fill it was asked for.
            var framer = board.cameraFramer != null ? board.cameraFramer
                       : Camera.main != null ? Camera.main.GetComponent<CameraFramer>() : null;
            if (framer != null) framer.minDistance = cell * 2f;

            EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
            Debug.Log($"BoardSceneSetup: board fitted to {ground.name} — {rect.size.x:0.0} x {rect.size.z:0.0} " +
                      $"at y {surfaceY:0.00}, cell {cell:0.000}, apron {columns * cell:0.0} x {rows * cell:0.0}.");
        }

        // The ground's footprint, and the height of the surface pieces stand on.
        //
        // The height is dropped onto the ground's own collider from above rather than read
        // off its bounds: a car park is a single mesh whose top is the kerb wall around it,
        // three units above the asphalt, so bounds would leave the board floating over the
        // scene by more than a piece is tall.
        static bool TryGroundRect(GameObject ground, out Bounds rect, out float surfaceY)
        {
            rect = default;
            surfaceY = 0f;

            bool any = false;
            foreach (var r in ground.GetComponentsInChildren<Renderer>())
            {
                if (!any) { rect = r.bounds; any = true; }
                else rect.Encapsulate(r.bounds);
            }
            if (!any) return false;

            surfaceY = rect.min.y;
            var down = new Ray(new Vector3(rect.center.x, rect.max.y + 1f, rect.center.z), Vector3.down);

            foreach (var collider in ground.GetComponentsInChildren<Collider>())
                if (collider.Raycast(down, out RaycastHit hit, rect.size.y + 2f))
                    surfaceY = Mathf.Max(surfaceY, hit.point.y);

            return true;
        }
    }
}
