using System.Text;
using UnityEngine;
using UnityEditor;

public static class ClaudeDumpHud
{
    public static string Execute()
    {
        var sb = new StringBuilder();
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas) continue;
            var scaler = c.GetComponent<UnityEngine.UI.CanvasScaler>();
            sb.AppendLine($"CANVAS {c.name} active={c.gameObject.activeInHierarchy} ref={(scaler ? scaler.referenceResolution.ToString() : "-")}");
            Dump(c.transform, 1, sb);
        }
        System.IO.File.WriteAllText(@"C:\Users\kaktu5\AppData\Local\Temp\claude\F--Unity-SheepGame\0fd5d776-ca9e-42f9-87f2-bb930bc29874\scratchpad\hud.txt", sb.ToString());
        return "ok " + sb.Length;
    }

    static void Dump(Transform t, int depth, StringBuilder sb)
    {
        if (depth > 4) return;
        foreach (Transform ch in t)
        {
            var r = ch as RectTransform;
            string rect = r ? $"amin={r.anchorMin} amax={r.anchorMax} pos={r.anchoredPosition} size={r.sizeDelta}" : "";
            sb.AppendLine($"{new string(' ', depth * 2)}{ch.name} [{(ch.gameObject.activeSelf ? "on" : "off")}] {rect}");
            Dump(ch, depth + 1, sb);
        }
    }
}
