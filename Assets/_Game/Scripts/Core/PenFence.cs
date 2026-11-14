using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    // A rail fence around the pen, following the outer edge of the ring road so the
    // animals' route out stays inside it and only the opening breaks the line.
    //
    // Generated rather than placed: the bay is rebuilt to fit each level, and a fence
    // laid out by hand would enclose the previous level's boundary. Built from boxes and
    // welded into a single mesh, because a full-size board is a few hundred posts and
    // rails and that is a few hundred draw calls if they stay separate objects.
    [ExecuteAlways]
    [RequireComponent(typeof(ApronSurface))]
    public class PenFence : MonoBehaviour
    {
        public Material material;

        [Header("Posts")]
        [Tooltip("Distance between posts along the run.")]
        public float postSpacing = 9f;
        public float postWidth = 1.2f;
        public float postHeight = 5f;

        [Header("Rails")]
        public float railThickness = 0.6f;
        public float railDepth = 0.5f;
        [Tooltip("Rail heights as a fraction of post height.")]
        [Range(0f, 1f)] public float lowerRail = 0.36f;
        [Range(0f, 1f)] public float upperRail = 0.74f;

        [Tooltip("Sits the fence a little above the shore so it does not z-fight it.")]
        public float lift = 0.05f;

        const string BuiltName = "Fence (generated)";

        GameObject _built;
        static Mesh _box;

        public void Populate(ApronSurface apron)
        {
            Clear();
            if (apron == null || material == null || postSpacing < 1f) return;

            var runs = apron.BoundaryRuns(postSpacing);
            if (runs.Count == 0) return;

            var box = BoxMesh();
            if (box == null) return;

            float baseY = apron.shoreHeight + lift;
            var parts = new List<CombineInstance>();

            foreach (var run in runs)
            {
                for (int i = 0; i < run.Count; i++)
                {
                    Vector3 here = run[i];

                    // Face along the run so posts stay square to the fence line and the
                    // rails meet them flush.
                    Vector3 along = i + 1 < run.Count ? run[i + 1] - here : here - run[i - 1];
                    along.y = 0f;
                    if (along.sqrMagnitude < 0.0001f) continue;
                    Quaternion facing = Quaternion.LookRotation(along.normalized, Vector3.up);

                    parts.Add(Part(box,
                                   here + Vector3.up * (baseY + postHeight * 0.5f),
                                   facing,
                                   new Vector3(postWidth, postHeight, postWidth)));

                    // Rails span to the next post, so the last post in a run carries none
                    // and the fence stops cleanly at the opening.
                    if (i + 1 >= run.Count) continue;

                    Vector3 next = run[i + 1];
                    float span = Vector3.Distance(here, next);
                    Vector3 mid = (here + next) * 0.5f;

                    foreach (float at in new[] { lowerRail, upperRail })
                        parts.Add(Part(box,
                                       mid + Vector3.up * (baseY + postHeight * at),
                                       facing,
                                       new Vector3(railDepth, railThickness, span)));
                }
            }

            if (parts.Count == 0) return;

            var mesh = new Mesh { name = "PenFence (generated)" };
            mesh.indexFormat = parts.Count * 24 > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();

            _built = new GameObject(BuiltName) { hideFlags = HideFlags.DontSave };
            _built.transform.SetParent(transform, false);
            _built.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = _built.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static CombineInstance Part(Mesh box, Vector3 position, Quaternion rotation, Vector3 size) =>
            new CombineInstance { mesh = box, transform = Matrix4x4.TRS(position, rotation, size) };

        // Unity's own cube, borrowed once. Building one by hand would be the same eight
        // corners with more room to get the winding wrong.
        static Mesh BoxMesh()
        {
            if (_box != null) return _box;

            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _box = temp.GetComponent<MeshFilter>().sharedMesh;
            // Immediate: a primitive comes with a collider, and a deferred Destroy would
            // leave a unit cube sitting at the origin catching taps for the rest of the
            // frame the level loads in.
            DestroyImmediate(temp);
            return _box;
        }

        public void Clear()
        {
            _built = null;

            // By name: a domain reload drops the reference but leaves the object behind.
            // Immediate even in play mode, so the old run is gone before the new one is
            // laid rather than overlapping it for a frame.
            var stale = new List<Transform>();
            for (int i = 0; i < transform.childCount; i++)
                if (transform.GetChild(i).name == BuiltName) stale.Add(transform.GetChild(i));

            foreach (var t in stale) DestroyImmediate(t.gameObject);
        }
    }
}
