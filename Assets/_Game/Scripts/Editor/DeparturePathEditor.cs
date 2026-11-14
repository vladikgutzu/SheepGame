using SheepGame.Core;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    [CustomEditor(typeof(DeparturePath))]
    public class DeparturePathEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var path = (DeparturePath)target;

            DrawDefaultInspector();
            EditorGUILayout.Space(6f);

            path.Rebuild();
            EditorGUILayout.HelpBox(
                path.IsValid
                    ? $"{path.transform.childCount} waypoints, {path.Length:F0} units long.\n" +
                      "Drag the child objects in the scene to reshape the route. " +
                      "Yellow = taxi, orange = takeoff roll, cyan = climb-out."
                    : "Needs at least 2 waypoints. Use Append below.",
                path.IsValid ? MessageType.Info : MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Append waypoint")) Append(path);

                using (new EditorGUI.DisabledScope(path.transform.childCount == 0))
                    if (GUILayout.Button("Delete last"))
                    {
                        var last = path.transform.GetChild(path.transform.childCount - 1).gameObject;
                        Undo.DestroyObjectImmediate(last);
                    }

                if (GUILayout.Button("Renumber")) Renumber(path);
            }

            if (GUILayout.Button("Select all waypoints"))
            {
                var objs = new Object[path.transform.childCount];
                for (int i = 0; i < objs.Length; i++) objs[i] = path.transform.GetChild(i).gameObject;
                Selection.objects = objs;
            }
        }

        // New points continue in the direction the route was already heading, so
        // appending several in a row lays out a usable line instead of a pile.
        static void Append(DeparturePath path)
        {
            int count = path.transform.childCount;
            Vector3 position;

            if (count == 0) position = path.transform.position;
            else if (count == 1) position = path.transform.GetChild(0).position + path.transform.forward * 40f;
            else
            {
                Vector3 last = path.transform.GetChild(count - 1).position;
                Vector3 prev = path.transform.GetChild(count - 2).position;
                Vector3 dir = (last - prev).sqrMagnitude > 0.001f ? (last - prev).normalized : path.transform.forward;
                position = last + dir * Vector3.Distance(last, prev);
            }

            var go = new GameObject($"WP_{count:00}");
            Undo.RegisterCreatedObjectUndo(go, "Append waypoint");
            go.transform.SetParent(path.transform, false);
            go.transform.position = position;
            Selection.activeGameObject = go;
        }

        static void Renumber(DeparturePath path)
        {
            for (int i = 0; i < path.transform.childCount; i++)
            {
                var child = path.transform.GetChild(i);
                Undo.RecordObject(child.gameObject, "Renumber waypoints");
                child.gameObject.name = $"WP_{i:00}";
            }
        }
    }
}
