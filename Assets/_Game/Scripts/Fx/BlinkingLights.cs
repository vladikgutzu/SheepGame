using UnityEngine;

namespace SheepGame.Fx
{
    // Runs a slow chase along a row of indicator lamps. Each renderer gets its own phase
    // from its position in the list, so the row reads as a sequence rather than as
    // everything flashing at once — which is the difference between a machine that is
    // running and a string of fairy lights.
    //
    // One property block, reused, and emission only: no lights are added to the scene. A
    // phone forward renderer would pay for every real one of these.
    [ExecuteAlways]
    public class BlinkingLights : MonoBehaviour
    {
        public Renderer[] lamps = new Renderer[0];
        public Color colour = new Color(1f, 0.72f, 0.15f);

        [Tooltip("Seconds for the chase to travel the whole row.")]
        public float period = 2.4f;

        [Tooltip("How much of the row is lit at once, as a fraction.")]
        [Range(0.05f, 0.9f)] public float duty = 0.22f;

        public float brightness = 3.5f;

        MaterialPropertyBlock _block;
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");

        void OnEnable() => _block = new MaterialPropertyBlock();

        void Update()
        {
            if (lamps == null || lamps.Length == 0) return;
            if (_block == null) _block = new MaterialPropertyBlock();

            float time = Application.isPlaying ? Time.unscaledTime : (float)UnityEditor_Time();
            float head = Mathf.Repeat(time / Mathf.Max(0.05f, period), 1f);

            for (int i = 0; i < lamps.Length; i++)
            {
                if (lamps[i] == null) continue;

                float at = (float)i / lamps.Length;
                float distance = Mathf.Abs(Mathf.Repeat(at - head + 0.5f, 1f) - 0.5f);
                float lit = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(distance / duty));

                lamps[i].GetPropertyBlock(_block);
                _block.SetColor(Emission, colour * (0.15f + lit * brightness));
                lamps[i].SetPropertyBlock(_block);
            }
        }

        // Time.unscaledTime does not advance in the editor outside play mode; the editor's
        // own clock does, which is what keeps the chase visible while dressing the scene.
        static double UnityEditor_Time()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.unscaledTimeAsDouble;
#endif
        }
    }
}
