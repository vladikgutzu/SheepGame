using UnityEngine;

namespace SheepGame.Fx
{
    // The three moments a suitcase is worth marking: the tap that sends it away, the landing
    // when it drops onto the belt, and the bump when a blocked one refuses to move.
    //
    // Two particle systems for the whole board, emitted into by position, rather than a
    // prefab instantiated per event. A burst of ten is not worth an Instantiate, a destroy
    // and the garbage behind it on a phone, and the alternative — pooling — is a pool of
    // objects that do nothing but wait. `ParticleSystem.Emit` with explicit params costs
    // one call.
    //
    // Each burst is tinted with the piece's own colour. The hall is deliberately near-white
    // and so were the effects, which lost them completely; and an additive blend cannot fix
    // that, because adding light to an almost-white floor changes nothing. Alpha blended and
    // carrying the suitcase's colour, the burst is both visible and legible — it says which
    // bag went.
    //
    // Everything here is optional at the call site: Board looks the component up once and
    // does nothing if the scene has none, so the game runs with the effects deleted.
    public class PieceEffects : MonoBehaviour
    {
        public static PieceEffects Instance { get; private set; }

        [Tooltip("Star burst where a piece is tapped away.")]
        public ParticleSystem sparkle;

        [Tooltip("Soft puff where a piece lands on the belt.")]
        public ParticleSystem dust;

        [Tooltip("How far each burst's colour is pushed towards full saturation.")]
        [Range(0f, 1f)] public float vividness = 0.55f;

        void OnEnable() => Instance = this;
        void OnDisable() { if (Instance == this) Instance = null; }

        public static void Sparkle(Vector3 at, Color colour, float scale = 1f)
        {
            var fx = Instance;
            if (fx == null || fx.sparkle == null) return;
            Emit(fx.sparkle, at, 10, scale, fx.Vivid(colour));
        }

        public static void Landing(Vector3 at, Color colour)
        {
            var fx = Instance;
            if (fx == null || fx.dust == null) return;
            Emit(fx.dust, at, 7, 1f, Color.Lerp(fx.Vivid(colour), Color.white, 0.45f));
        }

        // A blocked tap is a failure, so it gets the quieter of the two and less of it —
        // enough to say "that was heard", not enough to read as a reward.
        public static void Blocked(Vector3 at, Color colour)
        {
            var fx = Instance;
            if (fx == null || fx.dust == null) return;
            Emit(fx.dust, at, 4, 0.7f, Color.Lerp(fx.Vivid(colour), Color.white, 0.55f));
        }

        // Pushed towards its own hue at full strength, and away from white: a pastel case on
        // a pale floor still has to throw a burst that can be seen.
        Color Vivid(Color colour)
        {
            Color.RGBToHSV(colour, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Lerp(s, 1f, vividness), Mathf.Lerp(v, 0.95f, 0.35f));
        }

        static void Emit(ParticleSystem system, Vector3 at, int count, float scale, Color colour)
        {
            var parameters = new ParticleSystem.EmitParams
            {
                position = at,
                applyShapeToPosition = true,
                startColor = colour
            };

            // startSize rather than a multiplier: EmitParams has no multiplier, and the size
            // it overrides is the one the system was built with, so this reads it back.
            if (!Mathf.Approximately(scale, 1f))
                parameters.startSize = system.main.startSize.Evaluate(0f, 0.5f) * scale;

            system.Emit(parameters, count);
        }
    }
}
