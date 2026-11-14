using SheepGame.Core;
using SheepGame.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class _Rebuild130
{
    public static void Execute()
    {
        TerminalBuilder.Build();

        var board = Object.FindFirstObjectByType<Board>(FindObjectsInactive.Include);
        var band = GameObject.Find("Terminal/Shell/PlayfieldBand");
        if (board != null && band != null) BoardSceneSetup.FitBoardTo(band);

        var framer = board != null ? board.cameraFramer : null;
        if (framer != null)
        {
            framer.fillWidth = 1.42f;
            framer.fillHeight = 1.18f;
            framer.focusShift = 0f;
            EditorUtility.SetDirty(framer);
        }

        ConveyorDressing.Dress();
        GameUIBuilder.Build();
        PieceEffectsBuilder.Build();

        Lightmapping.BakeReflectionProbe(
            Object.FindFirstObjectByType<ReflectionProbe>(FindObjectsInactive.Include),
            "Assets/_Game/Textures/Terminal_Probe.exr");

        EditorSceneManager.SaveOpenScenes();
        Debug.Log("_Rebuild130 done");
    }
}
