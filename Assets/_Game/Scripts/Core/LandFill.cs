using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    // Fills everything outside the bay with the art pack's own ground meshes, leaving
    // water only inside the harbour and in the channel running out through the mouth.
    // Without this the bay reads as a ring-shaped island afloat in open sea, with the
    // edge of the land visible at the corners of the frame.
    //
    // The patches are welded into one mesh rather than left as objects: a fill dense
    // enough to look solid runs to several hundred pieces, and they are rebuilt every
    // time a level loads.
    [ExecuteAlways]
    [RequireComponent(typeof(ApronSurface))]
    public class LandFill : MonoBehaviour
    {
        [Tooltip("Ground meshes from the art pack. Mixed at random per patch.")]
        public GameObject[] groundPrefabs;

        [Tooltip("How far past the bay to keep laying land. Needs to reach beyond the " +
                 "camera frame or the shoreline's outer edge shows up on screen.")]
        public float reach = 190f;
        [Tooltip("Grid step between patches. Smaller than the patch size, so they overlap " +
                 "into continuous ground with a ragged natural edge.")]
        public float spacing = 24f;
        public float jitter = 7f;
        public float minScale = 3.5f;
        public float maxScale = 6f;
        public float height = 0.45f;

        [Tooltip("Half width of the water channel cut outwards from the bay mouth.")]
        public float channelHalfWidth = 26f;
        [Tooltip("Clearance kept between the land and the bay, so patches do not spill " +
                 "over the shore into the harbour.")]
        public float bayClearance = 6f;

        public int seed = 4242;

        GameObject _mesh;

        public void Populate(ApronSurface apron, float halfW, float halfH, float shoreOuterRadius)
        {
            Clear();
            if (groundPrefabs == null || groundPrefabs.Length == 0) return;

            var rng = new System.Random(seed);
            var combine = new List<CombineInstance>();
            Material material = null;

            var half = new Vector2(halfW, halfH);
            float xLimit = halfW + reach;
            float zLimit = halfH + reach;

            for (float x = -xLimit; x <= xLimit; x += spacing)
                for (float z = -zLimit; z <= zLimit; z += spacing)
                {
                    var p = new Vector2(x + Rand(rng, -jitter, jitter), z + Rand(rng, -jitter, jitter));

                    // Keep out of the harbour itself...
                    if (RoundedBoxDistance(p, half, shoreOuterRadius) < bayClearance) continue;
                    // ...and out of the channel leading away from the mouth.
                    if (p.y < 0f && Mathf.Abs(p.x) < channelHalfWidth) continue;

                    var prefab = groundPrefabs[rng.Next(groundPrefabs.Length)];
                    if (prefab == null) continue;

                    var matrix = Matrix4x4.TRS(
                        new Vector3(p.x, height - Rand(rng, 0f, 0.25f), p.y),
                        Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f),
                        Vector3.one * Rand(rng, minScale, maxScale));

                    foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
                    {
                        if (filter.sharedMesh == null) continue;
                        if (material == null)
                        {
                            var r = filter.GetComponent<MeshRenderer>();
                            if (r != null) material = r.sharedMaterial;
                        }
                        combine.Add(new CombineInstance
                        {
                            mesh = filter.sharedMesh,
                            transform = matrix * prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix,
                        });
                    }
                }

            if (combine.Count == 0) return;

            var mesh = new Mesh { name = "Land (generated)" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.CombineMeshes(combine.ToArray(), true, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _mesh = new GameObject("Land") { hideFlags = HideFlags.DontSave };
            _mesh.transform.SetParent(transform, false);
            _mesh.transform.localPosition = Vector3.zero;
            _mesh.AddComponent<MeshFilter>().sharedMesh = mesh;
            _mesh.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        // Signed distance to a rounded rectangle: negative inside, positive outside.
        static float RoundedBoxDistance(Vector2 p, Vector2 half, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x) - half.x + radius, Mathf.Abs(p.y) - half.y + radius);
            return Mathf.Min(Mathf.Max(q.x, q.y), 0f)
                 + new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude
                 - radius;
        }

        static float Rand(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public void Clear()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
                _mesh = null;
            }

            // Survivors of a previous domain reload still hang off this object.
            var stale = new List<Transform>();
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name == "Land") stale.Add(child);
            }
            foreach (var t in stale)
            {
                if (Application.isPlaying) Destroy(t.gameObject); else DestroyImmediate(t.gameObject);
            }
        }
    }
}
