using SheepGame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Adopts a hand-assembled belt into the board and fits it there and then, so the belt
    // can be seen sitting on the route without entering play mode.
    //
    // The fitting itself lives in BeltRing, on the belt: it used to be here, which meant a
    // belt only ever matched the board if somebody remembered to run this after every move,
    // resize and re-theme — and it stopped matching four times before it became the board's
    // own job. This is now the one-off adoption plus a manual re-fit, nothing more.
    public static class BaggageBeltFitter
    {
        const string BeltName = "BaggageBelt";

        [MenuItem("SheepGame/Fit Baggage Belt")]
        public static void Fit()
        {
            var board = Object.FindFirstObjectByType<Board>();
            if (board == null)
            {
                EditorUtility.DisplayDialog("Fit Baggage Belt", "No Board in the open scene.", "OK");
                return;
            }

            var belt = board.belt;
            if (belt == null) belt = board.GetComponentInChildren<BeltRing>();
            if (belt == null)
            {
                var loose = GameObject.Find(BeltName);
                if (loose == null)
                {
                    EditorUtility.DisplayDialog("Fit Baggage Belt",
                        $"No '{BeltName}' object in the open scene.", "OK");
                    return;
                }

                belt = loose.GetComponent<BeltRing>();
                if (belt == null) belt = Undo.AddComponent<BeltRing>(loose);

                // Part of the board rather than a neighbour of it: parented, the belt
                // follows the board being moved onto new ground, and Board finds it in its
                // own children without anything being wired up by hand.
                Undo.SetTransformParent(belt.transform, board.transform, "Adopt baggage belt");
            }

            Undo.RecordObject(board, "Fit baggage belt");
            board.belt = belt;
            board.FitBelt();

            EditorUtility.SetDirty(board);
            EditorUtility.SetDirty(belt);
            EditorSceneManager.MarkSceneDirty(board.gameObject.scene);

            var lanes = new System.Text.StringBuilder();
            foreach (var lane in belt.Outfeeds) lanes.Append($" x={lane.position.x:0.#}");
            Debug.Log($"[BELT] fitted to the board — surface y={belt.SurfaceHeight:0.##}, " +
                      $"board-local height {board.beltHeight:0.##}, " +
                      $"{belt.Outfeeds.Count} outfeed lane(s):{lanes}");
        }
    }
}
