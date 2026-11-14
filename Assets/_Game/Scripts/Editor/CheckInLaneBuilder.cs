using System.Collections.Generic;
using System.Linq;
using SheepGame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Builds what lies past the junction: a baggage sorter across the top of the screen, laid out
    // after Bus Jam's car park (the user's reference, 2026-09-14) but made of the terminal's own
    // conveyors.
    //
    //                 [ scanner ]  (trolleys to come)
    //                      |
    //     .-- collector ===+=== collector --.      flows inwards
    //    (    | bay | bay | bay | bay |     )     a row of bays across the island, centred
    //     '-- sorter =====+==== sorter ----'      flows outwards
    //                      |
    //                junction merge
    //
    // Every case moves on belt: a case of the terminal's colour rides the sorter out, round the end
    // of the loop and back along the collector into the scanner; any other rides the sorter to the
    // nearest free bay and waits there; when its colour comes it runs up out of the bay onto the
    // collector and in. The row shows only the bays the level has places for. (The first version put
    // the places on a bare platform and the cases slid across the floor to them -- "места ожидания
    // откуда-то от балды", and the look of a car park: a road and parking bays. Neither belongs in
    // a terminal.)
    //
    // "Top of the screen" is the board's back edge, and the junction was on its front, so the
    // first thing this does is turn the conveyor half round the board. The levels are not turned.
    //
    // Measured off the belt the user assembled, like the conveyor dressing: the junction is
    // hand-placed and not symmetric, and belts laid at invented coordinates would meet it with a
    // step. Its own scene root, for the dressing's two reasons: a hall rebuild destroys everything
    // under Terminal, and BeltRing.Fit scales everything under the belt. Re-runnable. Build Terminal
    // Sandbox afterwards if the sorter's depth changed, to put the hall's far wall behind it.
    public static class CheckInLaneBuilder
    {
        public const string RootName = "Check-in Lane";
        const string MeshDir = "Assets/_Game/Meshes/CheckIn";
        const string MaterialDir = "Assets/_Game/Materials/CheckIn";

        // As many bays as a level can ever have places; only the ones in use are shown.
        const int Bays = 8;
        const float BayPitch = 4.3f;
        const float BayLength = 5.4f;
        const float MergeLength = 3.5f;
        const int UpcomingShown = 4;

        [MenuItem("SheepGame/Build Check-in Lane")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Check-in] Stop play mode first. Nothing was changed.");
                return;
            }

            var board = Object.FindFirstObjectByType<Board>(FindObjectsInactive.Include);
            if (board == null || board.belt == null)
            {
                Debug.LogError("[Check-in] Needs a Board with a BeltRing under it.");
                return;
            }

            var bt = board.transform;
            var belt = board.belt.transform;

            // The portals stood where the sorter goes, and so did the check-in desk parked over the
            // junction: one mesh with two belt arms and a scanner arch of its own, built for the
            // two-mouthed outfeed. Off, not deleted: they are the user's.
            foreach (Transform child in belt)
                if ((child.name.StartsWith("12.3") || child.name.StartsWith("08.CheckInDesk")) && child.gameObject.activeSelf)
                {
                    Undo.RecordObject(child.gameObject, "Build Check-in Lane");
                    child.gameObject.SetActive(false);
                }

            Undo.RecordObject(board, "Build Check-in Lane");
            Undo.RecordObject(belt, "Build Check-in Lane");
            board.FitBelt();

            // ---- the junction to the back ------------------------------------------------

            if (!FindJunction(bt, belt, board, out bool back, out _))
            {
                Debug.LogError("[Check-in] Could not find the junction's two turns.");
                return;
            }
            if (!back)
            {
                // Half a turn about the board's centre. BeltRing.Fit settles it onto the ring again.
                var half = Quaternion.Euler(0f, 180f, 0f);
                belt.localPosition = half * belt.localPosition;
                belt.localRotation = half * belt.localRotation;
                board.FitBelt();
            }

            float h = board.beltHeight;
            if (!FindJunction(bt, belt, board, out back, out var j) || !back)
            {
                Debug.LogError("[Check-in] The junction did not end up on the back edge.");
                return;
            }

            // Where each arm runs once it has turned: the old outfeed markers say, and they turned
            // with the belt.
            float armL = j.turnL.max.x - j.width * 0.5f, armR = j.turnR.min.x + j.width * 0.5f;
            var markers = belt.Cast<Transform>().Where(t => t.name.StartsWith("Outfeed"))
                              .Select(t => bt.InverseTransformPoint(t.position).x).OrderBy(x => x).ToList();
            if (markers.Count >= 2) { armL = markers[0]; armR = markers[markers.Count - 1]; }

            float w = j.width;
            float mergeX = (armL + armR) * 0.5f;
            float outer = Mathf.Max(j.turnL.max.z, j.turnR.max.z);
            float sorterZ = outer + MergeLength + w * 0.5f;
            float bayStart = sorterZ + w * 0.5f;
            float bayZ = bayStart + BayLength * 0.5f;
            float collectorZ = bayStart + BayLength + w * 0.5f;
            float mouthZ = collectorZ + w * 0.5f + 1.6f;
            float baseH = h - 0.04f;
            float tread = w * 0.82f;

            // ---- the root ----------------------------------------------------------------

            foreach (var existing in board.gameObject.scene.GetRootGameObjects())
                if (existing.name == RootName) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Check-in Lane");
            root.transform.SetPositionAndRotation(bt.position, bt.rotation);
            var lane = root.AddComponent<CheckInLane>();

            EnsureFolder(MeshDir);
            EnsureFolder(MaterialDir);

            var sideMat = Plastic("CheckIn_Side", new Color(0.62f, 0.80f, 0.90f), 0.45f);
            var railMat = Plastic("CheckIn_Rail", new Color(0.88f, 0.96f, 0.99f), 0.6f);
            var bodyMat = Plastic("CheckIn_Body", new Color(0.95f, 0.97f, 0.98f), 0.45f);
            var colourMat = Plastic("CheckIn_Colour", Color.white, 0.55f, glow: true);
            var lampMat = Plastic("CheckIn_Lamp", Color.white, 0.7f, glow: true);
            var darkMat = Plastic("CheckIn_Opening", new Color(0.10f, 0.28f, 0.34f), 0.2f);
            var treadMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Materials/Terminal/Belt_Tread.mat");
            var trimMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Materials/Terminal/Terminal_Trim.mat");
            if (trimMat == null) trimMat = sideMat;

            // ---- route points ------------------------------------------------------------

            var route = Node("Route", root.transform);
            lane.leftFeed = Feed(route, "Left", j.joinL, j.laneZ, armL, outer);
            lane.rightFeed = Feed(route, "Right", j.joinR, j.laneZ, armR, outer);
            lane.entry = Point(route, "Entry", new Vector3(mergeX, h, sorterZ));
            lane.road = Point(route, "Collector", new Vector3(mergeX, h, collectorZ));
            lane.mouth = Point(route, "Mouth", new Vector3(mergeX, h, mouthZ));

            var belts = Node("Belts", root.transform);

            // ---- the merge ---------------------------------------------------------------

            float top = outer - 0.6f;               // tucked under the turns so no seam shows
            float mergeEnd = sorterZ - w * 0.5f;
            Prism(belts, "Merge Base", sideMat, baseH, new[]
            {
                new Vector2(armL - w * 0.5f, top), new Vector2(armR + w * 0.5f, top),
                new Vector2(mergeX + w * 0.5f, mergeEnd + 0.05f), new Vector2(mergeX - w * 0.5f, mergeEnd + 0.05f),
            });
            Tread(belts, "Merge Tread", treadMat, h + 0.02f,
                  new Vector2(armL - tread * 0.5f, top), new Vector2(armR + tread * 0.5f, top),
                  new Vector2(mergeX - tread * 0.5f, mergeEnd), new Vector2(mergeX + tread * 0.5f, mergeEnd));
            Rail(belts, railMat, h, new Vector2(armL - tread * 0.5f - 0.2f, top), new Vector2(mergeX - w * 0.5f + 0.18f, mergeEnd));
            Rail(belts, railMat, h, new Vector2(armR + tread * 0.5f + 0.2f, top), new Vector2(mergeX + w * 0.5f - 0.18f, mergeEnd));

            // ---- the sorter loop -----------------------------------------------------------

            // A loop like the ring below it -- the sorter, a half circle at each end, the collector --
            // around a solid island, centred on the junction. Its length follows the number of places
            // the level has (CheckInLane.ShowPlaces), so it is a mesh built at run time by CheckInLoop.
            // The bays are lanes across the island between the two runs, one object each, slid into a
            // centred row for however many are shown.
            var islandMat = Plastic("CheckIn_Island", new Color(0.80f, 0.90f, 0.95f), 0.4f);
            var loopObject = new GameObject("Sorter Loop");
            loopObject.transform.SetParent(belts, false);
            var loop = loopObject.AddComponent<CheckInLoop>();
            loop.treadMaterial = treadMat;
            loop.baseMaterial = sideMat;
            loop.railMaterial = railMat;
            loop.islandMaterial = islandMat;
            // The bays are cut into one plinth in the scanner's white, so the row and the scanner
            // read as the same machine. A slot is a touch wider than a bay's lamp rails stand apart
            // (Rail is 0.36 wide), so the rails show on the slot's faces as its lamp strips.
            loop.plinthMaterial = bodyMat;
            loop.slotHalfWidth = BayPitch * 0.5f - 0.3f - 0.18f + 0.05f;
            loop.centreX = mergeX;
            loop.sorterZ = sorterZ;
            loop.collectorZ = collectorZ;
            loop.width = w;
            loop.tread = tread;
            loop.surface = h;
            loop.opening = w * 0.5f;

            // The scanner's feed, from the collector's middle into the tunnel.
            float feedEnd = mouthZ + 2.2f;
            Box(belts, "Feed Base", sideMat, new Vector3(mergeX, baseH * 0.5f, (collectorZ + w * 0.5f + feedEnd) * 0.5f),
                new Vector3(w, baseH, feedEnd - collectorZ - w * 0.5f + 0.1f), 0.1f);
            TreadRun(belts, "Feed Tread", treadMat, h + 0.02f, new Vector3(mergeX, 0f, collectorZ + w * 0.5f), new Vector3(mergeX, 0f, feedEnd), tread);
            Rail(belts, railMat, h, new Vector2(mergeX - w * 0.5f + 0.18f, collectorZ + w * 0.5f), new Vector2(mergeX - w * 0.5f + 0.18f, mouthZ));
            Rail(belts, railMat, h, new Vector2(mergeX + w * 0.5f - 0.18f, collectorZ + w * 0.5f), new Vector2(mergeX + w * 0.5f - 0.18f, mouthZ));

            // The bays. Built around their own middle and placed by CheckInLane. A bay's two side
            // rails are its lamp: they light the state of its place.
            var placeTransforms = new List<Transform>();
            var marks = new List<Renderer>();
            var columns = new List<GameObject>();
            for (int i = 0; i < Bays; i++)
            {
                var column = Node("Bay " + (i + 1), belts);
                columns.Add(column.gameObject);

                TreadRun(column, "Bay " + (i + 1) + " Tread", treadMat, h + 0.02f,
                         new Vector3(0f, 0f, bayStart - 0.3f), new Vector3(0f, 0f, bayStart + BayLength + 0.3f), tread, scroll: false);
                foreach (float side in new[] { -1f, 1f })
                {
                    var rail = Rail(column, lampMat, h, new Vector2(side * (BayPitch * 0.5f - 0.3f), bayStart + 0.25f),
                                    new Vector2(side * (BayPitch * 0.5f - 0.3f), bayStart + BayLength - 0.25f));
                    marks.Add(rail.GetComponent<Renderer>());
                }

                placeTransforms.Add(Point(column, "Place", new Vector3(0f, h, bayZ)));
            }

            float reachMax = (Bays * 0.5f) * BayPitch + (collectorZ - sorterZ) * 0.5f + w * 0.5f;
            float left = mergeX - reachMax, right = mergeX + reachMax;

            // ---- the terminal ------------------------------------------------------------

            // Not called "Terminal": that is the hall's root, and Build Terminal Sandbox finding this
            // by name instead deleted it and left the old hall standing.
            var terminalRoot = Node("Check-in Terminal", root.transform);
            terminalRoot.localPosition = new Vector3(mergeX, 0f, mouthZ);
            // Built with its opening facing local +Z, so turned to face the board.
            terminalRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var visual = Node("Visual", terminalRoot);

            // A baggage scanner: a dark tunnel at belt height with a strip curtain, the cart's colour
            // on its roof and one lamp per seat.
            float bodyW = BayPitch * 2f + 1.6f;
            float bodyH = h + 3.6f;
            Box(visual, "Body", bodyMat, new Vector3(0f, bodyH * 0.5f, -3.0f), new Vector3(bodyW, bodyH, 6f), 0.35f);
            Box(visual, "Post L", bodyMat, new Vector3(-2.9f, bodyH * 0.5f, 0.2f), new Vector3(1f, bodyH, 0.9f), 0.2f);
            Box(visual, "Post R", bodyMat, new Vector3(2.9f, bodyH * 0.5f, 0.2f), new Vector3(1f, bodyH, 0.9f), 0.2f);
            Box(visual, "Opening", darkMat, new Vector3(0f, h + 1.4f, 0.05f), new Vector3(4.8f, 3.0f, 0.2f), 0.05f);
            var curtainMat = Plastic("CheckIn_Curtain", new Color(0.30f, 0.52f, 0.60f), 0.5f);
            for (int i = 0; i < 6; i++)
                Box(visual, "Curtain Strip", curtainMat, new Vector3(-2.0f + i * 0.8f, h + 1.6f, 0.22f), new Vector3(0.7f, 2.4f, 0.06f), 0.02f);

            var roof = Box(visual, "Roof Sign", colourMat, new Vector3(0f, bodyH + 0.55f, -2.6f), new Vector3(bodyW + 0.8f, 1.1f, 7f), 0.4f);
            var arch = Box(visual, "Arch Band", colourMat, new Vector3(0f, bodyH - 0.4f, 0.35f), new Vector3(bodyW, 0.8f, 0.8f), 0.2f);

            var seats = new List<Renderer>();
            for (int i = 0; i < 3; i++)
                seats.Add(Box(visual, $"Seat Lamp {i + 1}", lampMat, new Vector3((i - 1) * 1.9f, bodyH + 1.25f, -2.6f),
                              new Vector3(1.3f, 0.4f, 1.3f), 0.18f).GetComponent<Renderer>());

            // The carts still to come, lined up beside the scanner like passengers outside a
            // station: baggage trolleys, each carrying a case in its cart's colour, nearest first.
            // Local -X is the screen's right once the terminal is turned.
            var line = Node("Carts To Come", terminalRoot);
            var upcoming = new List<Renderer>();
            for (int i = 0; i < UpcomingShown; i++)
            {
                var cart = Node($"Trolley {i + 1}", line);
                cart.localPosition = new Vector3(-(bodyW * 0.5f + 2.0f + i * 3.3f), 0f, -2.4f);
                Box(cart, "Deck", trimMat, new Vector3(0f, 0.55f, 0f), new Vector3(2.6f, 0.25f, 3.4f), 0.1f);
                Box(cart, "Handle Post", trimMat, new Vector3(0f, 1.5f, 1.55f), new Vector3(2.2f, 1.9f, 0.22f), 0.08f);
                Box(cart, "Handle", trimMat, new Vector3(0f, 2.45f, 1.55f), new Vector3(2.6f, 0.3f, 0.36f), 0.12f);
                foreach (var (wx, wz) in new[] { (-1.0f, -1.3f), (1.0f, -1.3f), (-1.0f, 1.3f), (1.0f, 1.3f) })
                    Box(cart, "Wheel", darkMat, new Vector3(wx, 0.25f, wz), new Vector3(0.3f, 0.5f, 0.5f), 0.12f);
                var bag = Box(cart, "Case", colourMat, new Vector3(0f, 1.45f, -0.2f), new Vector3(2.2f, 1.55f, 2.5f), 0.35f);
                Box(bag.transform, "Case Handle", railMat, new Vector3(0f, 0.85f, 0f), new Vector3(0.9f, 0.18f, 0.3f), 0.06f);
                upcoming.Add(bag.GetComponent<Renderer>());
            }

            var terminal = terminalRoot.gameObject.AddComponent<CheckInTerminal>();
            terminal.colourParts = new[] { roof.GetComponent<Renderer>(), arch.GetComponent<Renderer>() };
            terminal.seatLamps = seats.ToArray();
            terminal.upcoming = upcoming.ToArray();
            terminal.punch = visual;

            // ---- framing -----------------------------------------------------------------

            float farEdge = mouthZ + 6.5f;
            var frame = Node("Frame", root.transform);
            lane.frameInclude = new[]
            {
                Point(frame, "Far L", new Vector3(left - 0.5f, h, farEdge)),
                Point(frame, "Far R", new Vector3(right + 0.5f, h, farEdge)),
            };

            // The HUD is laid out around the terminal by SheepGame > UI > Apply GUI Pro Casual
            // Theme: the level is a small pill beside pause, clear of the terminal, so it stays.
            if (board.cameraFramer != null)
            {
                Undo.RecordObject(board.cameraFramer, "Build Check-in Lane");
                board.cameraFramer.includeTop = 0.09f;
                EditorUtility.SetDirty(board.cameraFramer);
            }

            // ---- wiring ------------------------------------------------------------------

            lane.board = board;
            lane.places = placeTransforms.ToArray();
            lane.placeMarks = marks.ToArray();
            lane.placeLength = BayLength * 0.85f;
            lane.terminal = terminal;
            lane.columns = columns.ToArray();
            lane.loop = loop;
            lane.bayPitch = BayPitch;
            // Drawn at the usual opening size in the editor; a level sets its own as it starts.
            var economy = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include)?.economy;
            lane.ShowPlaces(economy != null ? economy.queueSlots : 5, instant: true);

            board.checkIn = lane;
            board.rideBelt = true;
            EditorUtility.SetDirty(board);

            // Paint the idle look into the scene, so the terminal is not a white box in the editor.
            terminal.Show(terminal.idleColour, instant: true);
            terminal.SetSeats(0);
            terminal.SetUpcoming(null, 0);

            // The belt turned, and the dressing's tread has to run the other way with it.
            ConveyorDressing.Dress();

            EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[Check-in] Sorter built: arms at x {armL:0.00} / {armR:0.00}, sorter z {sorterZ:0.00}, " +
                      $"{Bays} bays at z {bayZ:0.00}, collector z {collectorZ:0.00}, scanner z {mouthZ:0.00}, far edge {farEdge:0.00}.");
        }

        struct Junction
        {
            public Bounds turnL, turnR;
            public float joinL, joinR, laneZ, width;
        }

        // The junction's two turns and the straights that run into them, on whichever long edge
        // of the ring they are.
        static bool FindJunction(Transform bt, Transform belt, Board board, out bool back, out Junction junction)
        {
            back = false;
            junction = default;

            var straights = new List<Bounds>();
            var turns = new List<Bounds>();
            foreach (Transform child in belt)
            {
                if (!child.gameObject.activeInHierarchy || !LocalBounds(bt, child, out var b)) continue;
                if (child.name.StartsWith("12.2")) straights.Add(b);
                else if (child.name.StartsWith("12.4")) turns.Add(b);
            }

            foreach (bool atBack in new[] { true, false })
            {
                float edge = atBack ? board.RingBack : board.RingFront;
                var along = straights.Where(b => b.size.x > b.size.z && Mathf.Abs(b.center.z - edge) < 2.5f).ToList();
                var pair = turns.Where(b => Mathf.Abs(b.center.x) < 9f && Mathf.Abs(b.center.z - edge) < 5f)
                                .OrderBy(b => b.center.x).ToList();
                if (along.Count < 2 || pair.Count < 2) continue;

                Bounds turnL = pair[0], turnR = pair[pair.Count - 1];
                var leftStraight = along.Where(b => b.center.x < turnL.center.x).OrderByDescending(b => b.center.x).FirstOrDefault();
                var rightStraight = along.Where(b => b.center.x > turnR.center.x).OrderBy(b => b.center.x).FirstOrDefault();
                if (leftStraight.size == Vector3.zero || rightStraight.size == Vector3.zero) continue;

                back = atBack;
                junction = new Junction
                {
                    turnL = turnL,
                    turnR = turnR,
                    joinL = leftStraight.max.x,
                    joinR = rightStraight.min.x,
                    laneZ = (leftStraight.center.z + rightStraight.center.z) * 0.5f,
                    width = (leftStraight.size.z + rightStraight.size.z) * 0.5f,
                };
                return true;
            }
            return false;
        }

        // ---- route helpers -----------------------------------------------------------------

        // The quarter turn from the ring's straight into an arm, as the dressing draws it: an
        // ellipse whose radii are the two gaps, so the path sits on the band.
        static Transform[] Feed(Transform parent, string side, float joinX, float laneZ, float armX, float outer)
        {
            var holder = Node(side + " Feed", parent);
            var points = new List<Transform>();
            const int steps = 8;
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.PI * 0.5f * (1f - i / (float)steps);
                var p = new Vector3(joinX + (armX - joinX) * Mathf.Cos(a), 0f, outer + (laneZ - outer) * Mathf.Sin(a));
                points.Add(Point(holder, $"{side} {i}", p));
            }
            return points.ToArray();
        }

        static Transform Node(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Transform Point(Transform parent, string name, Vector3 local)
        {
            var t = Node(name, parent);
            // Parents are all at the root's identity, so local here is the board's frame.
            t.localPosition = local - LocalOffset(parent);
            return t;
        }

        static Vector3 LocalOffset(Transform t)
        {
            var offset = Vector3.zero;
            while (t != null && t.GetComponent<CheckInLane>() == null)
            {
                offset += t.localPosition;
                t = t.parent;
            }
            return offset;
        }

        static bool LocalBounds(Transform board, Transform t, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                             (i & 2) == 0 ? b.min.y : b.max.y,
                                             (i & 4) == 0 ? b.min.z : b.max.z);
                    var local = board.InverseTransformPoint(corner);
                    if (!any) { bounds = new Bounds(local, Vector3.zero); any = true; }
                    else bounds.Encapsulate(local);
                }
            }
            return any;
        }

        // ---- geometry ----------------------------------------------------------------------

        static GameObject Box(Transform parent, string name, Material material, Vector3 centre, Vector3 size, float bevel)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.GetComponent<MeshFilter>().sharedMesh = BoxMesh(size, bevel);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static Mesh BoxMesh(Vector3 size, float bevel)
        {
            string key = $"Box_{size.x:0.##}x{size.y:0.##}x{size.z:0.##}_{bevel:0.##}".Replace(',', '.');
            string path = $"{MeshDir}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = RoundedBox.Create(size, bevel);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // A rail between two points on the floor plan, standing on the belt surface.
        static GameObject Rail(Transform parent, Material material, float surface, Vector2 from, Vector2 to)
        {
            var a = new Vector3(from.x, 0f, from.y);
            var b = new Vector3(to.x, 0f, to.y);
            float length = Vector3.Distance(a, b);
            var go = Box(parent, "Rail", material, (a + b) * 0.5f + Vector3.up * (surface + 0.16f),
                         new Vector3(0.36f, 0.42f, length + 0.3f), 0.1f);
            go.transform.localRotation = Quaternion.LookRotation(b - a, Vector3.up);
            return go;
        }

        // A straight run of tread from one point to another on the floor plan, V in world units the
        // way the luggage goes, as the dressing's tread is built.
        static void TreadRun(Transform parent, string name, Material material, float y, Vector3 from, Vector3 to,
                             float width, bool scroll = true, float vStart = 0f)
        {
            var forward = new Vector3(to.x - from.x, 0f, to.z - from.z);
            float length = forward.magnitude;
            forward /= Mathf.Max(0.0001f, length);
            var side = Vector3.Cross(Vector3.up, forward) * width * 0.5f;
            var a = new Vector3(from.x, y, from.z);
            var b = new Vector3(to.x, y, to.z);

            var mesh = new Mesh { name = name };
            var v = new[] { a - side, a + side, b - side, b + side };
            mesh.vertices = v;
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            // V starts where the run before it left off, so a belt built in pieces scrolls as one.
            mesh.uv = new[] { new Vector2(0f, vStart), new Vector2(1f, vStart), new Vector2(0f, vStart + length), new Vector2(1f, vStart + length) };
            var tris = new List<int> { 0, 2, 1, 1, 2, 3 };
            if (Vector3.Cross(v[2] - v[0], v[1] - v[0]).y < 0f) tris = new List<int> { 0, 1, 2, 1, 3, 2 };
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh = SaveMesh(mesh, name);

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (scroll) go.AddComponent<SheepGame.Fx.ScrollingBelt>().speed = 0.5f;
        }

        // A flat quad of tread between two edges (a, b at the start, c, d at the end), for the merge,
        // which narrows as it goes.
        static void Tread(Transform parent, string name, Material material, float y,
                          Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var mesh = new Mesh { name = name };
            var v = new[] { new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y), new Vector3(c.x, y, c.y), new Vector3(d.x, y, d.y) };
            float length = Vector2.Distance((a + b) * 0.5f, (c + d) * 0.5f);
            mesh.vertices = v;
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, length), new Vector2(1f, length) };
            var tris = new List<int> { 0, 2, 1, 1, 2, 3 };
            if (Vector3.Cross(v[2] - v[0], v[1] - v[0]).y < 0f) tris = new List<int> { 0, 1, 2, 1, 3, 2 };
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh = SaveMesh(mesh, name);

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<SheepGame.Fx.ScrollingBelt>().speed = 0.5f;
        }

        // A block with a four-sided floor plan, standing from the floor to the given height.
        static void Prism(Transform parent, string name, Material material, float height, Vector2[] outline)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();

            void Face(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                var n = Vector3.Cross(p1 - p0, p3 - p0).normalized;
                int i = vertices.Count;
                vertices.AddRange(new[] { p0, p1, p2, p3 });
                for (int k = 0; k < 4; k++) normals.Add(n);
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            var up = outline.Select(p => new Vector3(p.x, height, p.y)).ToArray();
            var down = outline.Select(p => new Vector3(p.x, 0f, p.y)).ToArray();

            // Unity draws a triangle wound clockwise as seen from its front. Put the outline in
            // clockwise order seen from above (a negative y here is anticlockwise) rather than
            // trusting the caller.
            bool anticlockwise = Vector3.Cross(up[1] - up[0], up[2] - up[0]).y < 0f;
            if (anticlockwise) { System.Array.Reverse(up); System.Array.Reverse(down); }

            Face(up[0], up[1], up[2], up[3]);
            for (int i = 0; i < 4; i++)
            {
                int k = (i + 1) % 4;
                Face(down[k], up[k], up[i], down[i]);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh = SaveMesh(mesh, name);

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = $"{MeshDir}/{name.Replace(' ', '_')}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ---- look --------------------------------------------------------------------------

        static Material Plastic(string name, Color colour, float smoothness, bool glow = false)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            // sRGB into the asset, as TerminalBuilder writes it.
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_SpecularHighlights", 1f);
            material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");

            if (glow)
            {
                // The colour comes per renderer through a property block; the keyword has to be
                // on in the material or the block's emission is ignored.
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", Color.black);
            }

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
