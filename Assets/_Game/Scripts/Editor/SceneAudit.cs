using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SheepGame.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SheepGame.EditorTools
{
    // The two questions that have to be re-asked every time the game moves onto a new
    // backdrop, and that were answered by eye four themes running: is anything standing in
    // the playfield, and what does the phone frame actually contain.
    //
    // Both are measurements, because eyeballing them keeps missing the same things — a cafe
    // whose bounds reach twelve units past its pivot and put a chair on the board, and a
    // band of background colour across the top of the shot that reads as "sky" until you
    // notice the room has no ceiling.
    public static class SceneAudit
    {
        // The phone the framing is tuned for, and the size the Game view is set to. Both
        // matter: the view is often left at 16:9 and every viewport figure moves with the
        // aspect, and a shot taken at a different aspect is framed from a different distance.
        const int ShotWidth = 1290, ShotHeight = 2430;

        [MenuItem("SheepGame/Audit Scene")]
        public static void Audit()
        {
            var sb = new StringBuilder();
            void P(string t) => sb.AppendLine("PROBLEM  " + t);
            void N(string t) => sb.AppendLine("note     " + t);

            var board = Object.FindFirstObjectByType<Board>(FindObjectsInactive.Include);
            var manager = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            var bindings = Object.FindFirstObjectByType<UIBindings>(FindObjectsInactive.Include);
            var camera = Camera.main;

            // ---- will it even ship ----------------------------------------------------------
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            N("build settings: " + (EditorBuildSettings.scenes.Length == 0 ? "empty"
                : string.Join(", ", EditorBuildSettings.scenes.Select(s => (s.enabled ? "" : "(off) ") + s.path))));
            if (!EditorBuildSettings.scenes.Any(s => s.enabled && s.path == scene.path))
                P($"{scene.path} is not an enabled scene in Build Settings — a build would not open it");

            // ---- wiring ----------------------------------------------------------------------
            if (board == null) { P("no Board in the scene"); Write(sb); return; }
            if (camera == null) P("no MainCamera");
            if (board.modelLibrary == null) P("Board.modelLibrary is empty");
            if (board.raycastCamera == null) P("Board.raycastCamera is empty — taps will not register");
            if (board.cameraFramer == null) P("Board.cameraFramer is empty — levels will not be framed");
            if (board.belt == null) P("Board.belt is empty — pieces will have nothing to ride");
            else if (!board.belt.gameObject.activeInHierarchy) P("the belt object is disabled");

            if (manager == null) P("no GameManager");
            else
            {
                if (manager.board == null) P("GameManager.board is empty");
                if (manager.levels == null || manager.levels.Count == 0) P("GameManager has no levels");
                if (manager.levelLabel == null || manager.remainingLabel == null ||
                    manager.winPanel == null || manager.progressBar == null)
                    P("GameManager has empty HUD slots");
            }
            if (bindings == null) P("no UIBindings");
            else if (bindings.shuffleButton == null || bindings.removeButton == null || bindings.restartButton == null)
                P("UIBindings has empty button slots");
            if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) == null)
                P("no EventSystem — no HUD button will respond");

            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (cameras.Length != 1) P($"{cameras.Length} cameras in the scene");

            // ---- what stands in the playfield --------------------------------------------------
            sb.AppendLine();
            var across = Vector3.ProjectOnPlane(board.transform.right, Vector3.up).normalized;
            var along = Vector3.ProjectOnPlane(board.transform.forward, Vector3.up).normalized;
            float halfAcross = board.fieldSize.x * 0.5f * board.cellSize;
            float halfAlong = board.fieldSize.y * 0.5f * board.cellSize;
            var centre = board.transform.position;

            N($"board at {centre:F1} yaw {board.transform.eulerAngles.y:F0}, cell {board.cellSize:F2}, " +
              $"lane {board.roadMargin:F2}, field {board.fieldSize}, " +
              $"half-extents {halfAcross:F1} x {halfAlong:F1} (with lane " +
              $"{halfAcross + board.roadMargin:F1} x {halfAlong + board.roadMargin:F1})");

            int intruders = 0;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (renderer.transform.IsChildOf(board.transform)) continue;
                if (renderer is ParticleSystemRenderer) continue;

                var b = renderer.bounds;
                if (b.min.y > centre.y + 5f) continue;                 // signage overhead is fine
                var offset = b.center - centre;

                // Measured with the prop's own footprint, in the board's axes: the board is a
                // quarter turn under the world, and a prop's pivot is regularly nowhere near
                // the middle of the thing it draws.
                float a = Mathf.Abs(Vector3.Dot(offset, across)) - Mathf.Abs(Vector3.Dot(b.extents, Abs(across)));
                float l = Mathf.Abs(Vector3.Dot(offset, along)) - Mathf.Abs(Vector3.Dot(b.extents, Abs(along)));
                if (a >= halfAcross + board.roadMargin || l >= halfAlong + board.roadMargin) continue;

                string path = PathOf(renderer.transform);
                if (path.Contains("Shell/Floor") || path.Contains("PlayfieldBand")) continue;

                // The conveyor's own dressing belongs on the lane — that is what the lane is
                // for. Only scenery that has wandered onto it is a problem.
                if (path.StartsWith("Conveyor Dressing")) continue;
                intruders++;
                P($"'{path}' reaches into the playfield — centre {b.center:F1}, size {b.size:F1}, " +
                  $"{halfAcross + board.roadMargin - a:F1} / {halfAlong + board.roadMargin - l:F1} past the lane edge");
            }
            if (intruders == 0) N("nothing stands in the playfield or its belt lane");

            // ---- levels against a fixed field ---------------------------------------------------
            sb.AppendLine();
            if (manager != null && manager.levels != null)
                foreach (var level in manager.levels.Where(l => l != null))
                {
                    string line = $"{level.name}: {level.gridWidth}x{level.gridHeight}, {level.pieces.Count} pieces";
                    bool offField = board.fieldSize.x > 0 &&
                                    (level.gridWidth != board.fieldSize.x || level.gridHeight != board.fieldSize.y);

                    // A small grid is normally a mistake, but two kinds of level mean it: one
                    // that frames its own pieces instead of the field, and the tutorial,
                    // which is small so that the one thing being taught is the only thing on
                    // screen. Neither should keep showing up as a problem to fix.
                    if (offField && (level.frameOnPieces || level.tutorialHints))
                        N(line + (level.frameOnPieces
                            ? " — smaller than the field, and frames its own pieces"
                            : " — smaller than the field on purpose (tutorial)"));
                    else if (offField)
                        P(line + $" — drawn on a grid other than the fixed field {board.fieldSize}, so it is " +
                                 "centred in it and the pieces are framed smaller than the board");
                    else N(line);
                }

            // ---- look -------------------------------------------------------------------------
            sb.AppendLine();
            N($"ambient {RenderSettings.ambientMode} {RenderSettings.ambientLight} probe0 {RenderSettings.ambientProbe[0, 0]:F3}");
            if (RenderSettings.ambientMode == AmbientMode.Skybox && RenderSettings.ambientProbe[0, 0] <= 0.001f)
                P("skybox ambient with an empty probe — everything out of the sun renders black");
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            N($"{lights.Length} light(s): " + string.Join(", ", lights.Select(l => $"{l.name} {l.type} {l.intensity:F2}")));
            var volume = Object.FindFirstObjectByType<Volume>(FindObjectsInactive.Include);
            N("volume: " + (volume != null && volume.sharedProfile != null ? volume.sharedProfile.name : "NONE"));

            string pipeline = GraphicsSettings.defaultRenderPipeline != null
                ? AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline) : "built-in";
            N("pipeline: " + pipeline);
            if (QualitySettings.renderPipeline != null &&
                AssetDatabase.GetAssetPath(QualitySettings.renderPipeline) != pipeline)
                P("the active quality level overrides the pipeline with " +
                  AssetDatabase.GetAssetPath(QualitySettings.renderPipeline));

            if (Application.isPlaying) Frame(board, camera, sb, P, N);
            else N("run this again in play mode for the framing checks — the framer only moves the camera then");

            Write(sb);
        }

        // What the phone frame is spent on, which is only knowable once the framer has
        // placed the camera for a level.
        static void Frame(Board board, Camera camera, StringBuilder sb,
                          System.Action<string> P, System.Action<string> N)
        {
            sb.AppendLine();
            float previousAspect = camera.aspect;
            camera.aspect = (float)ShotWidth / ShotHeight;
            FrameFor(board, camera);

            N($"portrait {ShotWidth}x{ShotHeight}: camera {camera.transform.position:F1}, " +
              $"distance {Vector3.Distance(camera.transform.position, board.transform.position):F1}");

            var pieces = Object.FindObjectsByType<PuzzlePiece>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (pieces.Length > 0)
            {
                var r = pieces[0].GetComponentInChildren<Renderer>();
                if (r != null && Viewport(camera, r.bounds, out Rect vp))
                {
                    N($"{pieces.Length} pieces; one reads {vp.width * 100f:F1}% of the screen width");
                    if (vp.width < 0.08f) P("pieces are under 8% of the screen width — small for a thumb");
                }
            }

            // A grid of rays against the scenery's bounds. The generated shell carries no
            // colliders, so Physics would report an empty room.
            var scenery = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                .Where(m => !m.transform.IsChildOf(board.transform)).ToArray();
            int empty = 0, cells = 0;
            var rows = new List<string>();
            for (int gy = 10; gy >= 0; gy--)
            {
                var row = new StringBuilder($"y {gy / 10f:F1}  ");
                for (int gx = 0; gx <= 10; gx++)
                {
                    var ray = camera.ViewportPointToRay(new Vector3(gx / 10f, gy / 10f, 0f));
                    float best = float.MaxValue;
                    MeshRenderer hit = null;
                    foreach (var m in scenery)
                        if (m.bounds.IntersectRay(ray, out float d) && d < best) { best = d; hit = m; }
                    cells++;
                    if (hit == null) empty++;
                    row.Append(hit == null ? " .. " : $" {Tag(hit.name)} ");
                }
                rows.Add(row.ToString());
            }
            if (empty > 0)
                P($"{empty} of {cells} probes across the frame hit nothing at all — that much of the shot is " +
                  "the camera's background colour, with no room behind it");
            foreach (var row in rows) sb.AppendLine(row);
            sb.AppendLine("        " + string.Join("", Enumerable.Range(0, 11).Select(i => $"  {i}  ")));
            sb.AppendLine("        '..' nothing   FL floor  BD band  WA wall  PI pillar  GL glazing  ?? prop");

            Radiometry(board, camera, sb, P, N);

            camera.aspect = previousAspect;
            camera.ResetAspect();
        }

        // What the frame is worth in HDR, before post touches it. Two things go wrong here and
        // neither is visible in an LDR screenshot: a surface far enough over the bloom
        // threshold to haze the shot, and a shader returning NaN — which survives every blur
        // bloom does and comes back as a white blot the size of the filter. The airport pack's
        // toon shader did exactly that and it read as "the hall is blown out".
        static void Radiometry(Board board, Camera camera, StringBuilder sb,
                               System.Action<string> P, System.Action<string> N)
        {
            var urp = camera.GetUniversalAdditionalCameraData();
            var volume = Object.FindFirstObjectByType<Volume>(FindObjectsInactive.Include);
            float threshold = volume != null && volume.sharedProfile != null &&
                              volume.sharedProfile.TryGet<Bloom>(out var bloom)
                              ? bloom.threshold.value : 1f;

            var rt = new RenderTexture(ShotWidth / 2, ShotHeight / 2, 24, RenderTextureFormat.ARGBHalf);
            bool wasPost = urp.renderPostProcessing;
            var previousTarget = camera.targetTexture;

            urp.renderPostProcessing = false;
            camera.targetTexture = rt;
            FrameFor(board, camera);
            camera.Render();

            var previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            RenderTexture.active = previousActive;

            urp.renderPostProcessing = wasPost;
            camera.targetTexture = previousTarget;

            var pixels = image.GetPixels();
            float max = 0f;
            int over = 0, broken = 0;
            var blame = new Dictionary<string, int>();
            var scenery = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) ||
                    float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b))
                {
                    broken++;
                    if (blame.Count < 200)
                    {
                        var ray = camera.ViewportPointToRay(new Vector3(
                            (float)(i % rt.width) / rt.width, (float)(i / rt.width) / rt.height, 0f));
                        float best = float.MaxValue;
                        MeshRenderer hit = null;
                        foreach (var m in scenery)
                            if (m.bounds.IntersectRay(ray, out float d) && d < best) { best = d; hit = m; }
                        string key = hit != null
                            ? $"{PathOf(hit.transform)} [{(hit.sharedMaterial != null && hit.sharedMaterial.shader != null ? hit.sharedMaterial.shader.name : "?")}]"
                            : "background";
                        blame.TryGetValue(key, out int n);
                        blame[key] = n + 1;
                    }
                    continue;
                }
                float l = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                max = Mathf.Max(max, l);
                if (l > threshold) over++;
            }

            Object.DestroyImmediate(image);
            rt.Release();
            Object.DestroyImmediate(rt);

            sb.AppendLine();
            N($"scene HDR before post: max luminance {max:F2}, bloom threshold {threshold:F2}, " +
              $"{100f * over / pixels.Length:F1}% of the frame over it");
            if (broken > 0)
            {
                P($"{broken} pixels come back NaN or infinite — bloom smears each one into a white blot");
                foreach (var pair in blame.OrderByDescending(p => p.Value).Take(6))
                    sb.AppendLine($"         {pair.Value,6}  {pair.Key}");
            }
            else N("no NaN or infinite pixels in the frame");
        }

        // ---- a shot at the aspect that ships ------------------------------------------------

        [MenuItem("SheepGame/Render Phone Shot")]
        public static void RenderPhoneShot()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("Render Phone Shot needs play mode — the camera is placed by the framer.");
                return;
            }

            var board = Object.FindFirstObjectByType<Board>();
            var manager = Object.FindFirstObjectByType<GameManager>();
            if (board == null) { Debug.LogError("No Board in the scene."); return; }

            int count = manager != null && manager.levels != null ? manager.levels.Count : 1;
            foreach (int level in new[] { 1, Mathf.Max(1, count / 2), count })
            {
                if (manager != null) manager.LoadLevel(level);
                Capture(board, Path.Combine(Path.GetTempPath(), $"phone_L{level:00}.png"));
            }
            if (manager != null) manager.LoadLevel(1);
            Debug.Log($"Phone shots written to {Path.GetTempPath()} (phone_L*.png).");
        }

        // The Game view's own back buffer. Worth having next to the off-screen render: the
        // two disagreed for a whole session, and this is the one the player is looking at.
        [MenuItem("SheepGame/Capture Game View")]
        public static void CaptureGameView()
        {
            var file = Path.Combine(Path.GetTempPath(), "gameview.png");
            if (File.Exists(file)) File.Delete(file);
            ScreenCapture.CaptureScreenshot(file);
            Debug.Log("Game view capture requested: " + file + " (written at the end of the frame)");
        }

        public static void Capture(Board board, string file)
        {
            var camera = Camera.main;

            // sRGB, which ARGB32 is in a linear project — the pipeline's last blit converts
            // into it, and an HDR target skips that conversion and writes linear values into
            // the PNG, which comes out dark and flat and is no use for judging a colour.
            //
            // The trade is that an LDR target has no headroom above 1, so **this shot
            // understates bloom** — that lie cost two rounds of "fixes". For glow, use
            // Capture Game View; for the numbers, the audit measures an HDR target instead.
            var rt = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing)
            };
            var previousTarget = camera.targetTexture;
            float previousAspect = camera.aspect;

            camera.targetTexture = rt;
            camera.aspect = (float)ShotWidth / ShotHeight;
            FrameFor(board, camera);
            camera.Render();

            var previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
            image.Apply();
            RenderTexture.active = previousActive;
            File.WriteAllBytes(file, image.EncodeToPNG());

            camera.targetTexture = previousTarget;
            camera.aspect = previousAspect;
            camera.ResetAspect();
            Object.DestroyImmediate(image);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        // Asks the board to frame the level exactly as the game does, snapped rather than
        // glided since no frame runs before the render. It used to rebuild the rectangle
        // here from fieldSize, which was the same rectangle right up until a level could opt
        // out of it — after that the shot showed a frame the player never sees.
        static void FrameFor(Board board, Camera camera)
        {
            if (board == null || camera == null) return;
            board.RefitFrame(immediate: true);
        }

        // ---- helpers --------------------------------------------------------------------------

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static string Tag(string name) =>
            name.StartsWith("Floor") ? "FL" :
            name.StartsWith("PlayfieldBand") ? "BD" :
            name.StartsWith("Wall") ? "WA" :
            name.StartsWith("Pillar") ? "PI" :
            name.StartsWith("Glazing") || name.StartsWith("Mullion") ? "GL" :
            name.StartsWith("Skirting") || name.StartsWith("Fascia") || name.StartsWith("Sill") ? "TR" : "??";

        static bool Viewport(Camera camera, Bounds b, out Rect rect)
        {
            float xMin = 9f, xMax = -9f, yMin = 9f, yMax = -9f;
            bool any = false;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y,
                                         (i & 4) == 0 ? b.min.z : b.max.z);
                var v = camera.WorldToViewportPoint(corner);
                if (v.z <= 0f) continue;
                any = true;
                xMin = Mathf.Min(xMin, v.x); xMax = Mathf.Max(xMax, v.x);
                yMin = Mathf.Min(yMin, v.y); yMax = Mathf.Max(yMax, v.y);
            }
            rect = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
            return any;
        }

        static string PathOf(Transform t)
        {
            var path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        static void Write(StringBuilder sb)
        {
            var file = Path.Combine(Path.GetTempPath(), "scene_audit.txt");
            File.WriteAllText(file, sb.ToString());
            Debug.Log("Scene audit:\n" + sb);
        }
    }
}
