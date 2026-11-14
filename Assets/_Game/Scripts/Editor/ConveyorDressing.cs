using System.Collections.Generic;
using System.Linq;
using SheepGame.Core;
using SheepGame.Fx;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Makes the conveyor look like it is running.
    //
    // The pack's belt modules cannot do it themselves: they are mapped into a shared palette
    // atlas, so sliding their UVs walks them through the palette and recolours them rather
    // than moving anything. So a moving surface is laid over them.
    //
    // It is built **per module**, not along an idealised ring. The first version drew a
    // closed rounded rectangle through the lane's centre line, which the numbers said was
    // exactly where the modules are — and it still did not match, because the conveyor is
    // not a ring: on the near side it opens into a Y and runs off the board as two outfeed
    // spurs, and no rectangle has that shape. A photograph from straight above settled it
    // in one look after two rounds of measuring the wrong thing. Reading the modules means
    // the band is whatever shape the belt is, including the day someone re-assembles it.
    //
    // Built as its own scene root rather than under Terminal, because rebuilding the hall
    // destroys that root, and rather than under the belt, because BeltRing scales that
    // transform to fit the ring and would scale this with it.
    public static class ConveyorDressing
    {
        const string RootName = "Conveyor Dressing";
        const string MeshPath = "Assets/_Game/Meshes/Shell/BeltSurface.asset";
        const string LampMeshPath = "Assets/_Game/Meshes/Shell/BeltLamp.asset";

        [MenuItem("SheepGame/Dress Conveyor")]
        public static void Dress()
        {
            var board = Object.FindFirstObjectByType<Board>(FindObjectsInactive.Include);
            if (board == null) { Debug.LogError("No Board in the scene."); return; }

            var old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(RootName);
            var centre = board.transform.position;

            // Measured off the modules, not from board.beltHeight and not by calling FitBelt
            // to populate it. That readout is zero outside play, and this tool has no
            // business moving the belt to find out: an earlier version called FitBelt and
            // the belt settled from 2.83 to its fitted 1.98 the first time, which looks
            // exactly like a bug in the dressing. (FitBelt itself is fine — five calls in a
            // row were checked and give the same answer.) The modules are in the scene and
            // their tops are the surface.
            var tops = board.belt.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.name.StartsWith("12.2"))
                .Select(r => r.bounds.max.y)
                .ToArray();
            if (tops.Length == 0) { Debug.LogError("No belt modules to measure."); return; }
            float surface = tops.OrderBy(t => t).ElementAt(tops.Length / 2);   // median

            // Straights only. Corners get arcs built from where the straights end — a flat
            // quad over a curved corner module reads as a facet, and the whole point of the
            // exercise is that the band is the belt's shape. The outfeed portal excludes
            // itself by the height test: it stands in the junction and its top is at 7.4
            // against the belt's 2.8.
            var straights = board.belt.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.name.StartsWith("12.2"))
                .Select(r => r.bounds)
                .Where(b => Mathf.Abs(b.max.y - surface) < 0.4f)
                .ToArray();

            if (straights.Length == 0) { Debug.LogError("No belt modules found at the belt surface."); return; }

            // Every turn the belt makes, not only the four the ring has: the outfeed branches
            // off through corner modules of its own, and those were left bare.
            var turns = board.belt.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.name.StartsWith("12.4"))
                .Select(r => r.bounds)
                .Where(b => Mathf.Abs(b.max.y - surface) < 0.4f)
                .ToArray();

            // The junction's turns lead to the outfeed, not to another straight, so the lane
            // they hand over to has no module to measure — the mouths do instead.
            //
            // Board.outfeeds is a readout of the belt fit and `Fit Board To Ground` clears
            // it, so running this straight after a fit found none and silently skipped the
            // junction's two turns. The markers themselves are still in the scene under the
            // belt, so fall back to those.
            var mouths = (board.outfeeds ?? new Transform[0]).Where(o => o != null)
                                                             .Select(o => o.position)
                                                             .ToArray();
            if (mouths.Length == 0)
                mouths = board.belt.GetComponentsInChildren<Transform>(true)
                                   .Where(t => t.name.StartsWith("Outfeed"))
                                   .Select(t => t.position)
                                   .ToArray();

            // The portals the spurs run into. The belt between the junction and them is not
            // a module of any kind, so nothing was covering it.
            // Active ones only: the check-in lane switches the portals off and lays its own
            // band from the junction onwards, and a strip run out to a hidden portal would
            // float over the lane.
            var portals = board.belt.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.name.StartsWith("12.3") && r.gameObject.activeInHierarchy)
                .Select(r => r.bounds)
                .ToArray();

            // The board's own axes. Which way the belt runs is a fact about the board's
            // front, not about world X, and the board is a quarter turn under the world.
            var across = Vector3.ProjectOnPlane(board.transform.right, Vector3.up).normalized;
            var along = Vector3.ProjectOnPlane(board.transform.forward, Vector3.up).normalized;

            // Which edge the junction is on: the check-in builder turns the belt so that it is at
            // the back. Told by the two turns near the middle of an edge.
            var junctionTurns = turns.Where(b => Mathf.Abs(Vector3.Dot(b.center - centre, across)) < 9f).ToArray();
            _junctionSign = junctionTurns.Length > 0 && Vector3.Dot(junctionTurns[0].center - centre, along) > 0f ? 1f : -1f;

            int corners = BuildSurface(root.transform, straights, turns, mouths, portals,
                                       centre, surface + 0.02f, across, along);
            BuildLamps(root.transform, straights, centre, surface + 0.02f);

            Selection.activeGameObject = root;
            Debug.Log($"Conveyor dressed: {straights.Length} straight modules and {corners} corner arcs " +
                      $"at y {surface + 0.02f:0.00}.");
        }

        // One quad per module, all in one mesh so the whole belt is a single draw.
        //
        // Each quad's V axis runs the way the luggage does — worked out from where the
        // module sits relative to the middle of the board, so the top surface circulates
        // one way round the loop and the spurs run outwards. V is in world units, so the
        // tread keeps its pitch whatever length a module was stretched to, and one shared
        // scroll offset moves every quad in step.
        // Which way the belt runs under a given module, in the board's axes.
        //
        // Board.BuildRingRoute is the authority and it says: every route ends at the board's
        // **front** edge, where the two mouths are. A piece on a side edge runs to the front;
        // a piece that got on at the back runs out to whichever side it was routed round.
        // So the flow **diverges at the middle of the back edge and converges at the middle
        // of the front edge** — the belt is two runs, not one loop. A single circulation, as
        // the first version had, is right for one half of the board and backwards for the
        // other, which is what "the belt goes one way and the suitcases go two" was.
        // -1 with the junction on the front edge, +1 on the back. Set by Dress.
        static float _junctionSign = -1f;

        static Vector3 Flow(Bounds plate, Vector3 board, Vector3 across, Vector3 along)
        {
            var offset = plate.center - board;
            float lx = Vector3.Dot(offset, across);
            float lz = Vector3.Dot(offset, along);

            float spanX = Mathf.Abs(Vector3.Dot(plate.size, Abs(across)));
            float spanZ = Mathf.Abs(Vector3.Dot(plate.size, Abs(along)));

            // Along the front or back edge: towards the middle on the junction's edge, away from
            // it on the other, which the product of the signs gives in one line.
            if (spanX > spanZ) return across * Mathf.Sign(lx) * Mathf.Sign(lz) * -_junctionSign;

            // Down a side edge: always towards the junction's edge.
            return along * _junctionSign;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static int BuildSurface(Transform parent, Bounds[] plates, Bounds[] turns, Vector3[] mouths,
                                Bounds[] portals, Vector3 centre, float height, Vector3 across, Vector3 along)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            foreach (var plate in plates)
            {
                bool alongX = plate.size.x >= plate.size.z;
                float length = alongX ? plate.size.x : plate.size.z;
                float width = (alongX ? plate.size.z : plate.size.x) * 0.82f;   // inside the rails

                var forward = Flow(plate, centre, across, along);
                var side = Vector3.Cross(Vector3.up, forward).normalized * width * 0.5f;

                var mid = new Vector3(plate.center.x, height, plate.center.z);
                var back = mid - forward * length * 0.5f;
                var front = mid + forward * length * 0.5f;

                int first = vertices.Count;
                vertices.Add(back - side); vertices.Add(back + side);
                vertices.Add(front - side); vertices.Add(front + side);
                for (int i = 0; i < 4; i++) normals.Add(Vector3.up);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, length)); uvs.Add(new Vector2(1f, length));
                triangles.AddRange(new[] { first, first + 2, first + 1, first + 1, first + 2, first + 3 });
            }

            int corners = BuildCorners(plates, turns, mouths, portals, centre, height, across, along,
                                       vertices, normals, uvs, triangles);

            var mesh = new Mesh { name = "BeltSurface" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            if (AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath) != null) AssetDatabase.DeleteAsset(MeshPath);
            AssetDatabase.CreateAsset(mesh, MeshPath);

            var go = new GameObject("Belt Surface", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = TreadMaterial();
            go.AddComponent<ScrollingBelt>().speed = 0.5f;
            return corners;
        }

        // One arc per corner module — every turn the belt makes, not the four a rectangle
        // would have. The outfeed branches off the near side through corners of its own, and
        // doing this by ring quadrant left those bare.
        //
        // Each arc is built from the two straights the corner joins, found as the nearest
        // straight end running along X and the nearest running along Z. Not a circle: the
        // belt was fitted by scaling X and Z by different factors, so the turn is an
        // ellipse whose radii are the gaps between each lane line and where the other lane's
        // straight stops. Taken from the straights' own ends, the arc meets them exactly
        // whatever the fit did, and the width sweeps between the two lanes' widths.
        static int BuildCorners(Bounds[] plates, Bounds[] turns, Vector3[] mouths, Bounds[] portals,
                                Vector3 board, float height, Vector3 across, Vector3 along,
                                List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            // A flat run of band between two points, used to carry the spur on to its portal.
            void Strip(Vector3 from, Vector3 to, float width)
            {
                var forward = (to - from).normalized;
                var side = Vector3.Cross(Vector3.up, forward).normalized * width * 0.5f;
                float length = Vector3.Distance(from, to);

                int first = vertices.Count;
                vertices.Add(from - side); vertices.Add(from + side);
                vertices.Add(to - side); vertices.Add(to + side);
                for (int i = 0; i < 4; i++) normals.Add(Vector3.up);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, length)); uvs.Add(new Vector2(1f, length));
                triangles.AddRange(new[] { first, first + 2, first + 1, first + 1, first + 2, first + 3 });
            }

            const int segments = 10;
            int built = 0;

            // The two ends of a straight, on its lane's centre line.
            (Vector3 near, Vector3 far, bool alongX) Ends(Bounds b)
            {
                bool alongX = b.size.x >= b.size.z;
                var a = alongX ? new Vector3(b.min.x, 0f, b.center.z) : new Vector3(b.center.x, 0f, b.min.z);
                var c = alongX ? new Vector3(b.max.x, 0f, b.center.z) : new Vector3(b.center.x, 0f, b.max.z);
                return (a, c, alongX);
            }

            foreach (var turn in turns)
            {
                var at = new Vector3(turn.center.x, 0f, turn.center.z);
                float reach = (turn.size.x + turn.size.z) * 1.2f;

                Bounds? bestX = null, bestZ = null;
                Vector3 endOfX = default, endOfZ = default;
                float nearestX = reach, nearestZ = reach;

                foreach (var plate in plates)
                {
                    var (a, c, alongX) = Ends(plate);
                    var end = Vector3.Distance(a, at) < Vector3.Distance(c, at) ? a : c;
                    float distance = Vector3.Distance(end, at);

                    if (alongX && distance < nearestX) { nearestX = distance; bestX = plate; endOfX = end; }
                    if (!alongX && distance < nearestZ) { nearestZ = distance; bestZ = plate; endOfZ = end; }
                }

                float laneOfX, laneOfZ, widthX, widthZ;
                Vector2 origin;

                if (bestX != null && bestZ != null)
                {
                    // A turn between two straights: both lanes measured, both ends known.
                    laneOfX = bestX.Value.center.z;        // the X-running lane's z
                    laneOfZ = bestZ.Value.center.x;        // the Z-running lane's x
                    widthX = bestX.Value.size.z * 0.82f;
                    widthZ = bestZ.Value.size.x * 0.82f;
                    origin = new Vector2(endOfX.x, endOfZ.z);
                }
                else if (bestZ != null && mouths.Length > 0)
                {
                    // A turn off the ring and into an outfeed. There is no straight on the
                    // far side to measure — the run leaves through the portal — so the lane
                    // it hands over to is taken from the nearest outfeed mouth, and the
                    // point it hands over at is this module's own outer face.
                    var mouth = mouths.OrderBy(m => (new Vector2(m.x, m.z) - new Vector2(at.x, at.z)).sqrMagnitude)
                                      .First();
                    laneOfX = mouth.z;
                    laneOfZ = bestZ.Value.center.x;
                    widthZ = bestZ.Value.size.x * 0.82f;
                    widthX = widthZ;
                    float outer = turn.center.x > board.x ? turn.max.x : turn.min.x;
                    origin = new Vector2(outer, endOfZ.z);

                    // Carry on from where the turn lets go to the portal it feeds. That
                    // stretch is neither a straight nor a corner — it is the housing the
                    // outfeed sits in — so nothing else covers it.
                    var portal = portals
                        .OrderBy(p => (new Vector2(p.center.x, p.center.z) - new Vector2(mouth.x, mouth.z)).sqrMagnitude)
                        .Cast<Bounds?>().FirstOrDefault();
                    if (portal != null)
                    {
                        float face = portal.Value.center.x > board.x ? portal.Value.min.x : portal.Value.max.x;
                        var from = new Vector3(outer, height, mouth.z);
                        var to = new Vector3(face, height, mouth.z);
                        if (Vector3.Distance(from, to) > 0.2f) Strip(from, to, widthZ);
                    }
                }
                else continue;

                float radiusX = laneOfZ - origin.x;
                float radiusZ = laneOfX - origin.y;
                if (Mathf.Abs(radiusX) < 0.05f || Mathf.Abs(radiusZ) < 0.05f) continue;

                {
                    // Which end of the arc the luggage enters by. The arc is generated from
                    // the Z-lane end round to the other; if the straight that feeds it runs
                    // away from the corner instead of into it, the traffic goes the other
                    // way and the tread has to as well.
                    var feed = Flow(bestZ.Value, board, across, along);
                    bool reverse = Vector3.Dot(feed, at - endOfZ) < 0f;

                    int first = vertices.Count;
                    int firstUv = uvs.Count;
                    float travelled = 0f;
                    Vector3 previous = Vector3.zero;

                    for (int i = 0; i <= segments; i++)
                    {
                        // From the Z-running lane round to the X-running one.
                        float t = (float)i / segments;
                        float angle = t * Mathf.PI * 0.5f;
                        var point = new Vector3(origin.x + radiusX * Mathf.Cos(angle), height,
                                                origin.y + radiusZ * Mathf.Sin(angle));

                        // Tangent of the ellipse, which is what the band's width sits across.
                        var tangent = new Vector3(-radiusX * Mathf.Sin(angle), 0f,
                                                   radiusZ * Mathf.Cos(angle)).normalized;
                        var side = Vector3.Cross(Vector3.up, tangent).normalized *
                                   Mathf.Lerp(widthZ, widthX, t) * 0.5f;

                        if (i > 0) travelled += Vector3.Distance(previous, point);
                        previous = point;

                        vertices.Add(point - side);
                        vertices.Add(point + side);
                        normals.Add(Vector3.up);
                        normals.Add(Vector3.up);
                        uvs.Add(new Vector2(0f, travelled));
                        uvs.Add(new Vector2(1f, travelled));

                        if (i > 0)
                        {
                            int a = first + (i - 1) * 2;
                            triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                        }
                    }

                    if (reverse)
                        for (int u = firstUv; u < uvs.Count; u++)
                            uvs[u] = new Vector2(uvs[u].x, travelled - uvs[u].y);

                    // Which way a quarter ellipse winds depends on the signs of its two
                    // radii, and there are now turns in every combination — the ring's four
                    // and the outfeed's. Rather than reason about quadrants, look at the
                    // first triangle: if it faces the floor, flip the arc.
                    if (Vector3.Cross(vertices[first + 2] - vertices[first],
                                      vertices[first + 1] - vertices[first]).y < 0f)
                        FlipLast(triangles, segments * 6);
                    built++;
                }
            }

            return built;
        }

        static void FlipLast(List<int> triangles, int count)
        {
            for (int i = triangles.Count - count; i < triangles.Count; i += 3)
                (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
        }

        // A lamp at each module's outer edge, ordered round the board so the chase travels
        // the way the luggage does rather than in module order, which is assembly order.
        static void BuildLamps(Transform parent, Bounds[] plates, Vector3 centre, float height)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(LampMeshPath);
            if (mesh == null)
            {
                mesh = RoundedBox.Create(new Vector3(0.42f, 0.22f, 0.42f), 0.08f);
                AssetDatabase.CreateAsset(mesh, LampMeshPath);
            }

            var material = LampMaterial();
            var holder = new GameObject("Indicator Lamps");
            holder.transform.SetParent(parent, false);

            var lamps = new List<(float angle, Renderer renderer)>();
            foreach (var plate in plates)
            {
                var radial = plate.center - centre;
                radial.y = 0f;
                bool alongX = plate.size.x >= plate.size.z;
                float half = (alongX ? plate.size.z : plate.size.x) * 0.5f;
                var outward = (alongX ? new Vector3(0f, 0f, Mathf.Sign(radial.z))
                                      : new Vector3(Mathf.Sign(radial.x), 0f, 0f));

                var go = new GameObject("Lamp", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(holder.transform, false);
                go.transform.position = new Vector3(plate.center.x, height + 0.04f, plate.center.z)
                                        + outward * (half + 0.3f);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lamps.Add((Mathf.Atan2(radial.z, radial.x), renderer));
            }

            var blink = holder.AddComponent<BlinkingLights>();
            blink.lamps = lamps.OrderBy(l => l.angle).Select(l => l.renderer).ToArray();
            blink.period = 2.6f;
        }

        static Material TreadMaterial()
        {
            const string path = "Assets/_Game/Materials/Terminal/Belt_Tread.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Belt_Tread" };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", TerminalTextures.Tread());
            // One repeat every two units along the run, which is the slat pitch the eye
            // reads as movement. V was built in world units for exactly this.
            material.SetTextureScale("_BaseMap", new Vector2(1f, 0.5f));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.5f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material LampMaterial()
        {
            const string path = "Assets/_Game/Materials/Terminal/Belt_Lamp.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Belt_Lamp" };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", new Color(1f, 0.86f, 0.55f));
            material.SetFloat("_Smoothness", 0.7f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            // The block written per lamp overrides this; it is here so the material reads
            // right in the inspector and on any lamp the chase has not reached yet.
            material.SetColor("_EmissionColor", new Color(1f, 0.72f, 0.15f) * 0.4f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
