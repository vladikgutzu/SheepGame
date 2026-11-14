using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SheepGame.Core;
using SheepGame.Core.Meta;

public static class FreshPlayerSetup
{
    [MenuItem("SheepGame/Debug/Prepare Fresh Player")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Stop Play mode before resetting the player.");
        var manager=Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if(!manager)throw new System.InvalidOperationException("GameManager missing.");
        Undo.RecordObject(manager,"First launch configuration");
        manager.startingLevel=1;
        manager.ignoreSavedProgress=false;
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        EditorSceneManager.SaveScene(manager.gameObject.scene);
        GameState.ResetAll();
        SheepGame.EditorTools.EconomySetup.ClearReceipts();
        if(SaveSystem.FileExists)throw new System.InvalidOperationException("Save was not deleted.");
        Debug.Log("Fresh player ready: save and local test receipts removed; next launch starts level 1.");
    }
}
