using System.IO;
using SheepGame.Core;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Wraps the luggage model into a board piece and points the library at it.
    //
    // Re-runnable on purpose: the model is the artist's, so every time it changes shape —
    // a taller handle, a deeper shell, a new pivot — the wrapper it needs changes with it,
    // and rebuilding is one menu click rather than a hunt through the prefab's transforms.
    //
    // What the wrapper is for: Board does Instantiate(prefab, worldPos, Quaternion.identity)
    // and then steers the root, so a model whose own root carries an import rotation, an
    // off-centre pivot or a height offset cannot be a piece as it stands. Everything the
    // model needs doing to it therefore goes on a child, and the root stays identity.
    public static class SuitcasePieceBuilder
    {
        const string ModelPath = "Assets/Airport/prefab/02 _strong_chemodan.prefab";
        const string OutputPath = "Assets/_Game/Prefabs/Airport/Suitcase_Strong.prefab";
        const string LibraryPath = "Assets/_Game/Data/SuitcaseLibrary.asset";

        // Name of the shell inside the model, and of the material on it that takes the
        // level's colour. The rest of the case — trim, lock, wheels, handle — keeps what it
        // was built with, which is what stops a recoloured board looking like painted toys.
        const string BodyRenderer = "Kase";
        const string BodyMaterial = "Sumka";

        // Fallback for the height of the playfield's visible surface above the board plane,
        // used when the tool is run without the scene open. ApronSurface draws its cells at
        // the apron object's own y, and a piece dropped at the board plane instead stands
        // that far into the floor — which the previous luggage got away with and a model
        // with wheels does not.
        const float FallbackFloorHeight = 0.3f;

        // Tap target, as fractions of a cell. Sized from the cell rather than from the
        // model: the shell is barely a third of a cell deep, and a collider that hugged it
        // would be a strip too thin to hit on a phone. Kept low because a tall box leans
        // towards the camera under the board's tilt and starts answering for its neighbour.
        const float TapWidth = 0.86f;
        const float TapHeight = 0.6f;

        static readonly Color[] Palette =
        {
            new Color(0.78f, 0.20f, 0.22f), // red
            new Color(0.16f, 0.44f, 0.52f), // teal
            new Color(0.22f, 0.30f, 0.52f), // navy
            new Color(0.90f, 0.68f, 0.20f), // mustard
            new Color(0.31f, 0.50f, 0.30f), // moss
            new Color(0.48f, 0.26f, 0.46f), // plum
            new Color(0.88f, 0.86f, 0.79f), // bone
            new Color(0.22f, 0.23f, 0.26f), // graphite
        };

        // The pack the theme was built on, kept as a one-click way back. They carry their
        // colours in their own materials, so the palette has to go with them: left in
        // place it would paint all five the same, being aimed at no renderer in particular.
        static readonly string[] KoperPieces =
        {
            "Assets/Airport/prefab/Koper 1.prefab",
            "Assets/Airport/prefab/Koper 2.prefab",
            "Assets/Airport/prefab/Koper 3.prefab",
            "Assets/Airport/prefab/Koper 4.prefab",
            "Assets/Airport/prefab/Koper 5.prefab",
        };

        [MenuItem("SheepGame/Restore Koper Suitcases")]
        public static void RestoreKopers()
        {
            var library = AssetDatabase.LoadAssetAtPath<PieceModelLibrary>(LibraryPath);
            if (library == null)
            {
                Debug.LogError($"SuitcasePieceBuilder: no library at {LibraryPath}");
                return;
            }

            var pieces = new GameObject[KoperPieces.Length];
            for (int i = 0; i < KoperPieces.Length; i++)
            {
                pieces[i] = AssetDatabase.LoadAssetAtPath<GameObject>(KoperPieces[i]);
                if (pieces[i] == null)
                {
                    Debug.LogError($"SuitcasePieceBuilder: missing {KoperPieces[i]}");
                    return;
                }
            }

            library.variants = pieces;
            library.palette = new Color[0];
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();

            Debug.Log($"SuitcasePieceBuilder: library back on {pieces.Length} Koper cases, palette cleared.");
        }

        [MenuItem("SheepGame/Build Suitcase Piece")]
        public static void Execute()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"SuitcasePieceBuilder: no model at {ModelPath}");
                return;
            }

            var root = new GameObject("Suitcase_Strong");
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = "Model";
                instance.transform.SetParent(root.transform, false);
                instance.transform.localPosition = Vector3.zero;
                // The case is modelled facing its own -X, and a piece leaves along +Z, so
                // without this a bag would set off sideways: rolling on the edges of four
                // wheels, showing the camera nothing but its narrow side, and giving the
                // direction badge a strip of lid barely two units deep to sit on.
                instance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                instance.transform.localScale = Vector3.one;

                if (!TryBounds(instance, out Bounds bounds))
                {
                    Debug.LogError("SuitcasePieceBuilder: the model has no renderers");
                    return;
                }

                // Root sits at the origin, so the instance's world bounds are the offset
                // needed to bring the model's footprint over the cell centre and its wheels
                // onto the floor.
                instance.transform.localPosition = new Vector3(
                    -bounds.center.x, FloorHeight() - bounds.min.y, -bounds.center.z);

                float cell = CellSize();
                var collider = root.AddComponent<BoxCollider>();
                collider.size = new Vector3(cell * TapWidth, cell * TapHeight, cell * TapWidth);
                collider.center = new Vector3(0f, cell * TapHeight * 0.5f, 0f);

                var piece = root.AddComponent<PuzzlePiece>();
                piece.bodyRenderer = BodyRenderer;
                piece.bodyMaterial = BodyMaterial;
                // A trolley case rides the belt on its wheels. Flopping it onto a face would
                // drive the extended handle either through the conveyor or straight up in
                // the air, and neither is worth the pose.
                piece.lieOnBelt = false;

                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
                var saved = PrefabUtility.SaveAsPrefabAsset(root, OutputPath);
                PointLibraryAt(saved);

                Debug.Log($"SuitcasePieceBuilder: {OutputPath} built — model " +
                          $"{bounds.size.x:0.00} x {bounds.size.z:0.00}, {bounds.size.y:0.00} tall, " +
                          $"in a {cell:0.0} cell.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static bool TryBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        // Read off the open scene where there is one, so the piece is built against the
        // board it will actually stand on rather than against numbers copied into here.
        static Board FindBoard() => Object.FindFirstObjectByType<Board>();

        static float FloorHeight()
        {
            var board = FindBoard();
            if (board == null || board.apron == null) return FallbackFloorHeight;
            return board.apron.transform.position.y - board.transform.position.y;
        }

        static float CellSize()
        {
            var board = FindBoard();
            return board != null ? board.cellSize : 7f;
        }

        static void PointLibraryAt(GameObject piece)
        {
            var library = AssetDatabase.LoadAssetAtPath<PieceModelLibrary>(LibraryPath);
            if (library == null)
            {
                Debug.LogWarning($"SuitcasePieceBuilder: no library at {LibraryPath}; " +
                                 "the piece was built but nothing spawns it.");
                return;
            }

            library.variants = new[] { piece };
            library.palette = Palette;
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
        }
    }
}
