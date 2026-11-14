using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    // Dresses the generated shore. Because the bay is rebuilt per level, hand-placed
    // props would sit where the previous level's bank used to be — on a small board the
    // camera closes in and static decor slides out of frame entirely. Scattering along
    // the contour keeps the framing correct at every size.
    [ExecuteAlways]
    [RequireComponent(typeof(ApronSurface))]
    public class ShoreDecor : MonoBehaviour
    {
        [System.Serializable]
        public class Layer
        {
            public string name = "layer";
            public GameObject[] prefabs;
            [Tooltip("Approximate spacing along the bank, in units.")]
            public float spacing = 22f;
            public float minScale = 1.6f;
            public float maxScale = 2.4f;
            [Tooltip("Sideways offset across the bank: 0 is the inner edge, 1 the outer.")]
            [Range(0f, 1f)] public float across = 0.5f;
            [Range(0f, 1f)] public float acrossJitter = 0.35f;
            public float lift;
        }

        public Layer[] layers;

        [Header("Bay mouth")]
        [Tooltip("Landmarks planted on the two headlands either side of the opening, so " +
                 "the exit reads as a gate. Anchored to the gap, so they follow it when " +
                 "the bay resizes.")]
        public GameObject[] gateProps;
        public float gateScale = 2.6f;
        [Tooltip("How far back from the headland tip to set them.")]
        public float gateInset = 8f;

        public int seed = 2027;
        [Tooltip("Chance a slot is left empty, so the bank does not read as a fence.")]
        [Range(0f, 0.9f)] public float skipChance = 0.25f;

        // Everything generated goes under one named container. Sweeping the apron for
        // loose DontSave children instead would also carry off whatever else generates
        // content onto the same object — the fence did exactly that, silently.
        const string ContainerName = "ShoreDecor (generated)";

        Transform _container;

        public void Populate(ApronSurface apron)
        {
            Clear();
            if (layers == null || apron == null) return;

            var line = apron.ShoreCentreLine;
            if (line == null || line.Count < 4) return;

            // Arc length along the bank, so spacing is in units rather than in samples
            // (corners are sampled far more densely than the straights).
            var distance = new List<float>(line.Count) { 0f };
            float total = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                total += Vector3.Distance(line[i - 1], line[i]);
                distance.Add(total);
            }
            if (total < 1f) return;

            var rng = new System.Random(seed);
            float halfWidth = apron.shoreWidth * 0.5f;

            foreach (var layer in layers)
            {
                if (layer == null || layer.prefabs == null || layer.prefabs.Length == 0) continue;
                if (layer.spacing < 1f) continue;

                for (float d = 0f; d < total; d += layer.spacing)
                {
                    if (rng.NextDouble() < skipChance) continue;

                    int i = IndexAt(distance, d);
                    Vector3 here = line[i];
                    Vector3 next = line[(i + 1) % line.Count];

                    Vector3 along = (next - here).sqrMagnitude > 0.0001f ? (next - here).normalized : Vector3.forward;
                    Vector3 outward = Vector3.Cross(Vector3.up, along).normalized;

                    float across = (layer.across - 0.5f + Rand(rng, -layer.acrossJitter, layer.acrossJitter)) * apron.shoreWidth;
                    Vector3 local = here + outward * Mathf.Clamp(across, -halfWidth * 0.95f, halfWidth * 0.95f);
                    local.y = apron.shoreHeight + layer.lift;

                    var prefab = layer.prefabs[rng.Next(layer.prefabs.Length)];
                    if (prefab == null) continue;

                    var go = Instantiate(prefab, Container());
                    go.transform.localPosition = local;
                    go.transform.localRotation = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);
                    go.transform.localScale = Vector3.one * Rand(rng, layer.minScale, layer.maxScale);
                }
            }

            PlaceGate(line, rng);
        }

        // The shore ring is generated with the mouth quads omitted, so the two ends of
        // the opening show up as the single largest jump between consecutive samples.
        void PlaceGate(IReadOnlyList<Vector3> line, System.Random rng)
        {
            if (gateProps == null || gateProps.Length == 0 || line.Count < 4) return;

            int breakAt = -1;
            float widest = 0f;
            for (int i = 0; i < line.Count; i++)
            {
                float d = Vector3.Distance(line[i], line[(i + 1) % line.Count]);
                if (d > widest) { widest = d; breakAt = i; }
            }
            if (breakAt < 0) return;

            int after = (breakAt + 1) % line.Count;
            Vector3 inward = (line[Mathf.Max(breakAt - 1, 0)] - line[breakAt]).normalized;
            Vector3 inwardOther = (line[(after + 1) % line.Count] - line[after]).normalized;

            Spawn(gateProps[rng.Next(gateProps.Length)], line[breakAt] + inward * gateInset, rng);
            Spawn(gateProps[rng.Next(gateProps.Length)], line[after] + inwardOther * gateInset, rng);
        }

        void Spawn(GameObject prefab, Vector3 local, System.Random rng)
        {
            if (prefab == null) return;
            var go = Instantiate(prefab, Container());
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);
            go.transform.localScale = Vector3.one * gateScale;
        }

        Transform Container()
        {
            if (_container != null) return _container;

            var go = new GameObject(ContainerName) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            _container = go.transform;
            return _container;
        }

        static int IndexAt(List<float> distance, float d)
        {
            int lo = 0, hi = distance.Count - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) / 2;
                if (distance[mid] <= d) lo = mid; else hi = mid;
            }
            return lo;
        }

        static float Rand(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public void Clear()
        {
            _container = null;

            // By name, not by hide flags: a domain reload drops the reference but leaves
            // the container, and matching on flags alone would also sweep up the fence.
            var stale = new List<Transform>();
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name == ContainerName) stale.Add(child);
            }
            // Immediate even in play mode: a deferred Destroy would leave the old props
            // standing for the rest of the frame while the new ones are already being
            // placed, and the level would be dressed twice over.
            foreach (var t in stale) DestroyImmediate(t.gameObject);
        }
    }
}
