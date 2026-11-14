using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SheepGame.Core;
using SheepGame.Fx;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SheepGame.EditorTools
{
    // A terminal hall to stand the board in, built into a scene of its own.
    //
    // The airport pack is furniture only — desks, chairs, a cafe, signage, belt modules —
    // and has no hall to put them in, so the shell is generated: floor, two walls set back
    // like a stage flat, and a colonnade. Furniture is then placed in the margin around a
    // reserved rectangle the size of the playfield, so the board can drop straight in.
    //
    // The look is the soft casual one: matte surfaces, bright warm light, weak shadows,
    // saturated grading.
    public static class TerminalBuilder
    {
        const string Pack = "Assets/Airport/source/airport-inside-building-pack/Airport_InsideBuildingPack.fbx";
        const string PackMaterial = "Assets/Airport/source/airport-inside-building-pack/materials/AIRPORT 1.mat";
        const string ScenePath = "Assets/Scenes/TerminalSandbox.unity";
        const string RootName = "Terminal";
        const string MaterialDir = "Assets/_Game/Materials/Terminal";
        const string MeshDir = "Assets/_Game/Meshes/Shell";
        const string ProfilePath = "Assets/Settings/Terminal_Casual.asset";

        // The playfield this hall is built around. A fallback only: Build measures the
        // board's own conveyor ring and uses that, so the hall follows the board.
        //
        // It was this being a constant that put the room inside the game. A number written
        // down once is true of the board it was measured on and of no other, and the board
        // has been resized four times since — so the trolleys ended up parked on the belt,
        // the hanging signs over it, and the ring itself hanging off the band it is meant
        // to stand on. Nothing here is a distance any more; everything is measured out from
        // the ring.
        static readonly Vector2 DefaultPlayArea = new Vector2(44f, 34f);
        static Vector2 PlayArea = DefaultPlayArea;

        // The whole belt, ring and outfeed alike, in the floor plane. The ring is what the
        // playfield reserves; this is what a prop has to miss, and the two are very
        // different rectangles — the junction, its two portals and the desk standing over
        // them reach twenty units further out than the ring does, into precisely the corner
        // the scatter is thrown at and the second carousel was laid down in.
        static Bounds _beltEnvelope;

        // Whether a prop dropped here would be standing in the belt. Two units of slack,
        // because a template is placed by its pivot and most of this pack's pivots are off
        // in a corner of the thing they belong to.
        static bool InTheBelt(Vector3 at) =>
            at.x > _beltEnvelope.min.x - 2f && at.x < _beltEnvelope.max.x + 2f &&
            at.z > _beltEnvelope.min.z - 2f && at.z < _beltEnvelope.max.z + 2f;

        // The rectangle the game reserves: the ring as it actually stands, corner modules
        // and all, rather than the route through it. Furniture has to clear the modules and
        // not the line the luggage travels along — a corner module is half as wide again as
        // a straight, and that difference is exactly the strip the trolleys were parked in.
        //
        // Measured off the belt rather than recomputed from cellSize and roadMargin: the
        // belt is in the scene and already fitted, and measuring is the one way the two
        // cannot drift apart again. Fit it first (SheepGame > Fit Baggage Belt) after the
        // board has been resized.
        static Vector2 MeasurePlayArea()
        {
            var board = Object.FindFirstObjectByType<Board>(FindObjectsInactive.Include);
            if (board == null || board.belt == null) return DefaultPlayArea;

            Bounds ring = default, whole = default;
            bool any = false, all = false;
            foreach (var r in board.belt.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!all) { whole = r.bounds; all = true; }
                else whole.Encapsulate(r.bounds);

                // Lane modules only. The outfeed portal and the spur it stands on run off
                // towards the exit and would stretch the rectangle half across the hall.
                if (!r.name.StartsWith("12.2") && !r.name.StartsWith("12.4")) continue;
                if (!any) { ring = r.bounds; any = true; }
                else ring.Encapsulate(r.bounds);
            }

            _beltEnvelope = whole;
            if (!any) return DefaultPlayArea;

            // Squared about the board, because everything built from this is placed
            // symmetrically about it: a ring a hair off centre would bias all of it.
            Vector3 centre = board.transform.position;
            return new Vector2(2f * Mathf.Max(ring.max.x - centre.x, centre.x - ring.min.x),
                               2f * Mathf.Max(ring.max.z - centre.z, centre.z - ring.min.z));
        }

        // Both numbers are measured off the game's own portrait frame, not chosen for the
        // room's sake. Standing at the distance the framer picks for a 1080x2340 phone
        // (83 units back, pitched 61), the top of the shot looks past the floor's far edge
        // and out into nothing, and a 9-unit wall stops a third of the way up that gap: the
        // frame came back with a band of flat background colour across the top and the
        // check-in row silhouetted against it. The sightline at the top of the frame passes
        // the far wall 20.3 units up, so the wall has to be taller than that.
        //
        // The width is what the furniture needs rather than the camera: the cafe is one
        // object 28 deep, and against the old 62 there was no margin between the playfield
        // and the side wall that it fitted in — it stood in the belt lane with one of its
        // chairs out on the board.
        static readonly Vector2 Hall = new Vector2(78f, 80f);

        // The hall's two ends along world X. Near is the camera's side and stays where Hall puts
        // it; Far is pushed back past the check-in platform when there is one, since that stands
        // beyond the ring on the far side and the old wall ran straight through it.
        static float Near = 39f, Far = 39f, LaneFar;

        // 2026-09-14: steeper and narrower, for the Bus Jam reference the user brought -- the
        // board across the bottom of the screen, the platform and terminal across the top, and a
        // flatter, closer-to-plan look. The old 56 / 55 was a view into a hall; this is a view
        // down onto a board.
        public const float CameraPitch = 64f;
        public const float CameraFov = 36f;

        static void MeasureLane()
        {
            Near = Hall.x * 0.5f;
            Far = Near;
            LaneFar = 0f;

            GameObject lane = null;
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == CheckInLaneBuilder.RootName) lane = root;
            if (lane == null) return;

            Bounds bounds = default;
            bool any = false;
            foreach (var r in lane.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!any) return;

            _beltEnvelope.Encapsulate(bounds);
            LaneFar = -bounds.min.x;
            // The camera frames the sorter at the top of the shot and centres it all, so it stands
            // further back than it did and sees past the old near edge: floor runs on under the lens.
            Near = Hall.x * 0.5f + 18f;
            // Deep enough for the check-in row to stand against the wall behind the platform: a
            // desk is fifteen units deep, and at six units of clearance the row stood through the
            // terminal.
            Far = Mathf.Max(Near, LaneFar + 18f);
        }
        const float WallHeight = 22f;

        // The pack is authored small: a check-in desk is 4.8 units against a board 33 across,
        // so at native size the furniture reads as dolls' house furniture around the playfield.
        // The game stretches the belt modules for the same reason.
        const float PropScale = 2.4f;

        [MenuItem("SheepGame/Build Terminal Sandbox")]
        public static void Build()
        {
            var scene = OpenOrCreateScene();

            // Roots only, and every one of them. GameObject.Find matches any object of the name,
            // and the check-in lane had a child called Terminal: that was deleted instead of the
            // hall, and the rebuild stood a second hall on top of the first.
            foreach (var existing in scene.GetRootGameObjects())
                if (existing.name == RootName) Object.DestroyImmediate(existing);

            EnsurePackShading();

            PlayArea = MeasurePlayArea();
            MeasureLane();

            var root = new GameObject(RootName);
            var random = new System.Random(8);
            var templates = new Templates(Pack);

            try
            {
                BuildShell(root.transform);
                Dress(templates, root.transform, random);
            }
            finally
            {
                templates.Dispose();
            }

            SetUpScene(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"Built the terminal hall ({Hall.x} x {Hall.y} units around a " +
                      $"{PlayArea.x} x {PlayArea.y} playfield, {root.transform.childCount} groups) in {ScenePath}.");
        }

        // ---- shell --------------------------------------------------------------------------

        static void BuildShell(Transform parent)
        {
            // White, light grey, cyan and turquoise, with the accents left to the suitcases
            // and the signage — the brief's palette. Nothing here is brown or a dark grey any
            // more; the old warm greys were what made the hall read as concrete.
            //
            // Smoothness rather than matte: these are meant to be moulded plastic, and a
            // little gloss is what separates a toy from a wall. It stays well under a mirror
            // — there is one probe and a very soft sun, so a high value would only smear.
            var floorMaterial = Plastic("Terminal_Floor", new Color(0.95f, 0.96f, 0.97f), 0.42f,
                                        TerminalTextures.Tiles(), tiling: 0.5f);
            var wallMaterial = Plastic("Terminal_Wall", new Color(0.97f, 0.98f, 0.99f), 0.30f);
            var trimMaterial = Plastic("Terminal_Trim", new Color(0.20f, 0.72f, 0.76f), 0.55f);
            var laneMaterial = Plastic("Terminal_Lane", new Color(0.84f, 0.93f, 0.95f), 0.38f);

            var shell = new GameObject("Shell");
            shell.transform.SetParent(parent, false);

            Box(shell.transform, "Floor", new Vector3((Near - Far) * 0.5f, -0.25f, 0f),
                new Vector3(Near + Far, 0.5f, Hall.y), floorMaterial);

            // A band of a different tone where the play area sits, so the empty middle reads
            // as part of the hall rather than as a hole in it.
            Box(shell.transform, "PlayfieldBand", new Vector3(0f, 0.01f, 0f),
                new Vector3(PlayArea.x + 6f, 0.02f, PlayArea.y + 6f), laneMaterial);

            // Two walls only, and on the far side of the game's own shot: the camera is
            // yawed 270, so it looks down -X and the background of every frame is that
            // corner. A room with four walls would put one between the lens and the board.
            Box(shell.transform, "Wall Far", new Vector3(-Far, WallHeight * 0.5f, 0f),
                new Vector3(1f, WallHeight, Hall.y), wallMaterial);
            Box(shell.transform, "Wall Side", new Vector3((Near - Far) * 0.5f, WallHeight * 0.5f, Hall.y * 0.5f),
                new Vector3(Near + Far, WallHeight, 1f), wallMaterial);

            // Colonnade along both walls, with a dark capital so the top does not merge with
            // the wall behind it.
            for (float z = -Hall.y * 0.5f + 6f; z <= Hall.y * 0.5f - 6f; z += 12f)
            {
                Box(shell.transform, "Pillar", new Vector3(-Far + 2f, WallHeight * 0.5f, z),
                    new Vector3(1.4f, WallHeight, 1.4f), wallMaterial);
                Box(shell.transform, "Pillar Cap", new Vector3(-Far + 2f, WallHeight - 0.4f, z),
                    new Vector3(2f, 0.8f, 2f), trimMaterial);
            }

            for (float x = -Far + 8f; x <= Near - 6f; x += 12f)
            {
                Box(shell.transform, "Pillar", new Vector3(x, WallHeight * 0.5f, Hall.y * 0.5f - 2f),
                    new Vector3(1.4f, WallHeight, 1.4f), wallMaterial);
                Box(shell.transform, "Pillar Cap", new Vector3(x, WallHeight - 0.4f, Hall.y * 0.5f - 2f),
                    new Vector3(2f, 0.8f, 2f), trimMaterial);
            }

            // Skirting where wall meets floor, which is most of what stops a generated room
            // looking like two intersecting slabs.
            Box(shell.transform, "Skirting Far", new Vector3(-Far + 0.6f, 0.35f, 0f),
                new Vector3(0.4f, 0.7f, Hall.y), trimMaterial);
            Box(shell.transform, "Skirting Side", new Vector3((Near - Far) * 0.5f, 0.35f, Hall.y * 0.5f - 0.6f),
                new Vector3(Near + Far, 0.7f, 0.4f), trimMaterial);

            // The far wall is the whole background of the shot, and at 22 units of blank
            // eggshell it is a bigger, duller version of the gap it was raised to close.
            // Glazing and a fascia give it the two horizontals a terminal reads by.
            // Bright for its own sake: this face points away from the sun and takes under a
            // third of it, so a colour picked to look like glass in the inspector renders as
            // a dark band across the top of every frame.
            var glassMaterial = Plastic("Terminal_Glass", new Color(0.82f, 0.93f, 0.98f), 0.72f);
            float glassBottom = WallHeight * 0.45f;
            float glassTop = WallHeight - 2.6f;
            float wallFace = -Far + 0.55f;   // just proud of the wall, never inside it

            for (float z = -Hall.y * 0.5f + 3f; z < Hall.y * 0.5f - 3f; z += 7f)
            {
                Box(shell.transform, "Glazing",
                    new Vector3(wallFace, (glassBottom + glassTop) * 0.5f, z + 2.75f),
                    new Vector3(0.3f, glassTop - glassBottom, 5.5f), glassMaterial);
                Box(shell.transform, "Mullion",
                    new Vector3(wallFace, (glassBottom + glassTop) * 0.5f, z),
                    new Vector3(0.35f, glassTop - glassBottom, 0.6f), trimMaterial);
            }

            Box(shell.transform, "Fascia Far", new Vector3(wallFace, WallHeight - 1.3f, 0f),
                new Vector3(0.5f, 2.6f, Hall.y), trimMaterial);
            Box(shell.transform, "Sill Far", new Vector3(wallFace, glassBottom - 0.3f, 0f),
                new Vector3(0.5f, 0.6f, Hall.y), trimMaterial);

            Ceiling(shell.transform, wallMaterial, trimMaterial);
            Markings(shell.transform);
        }

        // Something over the player's head at the top of the frame — but only there.
        //
        // A ceiling over this camera is a lid on the board: the shot looks down at 61
        // degrees from well above the wall, so anything spanning the hall at ceiling height
        // sits between the lens and the playfield. The first build proved it, laying teal
        // beams and glowing panels straight across the pieces. The same reasoning already
        // limits the hall to two walls; it limits the ceiling to the far strip, past the
        // playfield, where the sightline has already passed underneath it.
        static void Ceiling(Transform parent, Material wallMaterial, Material trimMaterial)
        {
            const float height = WallHeight - 1.2f;
            float from = -Far + 4f;
            float to = -PlayArea.x * 0.5f - 6f;      // stops short of the playfield
            // ...and of the check-in platform, which the camera looks straight down on.
            if (LaneFar > 0f) to = Mathf.Min(to, -LaneFar - 4f);

            var lightMaterial = Plastic("Terminal_CeilingLight", new Color(1f, 0.99f, 0.94f), 0.2f);
            lightMaterial.EnableKeyword("_EMISSION");
            lightMaterial.SetColor("_EmissionColor", new Color(1f, 0.97f, 0.88f) * 1.4f);
            lightMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(lightMaterial);

            for (float x = from; x <= to; x += 7f)
            {
                Box(parent, "Ceiling Beam", new Vector3(x, height, 0f),
                    new Vector3(1.2f, 0.7f, Hall.y), wallMaterial, bevel: 0.24f);

                for (float z = -Hall.y * 0.5f + 10f; z <= Hall.y * 0.5f - 10f; z += 13f)
                    Box(parent, "Ceiling Light", new Vector3(x + 3.5f, height - 0.2f, z),
                        new Vector3(1.8f, 0.25f, 6f), lightMaterial, bevel: 0.1f);
            }
        }

        // Painted floor: a dashed walking lane down each side of the playfield and chevrons
        // pointing the way out past the belt. Quads laid a hair over the floor rather than
        // baked into the tile texture, because the tiles repeat every couple of units and a
        // marking has to sit in one place.
        static void Markings(Transform parent)
        {
            var arrowMaterial = Plastic("Terminal_Arrow", Color.white, 0.3f,
                                        TerminalTextures.Arrow(), transparent: true);
            var laneMaterial = Plastic("Terminal_LaneMark", Color.white, 0.3f,
                                       TerminalTextures.Lane(), transparent: true);

            float halfX = PlayArea.x * 0.5f + 5.5f;
            float halfZ = PlayArea.y * 0.5f + 5.5f;

            // A lane down each side of the playfield. Tiled along its length rather than
            // stretched, or one dash covers sixty units.
            laneMaterial.SetTextureScale("_BaseMap", new Vector2(1f, 8f));
            for (int side = -1; side <= 1; side += 2)
                Quad(parent, "Lane Marking", new Vector3(0f, 0.06f, side * halfZ),
                     new Vector2(3.5f, Hall.x * 0.7f), 90f, laneMaterial);

            // Chevrons at the near end, running with the pieces towards the outfeed.
            for (int i = 0; i < 3; i++)
                Quad(parent, "Floor Arrow", new Vector3(halfX + 2f + i * 4.5f, 0.06f, 0f),
                     new Vector2(4f, 4f), 90f, arrowMaterial);
        }

        static void Quad(Transform parent, string name, Vector3 centre, Vector2 size, float yaw, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.Euler(90f, yaw, 0f);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<MeshCollider>());
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        // The pack ships on a toon shader that returns NaN from this scene's lighting, and a
        // NaN survives every blur bloom does — one bad pixel comes back as a white blot the
        // size of the filter. That is what the white over the hall was: not an over-bright
        // surface (nothing in the frame gets past 0.76) but a few thousand NaN pixels on the
        // desks, the security check and the scenery belt, smeared. It also does not belong
        // here — everything else in this hall is matte URP Lit, and the cel look was turned
        // down twice.
        static void EnsurePackShading()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(PackMaterial);
            if (material == null) { Debug.LogWarning($"No pack material at {PackMaterial}."); return; }

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (material.shader == lit) return;

            // Swapped by name, so _BaseMap carries the pack's atlas across untouched.
            var atlas = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap")
                      : material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;

            Debug.Log($"Airport pack material was on '{material.shader.name}', which renders NaN here; " +
                      "moving it to URP Lit.");
            material.shader = lit;
            if (atlas != null) material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.08f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        // Every box in the hall is chamfered rather than a raw cube — see RoundedBox for
        // why. The mesh is built at the box's real size and the transform left at scale 1,
        // because a bevel scaled by 78 on one axis and 0.5 on another is not a bevel.
        //
        // Identical sizes share one mesh asset, so the colonnade is one mesh instanced
        // however many times and the scene file does not carry a copy of each.
        static GameObject Box(Transform parent, string name, Vector3 centre, Vector3 size, Material material,
                              float bevel = 0.18f)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;

            go.GetComponent<MeshFilter>().sharedMesh = ShellMesh(size, bevel);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            // Nothing in the shell ever moves, and marking it up is most of what makes a
            // hall this size cheap on a phone.
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic |
                                                       StaticEditorFlags.OccluderStatic |
                                                       StaticEditorFlags.OccludeeStatic |
                                                       StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        static readonly Dictionary<string, Mesh> _shellMeshes = new Dictionary<string, Mesh>();

        static Mesh ShellMesh(Vector3 size, float bevel)
        {
            string key = $"Shell_{size.x:0.##}x{size.y:0.##}x{size.z:0.##}_{bevel:0.##}".Replace(',', '.');
            if (_shellMeshes.TryGetValue(key, out var cached) && cached != null) return cached;

            EnsureFolder(MeshDir);
            string path = $"{MeshDir}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = RoundedBox.Create(size, bevel);
                AssetDatabase.CreateAsset(mesh, path);
            }

            _shellMeshes[key] = mesh;
            return mesh;
        }

        // ---- furniture ----------------------------------------------------------------------

        static void Dress(Templates templates, Transform parent, System.Random random)
        {
            var props = new GameObject("Fittings");
            props.transform.SetParent(parent, false);
            var t = props.transform;

            // Measured out from the band the board is fitted to, with clearance — not from
            // PlayArea itself. Fit Board To Ground sizes the board to whatever ground it is
            // given, so enlarging the reserved area enlarges the board with it: furniture
            // placed at the old edge ends up inside the playfield again. The clearance is
            // what breaks that loop.
            float halfX = PlayArea.x * 0.5f + 7f;
            float halfZ = PlayArea.y * 0.5f + 7f;

            // Check-in row against the far wall, faces turned into the hall — this is the
            // wall the camera looks at, so it carries the readable silhouettes.
            float wallX = Hall.x * 0.5f;
            float wallZ = Hall.y * 0.5f;

            // Measured in from the ring rather than out from the wall. A desk is fifteen
            // units deep and the strip between the two is five, so one of the ends has to
            // go somewhere: out through the wall, where the wall hides it, and not in over
            // the belt lane, which is where the fixed distance put it.
            // Against the far wall instead when the check-in platform stands where the row was.
            float checkIn = LaneFar > 0f ? -Far + 9f : -halfX - 1f;

            for (int i = 0; i < 4; i++)
                templates.Place("08.CheckInDesk", t, new Vector3(checkIn, 0f, -12f + i * 8f), 90f);

            templates.Place("09.SecurityCheck", t, new Vector3(checkIn, 0f, 22f), 90f);
            templates.Place("11.BoardingAreaDesk", t, new Vector3(6f, 0f, wallZ - 4f), 180f);
            templates.Place("13.Cafe", t, new Vector3(-14f, 0f, wallZ - 4f), 180f);

            // Seating in the open band on the near side.
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < 4; i++)
                    templates.Place("10.Chairs", t,
                        new Vector3(-9f + i * 6f, 0f, -halfZ - 2f - row * 4f), 0f);

            // A second baggage belt as scenery, so the one the game generates is not the only
            // one in the room. Laid along the near shelf past the game's own outfeed rather
            // than at a fixed -8, which is inside the desk that stands over the junction.
            //
            // Its portal is not built with it any more: the run and the portal together are
            // thirty-four units long and the clear floor beside the outfeed is twenty-nine,
            // so the portal could only be put back inside the outfeed or out through the
            // wall. The game's own belt has two of them a few units away.
            float shelf = halfX + 5f;
            float lastBand = _beltEnvelope.min.z - 4f;
            for (int i = 0; i < 5; i++)
                templates.Place("12.2.BaggageClaimBand", t,
                    new Vector3(shelf, 0f, lastBand - i * 4.2f), 90f);

            Furnish(t, halfX, halfZ);

            // Signage and small stuff, scattered along the walkways.
            string[] scatter = { "14.Trashcan", "15.Advertising", "16.DepartureSign", "17.3.Model", "17.5.Model", "17.8.Model" };
            for (int i = 0; i < 18; i++)
            {
                var name = scatter[random.Next(scatter.Length)];

                // Kept in the walkway between the playfield's clearance and the walls, and
                // re-thrown while it lands in the game's own belt: to the walkway the near
                // corner reads as clear floor, and it is where the outfeed's desk stands.
                Vector3 position = default;
                bool clear = false;
                for (int tries = 0; tries < 8 && !clear; tries++)
                {
                    bool alongX = random.NextDouble() < 0.5;
                    float u = (float)(random.NextDouble() * 2f - 1f);

                    position = alongX
                        ? new Vector3(u * halfX, 0f, (random.NextDouble() < 0.5 ? -1f : 1f) * (halfZ + 2f))
                        : new Vector3((random.NextDouble() < 0.5 ? -1f : 1f) * (halfX + 2f), 0f, u * halfZ);

                    clear = !InTheBelt(position);
                }

                if (!clear) continue;
                templates.Place(name, t, position, random.Next(4) * 90f);
            }
        }

        // The two strips either side of the playfield are what the player looks at for the
        // whole game and they were bare tile. The pack has nothing that fills them — it is
        // desks and seating, all of it low and most of it already placed — so these are
        // built: planters, baggage trolleys and a hanging board, out of the same chamfered
        // boxes as the shell, in the same four colours.
        static void Furnish(Transform parent, float halfX, float halfZ)
        {
            var potMaterial = Plastic("Terminal_Planter", new Color(0.96f, 0.97f, 0.98f), 0.45f);
            var leafMaterial = Plastic("Terminal_Foliage", new Color(0.36f, 0.78f, 0.42f), 0.35f);
            var trolleyMaterial = Plastic("Terminal_Trolley", new Color(0.24f, 0.76f, 0.80f), 0.5f);
            var signMaterial = Plastic("Terminal_Sign", new Color(0.12f, 0.52f, 0.62f), 0.4f);
            // The board's face carries a ticker texture; the material is shared, so the
            // scroll is driven per renderer through a property block by ScrollingBelt.
            var screenMaterial = Plastic("Terminal_Screen", Color.white, 0.6f,
                                         TerminalTextures.Ticker(), tiling: 1f);
            screenMaterial.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.62f));
            screenMaterial.EnableKeyword("_EMISSION");
            screenMaterial.SetColor("_EmissionColor", new Color(0.35f, 0.80f, 0.95f) * 0.55f);
            screenMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(screenMaterial);

            // Immediately outside the conveyor, and measured off it. The portrait shot is
            // 31 degrees wide, so between the ring and the edge of the frame there is only
            // about a trolley's width of floor: these stand half in shot, which is what a
            // room carrying on past the frame looks like and is the whole point of putting
            // anything there. A fixed distance cannot do it — 20.5 was the right answer for
            // one ring and put every trolley underneath the next one.
            // Clear of the route the board reserves and not merely of the modules on it:
            // Audit Scene measures against the route, and half a unit of overlap with it
            // reads there as furniture standing in the playfield.
            float lane = PlayArea.y * 0.5f + 2.5f;
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 4; i++)
                {
                    float x = -30f + i * 7f;
                    // Trolleys parked along the lane rather than across it: turned broadside
                    // their deck is 4.9 deep and the outer corner clips the belt lane, which
                    // is reserved for the game.
                    if (i % 2 == 0) Planter(parent, new Vector3(x, 0f, side * lane), potMaterial, leafMaterial);
                    else Trolley(parent, new Vector3(x, 0f, side * lane), side * 12f, trolleyMaterial);
                }

            // Hung over the far walkway, where the frame has height to spare and nothing in
            // it. Two posts and a board, with a lit face towards the camera.
            for (int i = -1; i <= 1; i += 2)
            {
                // Clear of the ring, hung just outside it: the board is only 0.6 deep and
                // 0.9 of that hangs back towards the belt.
                var at = new Vector3(LaneFar > 0f ? -Far + 5f : -PlayArea.x * 0.5f - 1.5f, 0f, i * 15f);
                Box(parent, "Sign Post", at + Vector3.up * 5.4f, new Vector3(0.5f, 10.8f, 0.5f), signMaterial);
                Box(parent, "Sign Board", at + new Vector3(0.3f, 9.6f, 0f),
                    new Vector3(0.6f, 2.6f, 9f), signMaterial, bevel: 0.25f);
                var screen = Box(parent, "Sign Screen", at + new Vector3(0.75f, 9.6f, 0f),
                                 new Vector3(0.15f, 1.9f, 8.2f), screenMaterial, bevel: 0.07f);

                // Slow, and the two boards run at slightly different speeds: in step they
                // read as one animation playing twice rather than as two live displays.
                var ticker = screen.AddComponent<ScrollingBelt>();
                ticker.speed = i > 0 ? -0.09f : -0.07f;

                // A board that scrolls is a board that is on, so it should not also be
                // marked static — batching would bake its transform and the property block
                // would still animate, which is fine, but the occlusion flags are a lie.
                GameObjectUtility.SetStaticEditorFlags(screen, 0);
            }
        }

        static void Planter(Transform parent, Vector3 at, Material pot, Material leaf)
        {
            Box(parent, "Planter", at + Vector3.up * 0.9f, new Vector3(2.6f, 1.8f, 2.6f), pot, bevel: 0.45f);

            // Three overlapping blobs read as foliage at this distance and cost three
            // spheres; anything modelled would be detail nobody sees.
            var offsets = new[] { new Vector3(0f, 2.6f, 0f), new Vector3(0.7f, 2.1f, 0.4f), new Vector3(-0.6f, 2.2f, -0.5f) };
            var sizes = new[] { 2.6f, 1.8f, 1.7f };
            for (int i = 0; i < offsets.Length; i++)
            {
                var blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                blob.name = "Foliage";
                blob.transform.SetParent(parent, false);
                blob.transform.localPosition = at + offsets[i];
                blob.transform.localScale = Vector3.one * sizes[i];
                blob.GetComponent<MeshRenderer>().sharedMaterial = leaf;
                Object.DestroyImmediate(blob.GetComponent<SphereCollider>());
                GameObjectUtility.SetStaticEditorFlags(blob, StaticEditorFlags.BatchingStatic);
            }
        }

        static void Trolley(Transform parent, Vector3 at, float yaw, Material material)
        {
            var trolley = new GameObject("Baggage Trolley");
            trolley.transform.SetParent(parent, false);
            trolley.transform.localPosition = at;
            trolley.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            Box(trolley.transform, "Deck", new Vector3(0f, 0.7f, 0f), new Vector3(4.4f, 0.4f, 2.6f), material, 0.15f);
            Box(trolley.transform, "Handle Post", new Vector3(-1.9f, 1.6f, 0f), new Vector3(0.3f, 2.2f, 2.2f), material, 0.12f);
            Box(trolley.transform, "Handle", new Vector3(-1.9f, 2.6f, 0f), new Vector3(0.45f, 0.35f, 2.6f), material, 0.15f);
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                    Box(trolley.transform, "Wheel", new Vector3(i * 1.7f, 0.32f, j * 1.05f),
                        new Vector3(0.6f, 0.6f, 0.35f), material, 0.2f);
        }

        // ---- pack -----------------------------------------------------------------------------

        // The pack is one FBX holding every piece as a child, laid out side by side. Same trick
        // as the city's road sheet: instantiate it once, keep the children as templates, clone
        // per placement — and drop the collision meshes, which ship as siblings.
        class Templates : System.IDisposable
        {
            readonly Dictionary<string, GameObject> _byName = new Dictionary<string, GameObject>();
            GameObject _sheet;

            public Templates(string path)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                {
                    Debug.LogError($"Airport pack not found at {path}.");
                    return;
                }

                _sheet = Object.Instantiate(asset);
                _sheet.hideFlags = HideFlags.HideAndDontSave;

                foreach (var filter in _sheet.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.name.Contains("Collider")) continue;
                    _byName[filter.name] = filter.gameObject;
                }
            }

            public void Place(string name, Transform parent, Vector3 position, float yaw)
            {
                if (!_byName.TryGetValue(name, out var template))
                {
                    Debug.LogWarning($"Airport pack has no '{name}'.");
                    return;
                }

                var copy = Object.Instantiate(template, parent);
                copy.hideFlags = HideFlags.None;
                copy.name = name;

                // Yaw on top of the imported rotation, never instead of it.
                copy.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * template.transform.rotation;
                copy.transform.position = position;
                copy.transform.localScale = template.transform.localScale * PropScale;
            }

            public void Dispose()
            {
                if (_sheet != null) Object.DestroyImmediate(_sheet);
                _byName.Clear();
            }
        }

        // ---- look ------------------------------------------------------------------------------

        static Material Plastic(string name, Color colour, float smoothness,
                                Texture texture = null, float tiling = 1f, bool transparent = false)
        {
            EnsureFolder(MaterialDir);
            string path = $"{MaterialDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            // Colours written into a saved material are sRGB — the asset format stores them
            // that way and Unity converts on load. Converting here would double it.
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);

            // Specular back on. It was switched off for the matte look, and it is exactly
            // what a moulded-plastic surface needs — the highlight rolling along a bevel is
            // most of the toy read.
            material.SetFloat("_SpecularHighlights", 1f);
            material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");

            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", Vector2.one * tiling);

            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_ZWrite", 0f);
                material.renderQueue = (int)RenderQueue.Transparent;
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            // The colonnade, the glazing bars and the markings are the same mesh over and
            // over; instancing is free here and this is a phone target.
            material.enableInstancing = true;

            EditorUtility.SetDirty(material);
            return material;
        }

        static void SetUpScene(GameObject terminal)
        {
            var sun = Object.FindFirstObjectByType<Light>(FindObjectsInactive.Include);
            if (sun == null) sun = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.95f, 0.86f, 1f);
            sun.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            sun.shadows = LightShadows.Soft;

            // Soft contact shadows rather than none: at 0.45 the pieces barely sat on the
            // floor, and a shadow is what stops a toy looking pasted onto its background.
            // It stays well under a hard shadow, so nothing in the hall goes dark.
            sun.shadowStrength = 0.62f;
            sun.shadowBias = 0.02f;
            sun.shadowNormalBias = 0.2f;

            // Flat, not Skybox: a probe nothing rebuilds serialises as zero and everything out
            // of the sun goes black. Lifted, and tinted towards the sky rather than neutral —
            // ambient is what fills the shaded side of every case, and a cool fill under a
            // warm key is most of the bright-toy read.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.66f, 0.72f, 1f);
            DynamicGI.UpdateEnvironment();

            EnsureReflectionProbe();

            AssignProfile(EnsureProfile());

            var camera = Camera.main;
            if (camera == null)
                camera = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" }.GetComponent<Camera>();

            // The game's own shot: yaw 270, FOV 55, and the pitch dropped from 61 to 56.
            //
            // Lower is not a reversal of the 57 -> 61 tilt that was asked for on the car
            // park; that was about seeing into the field. This hall has height — glazing, a
            // colonnade, hanging boards, a ceiling in the far strip — and at 61 the shot
            // looked past all of it into the floor. Five degrees back gives the frame a
            // horizon to read depth against and still keeps the cases' fronts, which is
            // what going the other way (66, 72) destroyed.
            camera.transform.rotation = Quaternion.Euler(CameraPitch, 270f, 0f);
            camera.transform.position = -camera.transform.forward * 52f;
            camera.fieldOfView = CameraFov;
            camera.farClipPlane = 500f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.78f, 0.84f, 0.9f);

            // Without this the volume profile is decoration: URP runs no post at all on a
            // camera that has it off, and a camera made in code has it off. Between that and
            // a profile whose overrides were never filed into the asset, not one frame of
            // this scene had been graded, bloomed or vignetted.
            var urp = camera.GetUniversalAdditionalCameraData();
            urp.renderPostProcessing = true;
            urp.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        }

        // One probe over the whole hall. Every material now carries some gloss, and gloss
        // with nothing to reflect samples the flat ambient and comes back as a dull sheen —
        // the probe is what puts the room's own colours into the highlight. Baked rather
        // than realtime: nothing in the shell moves.
        static void EnsureReflectionProbe()
        {
            var existing = Object.FindFirstObjectByType<ReflectionProbe>(FindObjectsInactive.Include);
            var probe = existing != null ? existing
                      : new GameObject("Reflection Probe", typeof(ReflectionProbe)).GetComponent<ReflectionProbe>();

            probe.transform.position = new Vector3((Near - Far) * 0.5f, WallHeight * 0.35f, 0f);
            probe.size = new Vector3(Near + Far, WallHeight * 1.2f, Hall.y);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
            probe.resolution = 128;
            probe.hdr = true;
            probe.boxProjection = true;
            probe.importance = 1;
            EditorUtility.SetDirty(probe);
        }

        static VolumeProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            // Overrides that were only ever added in memory serialise as null entries, and a
            // profile of nothing but nulls is exactly how this one was found: the grading,
            // the tonemapping, the bloom and the vignette below had never once been applied
            // to a frame. See GetOrAdd — a VolumeComponent has to be filed into the profile
            // asset as well as into its list.
            profile.components.RemoveAll(c => c == null);

            // ACES: it rolls the top of the range off into a filmic shoulder instead of
            // clipping, which is what lets the saturation sit this high without the bright
            // faces of the cases going to flat colour.
            var tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;

            var colour = GetOrAdd<ColorAdjustments>(profile);
            colour.postExposure.overrideState = true;
            colour.postExposure.value = 0f;
            colour.contrast.overrideState = true;
            colour.contrast.value = 12f;
            colour.saturation.overrideState = true;
            colour.saturation.value = 70f;

            // Saturation and warmth pull against each other here. The floor is most of the
            // frame and it is a warm grey, so every point of saturation turns it yellower
            // faster than it makes a suitcase brighter — past 60, with the old +12 of
            // warmth, the hall reads as sand. Cooling the balance buys the saturation.
            var balance = GetOrAdd<WhiteBalance>(profile);
            balance.temperature.overrideState = true;
            balance.temperature.value = 4f;

            // Measured, not estimated: the frame peaks at about 0.76 before post, so a
            // threshold of 1.6 meant bloom did nothing at all. 0.72 lets only the ceiling
            // panels and the brightest lit faces glow. It stays low — the white blowout
            // this scene had was NaN being smeared by bloom, not brightness, and a heavy
            // bloom over a hall this pale goes to milk immediately.
            var bloom = GetOrAdd<Bloom>(profile);
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.55f;
            bloom.threshold.overrideState = true;

            // Measured again after the lighting pass, because the numbers moved: the frame
            // now peaks at 2.74 on the emissive ceiling panels and the white floor sits
            // around 0.8. At 0.72 a third of the frame was over the threshold — the floor
            // itself — which is the same mistake as the original 1.0, just from the other
            // side. 1.15 leaves the panels and the sharpest speculars and nothing else.
            bloom.threshold.value = 1.15f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.6f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.7f;

            var vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.16f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.6f;

            EditorUtility.SetDirty(profile);
            return profile;
        }

        // An override lives in two places: the profile's component list, and the profile
        // asset itself as a sub-object. Add() only does the first, so a profile built this
        // way loads back with a list of nulls and no post at all.
        static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing)) return existing;

            var component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        static void AssignProfile(VolumeProfile profile)
        {
            var volume = Object.FindFirstObjectByType<Volume>(FindObjectsInactive.Include);
            if (volume == null)
            {
                var go = new GameObject("Global Volume");
                volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10f;
            }
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
        }

        static Scene OpenOrCreateScene()
        {
            if (System.IO.File.Exists(ScenePath))
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            return scene;
        }

        static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            string built = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }
    }
}
