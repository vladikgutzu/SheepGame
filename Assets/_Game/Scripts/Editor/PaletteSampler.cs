using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // These models are painted by where their UVs land on a palette texture, not by their
    // material, so anything that wants to know "what colour is this part" has to sample the
    // palette per triangle. Both the fill matcher and the shell splitter need that, and they
    // must agree on the answer, so it lives here.
    public static class PaletteSampler
    {
        public class Reading
        {
            /// <summary>Quantised palette colour under each triangle, indexed by triangle.</summary>
            public Color32[] perTriangle;
            /// <summary>Surface area each quantised colour covers.</summary>
            public Dictionary<Color32, float> areaByColour;
            /// <summary>Colours covering the most area, largest first.</summary>
            public List<KeyValuePair<Color32, float>> ranked;
            public float totalArea;

            // A copy of the geometry, not the Mesh it came from. Restoring the model's
            // import settings re-imports it a second time and destroys every Mesh object it
            // holds, so anything still holding that reference is reading a corpse — which
            // looks exactly like a mesh with no triangles in it.
            public string meshName;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector2[] uv;
            public Color[] colors;
            public Vector4[] tangents;
            public int[][] subMeshTriangles;

            public Color32 Dominant => ranked[0].Key;

            public string Report(int count = 3)
            {
                var lines = new List<string>();
                for (int i = 0; i < Mathf.Min(count, ranked.Count); i++)
                    lines.Add($"#{ColorUtility.ToHtmlStringRGB(ranked[i].Key)} {ranked[i].Value / totalArea:P0}");
                return "(" + string.Join(", ", lines) + ")";
            }
        }

        /// <summary>
        /// Samples the palette under every triangle of a mesh. Re-imports the model to read
        /// it if it has to, and puts the import settings back afterwards. Must not be called
        /// while a prefab is open for editing: the re-import replaces the very mesh objects
        /// that prefab points at.
        /// </summary>
        public static Reading Read(Mesh mesh, Material material, out string error)
        {
            error = null;
            if (mesh == null || material == null) { error = "no mesh or material"; return null; }

            var texture = ReadableCopy(material);
            if (texture == null) { error = "material has no readable base map"; return null; }

            string meshPath = AssetDatabase.GetAssetPath(mesh);
            string meshName = mesh.name;

            try
            {
                using (new TemporarilyReadable(meshPath))
                {
                    // Re-fetched by name: making the model readable re-imports it, which
                    // replaces every Mesh object it holds.
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(meshPath))
                        if (asset is Mesh candidate && candidate.name == meshName) { mesh = candidate; break; }

                    var verts = mesh.vertices;
                    var tris = mesh.triangles;
                    var uv = mesh.uv;

                    if (verts.Length == 0 || uv.Length != verts.Length)
                    {
                        error = "mesh has no readable UVs";
                        return null;
                    }

                    var reading = new Reading
                    {
                        meshName = meshName,
                        vertices = verts,
                        normals = mesh.normals,
                        uv = uv,
                        colors = mesh.colors,
                        tangents = mesh.tangents,
                        perTriangle = new Color32[tris.Length / 3],
                        areaByColour = new Dictionary<Color32, float>(),
                    };

                    reading.subMeshTriangles = new int[mesh.subMeshCount][];
                    for (int s = 0; s < mesh.subMeshCount; s++)
                        reading.subMeshTriangles[s] = mesh.GetTriangles(s);

                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                        float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;

                        Vector2 centre = (uv[tris[i]] + uv[tris[i + 1]] + uv[tris[i + 2]]) / 3f;
                        var key = Quantise(texture.GetPixelBilinear(centre.x, centre.y));

                        reading.perTriangle[i / 3] = key;
                        if (area <= 0f) continue;

                        reading.areaByColour.TryGetValue(key, out float total);
                        reading.areaByColour[key] = total + area;
                        reading.totalArea += area;
                    }

                    if (reading.areaByColour.Count == 0) { error = "no textured triangles"; return null; }

                    reading.ranked = new List<KeyValuePair<Color32, float>>(reading.areaByColour);
                    reading.ranked.Sort((x, y) => y.Value.CompareTo(x.Value));
                    return reading;
                }
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// Rounded to a sixty-fourth so neighbouring texels of one swatch group together,
        /// while two swatches never do.
        /// </summary>
        public static Color32 Quantise(Color colour) =>
            new Color32((byte)(Mathf.RoundToInt(colour.r * 255f) / 4 * 4),
                        (byte)(Mathf.RoundToInt(colour.g * 255f) / 4 * 4),
                        (byte)(Mathf.RoundToInt(colour.b * 255f) / 4 * 4), 255);

        public static Color ToColour(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);

        /// <summary>
        /// A colour on its way into a saved material asset needs no conversion: a shader
        /// property of type Color is stored in the file as sRGB and converted on load, so a
        /// value converted here is converted twice and lands far too dark. Measured, not
        /// assumed — #B08860 written straight matches the palette, written as .linear it
        /// comes out the colour of dark chocolate.
        ///
        /// Note this is the opposite of writing a colour to a live material at run time,
        /// which does go to the GPU as given: ApronSurface has to convert and does.
        /// </summary>
        public static Color ForMaterialAsset(Color srgb) => srgb;

        /// <summary>Reads a colour back out of a saved material asset, in the same space
        /// <see cref="ForMaterialAsset"/> writes it.</summary>
        public static Color FromMaterialAsset(Color stored) => stored;

        // Decoded straight from the file, so the texture's import settings are left alone —
        // most palettes ship with Read/Write off and turning it on would touch the art.
        public static Texture2D ReadableCopy(Material material)
        {
            var source = material.HasProperty("_BaseMap")
                ? material.GetTexture("_BaseMap")
                : material.mainTexture;
            if (source == null) return null;

            string path = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!copy.LoadImage(File.ReadAllBytes(path)))
            {
                Object.DestroyImmediate(copy);
                return null;
            }
            return copy;
        }

        /// <summary>
        /// Imported meshes usually ship with Read/Write off. Flipped for the read and put
        /// back, so the model's import settings end the run exactly as they started.
        /// </summary>
        public class TemporarilyReadable : System.IDisposable
        {
            ModelImporter _importer;

            public TemporarilyReadable(string path)
            {
                _importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (_importer == null || _importer.isReadable) { _importer = null; return; }

                _importer.isReadable = true;
                _importer.SaveAndReimport();
            }

            public void Dispose()
            {
                if (_importer == null) return;
                _importer.isReadable = false;
                _importer.SaveAndReimport();
                _importer = null;
            }
        }

        /// <summary>The heaviest mesh renderer under a prefab, which is the model itself
        /// rather than a filler primitive someone parented to it.</summary>
        public static MeshRenderer FindBodyRenderer(GameObject root, params Transform[] ignore)
        {
            MeshRenderer best = null;
            int bestCount = -1;

            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool skip = false;
                foreach (var t in ignore)
                    if (t != null && (renderer.transform == t || renderer.transform.IsChildOf(t))) skip = true;
                if (skip) continue;

                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                if (filter.sharedMesh.vertexCount <= bestCount) continue;

                bestCount = filter.sharedMesh.vertexCount;
                best = renderer;
            }
            return best;
        }

        public static Transform FindChild(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child != root && string.Equals(child.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return child;
            return null;
        }
    }
}
