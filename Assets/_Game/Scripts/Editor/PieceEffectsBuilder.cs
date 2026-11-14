using SheepGame.Core;
using SheepGame.Fx;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Builds the board's two particle systems and wires them up.
    //
    // Two systems for the whole game, emitted into by position — see PieceEffects for why
    // that beats a prefab per burst. Sized off the board's cell so the burst stays in
    // proportion whatever ground the board has been fitted to.
    public static class PieceEffectsBuilder
    {
        const string MaterialDir = "Assets/_Game/Materials/Fx";

        [MenuItem("SheepGame/Build Piece Effects")]
        public static void Build()
        {
            var board = Object.FindFirstObjectByType<Board>(FindObjectsInactive.Include);
            if (board == null) { Debug.LogError("No Board in the scene."); return; }

            var existing = Object.FindFirstObjectByType<PieceEffects>(FindObjectsInactive.Include);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var root = new GameObject("PieceEffects");
            var parent = GameObject.Find("MainSystemn");
            if (parent != null) root.transform.SetParent(parent.transform, false);

            var effects = root.AddComponent<PieceEffects>();
            float cell = board.cellSize;

            // Alpha blended, not additive. Additive can only add light, and this floor is
            // already near-white, so an additive spark over it is invisible however bright
            // it is made. The colour comes per burst from the piece.
            effects.sparkle = MakeSystem("Sparkle", root.transform,
                texture: TerminalTextures.Spark(),
                colour: Color.white,
                size: cell * 0.46f, speed: cell * 1.6f, lifetime: 0.55f, gravity: 0.9f, additive: false);

            effects.dust = MakeSystem("Dust", root.transform,
                texture: TerminalTextures.Puff(),
                colour: Color.white,
                size: cell * 0.52f, speed: cell * 0.5f, lifetime: 0.45f, gravity: -0.1f, additive: false);

            EditorUtility.SetDirty(effects);
            Selection.activeGameObject = root;
            Debug.Log("Piece effects built: sparkle on tap, dust on landing and on a blocked tap.");
        }

        static ParticleSystem MakeSystem(string name, Transform parent, Texture2D texture, Color colour,
                                         float size, float speed, float lifetime, float gravity, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();

            var main = system.main;
            main.loop = true;                      // the system runs; nothing spawns until Emit
            main.playOnAwake = true;
            main.startLifetime = lifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size);
            main.startColor = colour;
            main.gravityModifier = gravity;
            main.maxParticles = 120;
            // World space, because the burst is emitted at a piece's position and the piece
            // carries on moving — a local-space system would drag the sparks along with it.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = system.emission;
            emission.rateOverTime = 0f;            // Emit() only

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = size * 0.6f;

            var sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, Falloff());

            var colourOverLifetime = system.colorOverLifetime;
            colourOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.35f), new GradientAlphaKey(0f, 1f) });
            colourOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = Material(name, texture, additive);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.alignment = ParticleSystemRenderSpace.View;

            return system;
        }

        // Big at birth, gone by the end — the shape that reads as a pop rather than a puff
        // of smoke that lingers.
        static AnimationCurve Falloff()
        {
            var curve = new AnimationCurve();
            curve.AddKey(new Keyframe(0f, 0.35f));
            curve.AddKey(new Keyframe(0.25f, 1f));
            curve.AddKey(new Keyframe(1f, 0f));
            return curve;
        }

        static Material Material(string name, Texture2D texture, bool additive)
        {
            if (!AssetDatabase.IsValidFolder(MaterialDir))
                AssetDatabase.CreateFolder("Assets/_Game/Materials", "Fx");

            string path = $"{MaterialDir}/Fx_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "Fx_" + name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);

            // URP's particle shader takes its blending from a property *and* a keyword, and
            // they have to agree. `_Blend` is an enum — 0 alpha, 1 premultiply, 2 additive,
            // 3 multiply — and setting it to 1 for "additive" put the material in
            // premultiply while the blend factors said additive: the alpha in the texture
            // was ignored and every particle drew as an opaque white quad. That, not the
            // texture, was why the sparks were squares.
            material.SetFloat("_Surface", 1f);                        // transparent
            material.SetFloat("_Blend", additive ? 2f : 0f);          // additive : alpha
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", additive ? (int)UnityEngine.Rendering.BlendMode.One
                                                  : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
