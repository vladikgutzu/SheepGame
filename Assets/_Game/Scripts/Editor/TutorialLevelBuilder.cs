using System.Collections.Generic;
using SheepGame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Builds the first level and puts it at the head of the play order.
    //
    // Every other level is a filled silhouette spanning the fixed 7x10 field, drawn that way
    // because a small level centred in seventy cells reads as a blob in the middle of an
    // empty floor (see LevelShapeBuilder). The tutorial is the deliberate exception: three
    // cases and nothing else, so the one thing being taught — the arrow on the lid decides
    // where the case goes, and it only goes if the way is clear — is the only thing on
    // screen. It escapes the blob problem through LevelData.frameOnPieces, which zooms the
    // camera onto the pieces instead of the field, rather than by adding cases it does not need.
    public static class TutorialLevelBuilder
    {
        const string LevelDir = "Assets/_Game/Data/Levels";
        const string AssetPath = LevelDir + "/Level_00_Tutorial.asset";

        // Three in a row on a 3x3 grid, centred into the field at load. A row is the
        // smallest arrangement in which the facing matters: whichever way the middle case
        // points along the row, a neighbour is in the way until that neighbour leaves —
        // so the player sees an arrow, sees a case refuse to move, and sees the same case
        // leave once the board has changed. Two cases could not show that.
        static readonly Vector2Int[] Cells =
        {
            new Vector2Int(0, 1),
            new Vector2Int(1, 1),
            new Vector2Int(2, 1)
        };

        const int GridWidth = 3;
        const int GridHeight = 3;

        // Superseded by SheepGame > Build Handcrafted Levels, whose level 1 is the tutorial now.
        // Running this would push every handcrafted level back by one, and with them the levels
        // the boosters are handed over on.
        [MenuItem("SheepGame/Legacy/Build Old Tutorial Level")]
        public static void Build()
        {
            var level = CreateOrUpdateAsset();
            if (level == null) return;

            int inserted = InsertIntoPlayOrder(level);

            AssetDatabase.SaveAssets();
            Selection.activeObject = level;

            Debug.Log(inserted switch
            {
                1 => "Built the tutorial level and made it level 1. Every other level has " +
                     "shifted up by one, so an existing save resumes one level earlier than it left off.",
                0 => "Rebuilt the tutorial level; it was already level 1.",
                _ => "Built the tutorial level. No GameManager in the open scene, so it is " +
                     "not in the play order yet — drop it at the top of GameManager.levels."
            });
        }

        static LevelData CreateOrUpdateAsset()
        {
            var cells = new List<Vector2Int>(Cells);

            // Facings come from the solver playing the board backwards, so the level cannot
            // be unsolvable however the three end up pointing.
            if (!LevelSolver.TryAssignDirections(cells, GridWidth, GridHeight, 20260902, out var facing))
            {
                Debug.LogError("Tutorial: the solver could not place three cases in a row. " +
                               "That should not be possible; check LevelSolver.");
                return null;
            }

            // Rewritten in place, never replaced: GameManager.levels holds levels by GUID,
            // so deleting and recreating the asset would empty the slot it sits in.
            var level = AssetDatabase.LoadAssetAtPath<LevelData>(AssetPath);
            bool created = level == null;
            if (created)
            {
                EnsureFolder();
                level = ScriptableObject.CreateInstance<LevelData>();
            }

            Undo.RecordObject(level, "Build Tutorial Level");

            level.gridWidth = GridWidth;
            level.gridHeight = GridHeight;
            level.pieces = new List<LevelPieceEntry>();

            foreach (var cell in cells)
            {
                level.pieces.Add(new LevelPieceEntry
                {
                    position = cell,
                    direction = facing[cell],
                    // One model for all three: the lesson is the arrow, and three different
                    // cases invite the player to look for meaning in the difference.
                    variantIndex = 0
                });
            }

            // A tutorial the player can fail is not a tutorial.
            level.tapBudget = -1;

            // Framed like every other level, on the whole field. Zooming onto three cases
            // made them large and readable but cropped the belt to a sliver, and the belt is
            // half of what a first level has to teach: the case does not vanish, it leaves
            // along the lane and out through the mouth. Small cases with the route visible
            // beat big cases going nowhere.
            level.frameOnPieces = false;
            level.tutorialHints = true;

            if (created) AssetDatabase.CreateAsset(level, AssetPath);
            EditorUtility.SetDirty(level);

            if (!LevelSolver.TrySolve(level, out var stuck))
            {
                Debug.LogError($"Tutorial: {stuck.Count} case(s) cannot leave. The asset was " +
                               "still written so it can be inspected.");
            }

            return level;
        }

        /// <summary>1 if it was put at the front, 0 if already there, -1 if there is no GameManager.</summary>
        static int InsertIntoPlayOrder(LevelData level)
        {
            var gm = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            if (gm == null) return -1;

            if (gm.levels != null && gm.levels.Count > 0 && gm.levels[0] == level) return 0;

            Undo.RecordObject(gm, "Build Tutorial Level");
            if (gm.levels == null) gm.levels = new List<LevelData>();

            // Removed first, so re-running never leaves two copies in the order.
            gm.levels.Remove(level);
            gm.levels.Insert(0, level);

            EditorUtility.SetDirty(gm);
            EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);
            return 1;
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Data"))
                AssetDatabase.CreateFolder("Assets/_Game", "Data");
            if (!AssetDatabase.IsValidFolder(LevelDir))
                AssetDatabase.CreateFolder("Assets/_Game/Data", "Levels");
        }
    }
}
