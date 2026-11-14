using UnityEngine;
using UnityEditor;

public static class ClaudeShot
{
    const string Dir = @"C:\Users\kaktu5\AppData\Local\Temp\claude\F--Unity-SheepGame\0fd5d776-ca9e-42f9-87f2-bb930bc29874\scratchpad\";

    public static string Start()
    {
        var menu = Object.FindFirstObjectByType<SheepGame.Core.MainMenuPanel>(FindObjectsInactive.Include);
        if (menu == null) return "no menu";
        menu.playButton.onClick.Invoke();
        return "started";
    }

    public static string Execute()
    {
        var canvasGo = GameObject.Find("MainSystemn/UICanvas");
        var cv = canvasGo.GetComponent<Canvas>();
        var camera = Camera.main;
        const int W = 1080, H = 2340;
        var mode = cv.renderMode; var wc = cv.worldCamera; float pd = cv.planeDistance;
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(W, H, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt; camera.aspect = (float)W / H;
            cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = camera; cv.planeDistance = camera.nearClipPlane + 1;
            Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, W, H), 0, 0); image.Apply();
            System.IO.File.WriteAllBytes(Dir + "gameplay.png", image.EncodeToPNG());
        }
        finally
        {
            cv.renderMode = mode; cv.worldCamera = wc; cv.planeDistance = pd;
            camera.targetTexture = oldTarget; camera.ResetAspect(); RenderTexture.active = oldActive;
            Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt);
        }
        return "shot";
    }
}
