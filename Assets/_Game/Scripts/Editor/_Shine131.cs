using System.IO;
using System.Linq;
using SheepGame.Core;
using UnityEngine;

// The sweep is on for half a second in every three, so a screenshot catches it about one
// time in six. Forcing the period down to the sweep's own length makes it continuous for
// as long as it takes to look at it.
public static class _Shine131
{
    public static void Execute()
    {
        var shines = Object.FindObjectsByType<UIShine>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var shine in shines) shine.period = shine.duration;
        Debug.Log($"_Shine131: {shines.Length} shine(s) forced continuous — " +
                  string.Join(", ", shines.Select(s => s.transform.parent.parent.name)));

        var file = Path.Combine(Path.GetTempPath(), "shine131.png");
        if (File.Exists(file)) File.Delete(file);
        ScreenCapture.CaptureScreenshot(file);
    }

    public static void Restore()
    {
        foreach (var shine in Object.FindObjectsByType<UIShine>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            shine.period = 3.2f;
    }
}
