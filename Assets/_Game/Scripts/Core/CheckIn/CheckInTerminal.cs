using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// The check-in terminal at the end of the queue belt. It wears the colour of the cart
    /// being loaded, lights one lamp per case taken, and shows the colour after it.
    /// </summary>
    // Only looks. Which colour it is and when it changes is CheckInLane's call, made from the
    // cases that have physically gone in rather than from the rules -- the rules move on to
    // the next cart the moment the last case *arrives*, and a terminal that changed colour
    // then would swallow that last case in the wrong colour.
    //
    // Colours go through MaterialPropertyBlocks, as the pieces' tints do, and unconverted for
    // the same reason: the palette colour a case wears and the colour its terminal wears have
    // to be the same picture.
    public class CheckInTerminal : MonoBehaviour
    {
        [Tooltip("Renderers that wear the cart's colour.")]
        public Renderer[] colourParts;
        [Tooltip("One lamp per seat in the cart, lit as cases go in.")]
        public Renderer[] seatLamps;
        [Tooltip("The carts still to come, nearest first: a line beside the terminal, like the " +
                 "queue of passengers outside a station. Each wears its cart's colour.")]
        public Renderer[] upcoming;
        [Tooltip("Scaled for the punch when a case goes in and when the colour changes.")]
        public Transform punch;

        [Tooltip("Colour shown when there is no cart to load: before a level, and after the last one.")]
        public Color idleColour = new Color(0.86f, 0.93f, 0.96f);
        public Color lampOff = new Color(0.22f, 0.30f, 0.36f);
        public Color lampOn = Color.white;
        [Tooltip("How much of the colour is added again as glow, so the terminal reads from across the hall.")]
        [Range(0f, 2f)] public float glow = 0.35f;
        [Tooltip("Seconds the colour takes to change.")]
        public float changeTime = 0.25f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock _block;
        Color _from, _to, _shown;
        float _t = 1f;
        float _punch;
        Vector3 _punchRest = Vector3.one;
        bool _hasRest;

        public bool Changing => _t < 1f;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            _shown = _from = _to = idleColour;
            if (punch != null) { _punchRest = punch.localScale; _hasRest = true; }
        }

        public void Show(Color colour, bool instant)
        {
            if (_block == null) Awake();
            _from = _shown;
            _to = colour;
            _t = instant || changeTime <= 0f ? 1f : 0f;
            if (instant) Paint(colour);
        }

        public void ShowIdle(bool instant) => Show(idleColour, instant);

        public void SetSeats(int lit)
        {
            if (_block == null) Awake();
            if (seatLamps == null) return;
            for (int i = 0; i < seatLamps.Length; i++)
            {
                var lamp = seatLamps[i];
                if (lamp == null) continue;
                bool on = i < lit;
                lamp.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, on ? lampOn : lampOff);
                _block.SetColor(EmissionId, on ? lampOn * 0.9f : Color.black);
                lamp.SetPropertyBlock(_block);
            }
        }

        /// <summary>Shows the carts after the one at the terminal, nearest first; the rest of the line goes dark.</summary>
        public void SetUpcoming(IList<Color> colours, int from)
        {
            if (_block == null) Awake();
            if (upcoming == null) return;
            for (int i = 0; i < upcoming.Length; i++)
            {
                var cart = upcoming[i];
                if (cart == null) continue;
                int index = from + i;
                bool shown = colours != null && index < colours.Count;
                cart.gameObject.SetActive(shown);
                if (!shown) continue;

                cart.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, colours[index]);
                _block.SetColor(EmissionId, colours[index] * glow * 0.6f);
                cart.SetPropertyBlock(_block);
            }
        }

        public void Punch(float amount = 1f) => _punch = Mathf.Max(_punch, amount);

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (_t < 1f)
            {
                _t = Mathf.Min(1f, _t + dt / Mathf.Max(0.01f, changeTime));
                float k = _t * _t * (3f - 2f * _t);
                Paint(Color.Lerp(_from, _to, k));
            }

            if (punch != null && _hasRest)
            {
                if (_punch > 0f) _punch = Mathf.Max(0f, _punch - dt / 0.28f);
                // Up then settle: a sine over the decay reads as a bump, not a wobble.
                float s = 1f + Mathf.Sin(_punch * Mathf.PI) * 0.08f;
                punch.localScale = _punchRest * s;
            }
        }

        void Paint(Color colour)
        {
            _shown = colour;
            if (colourParts == null) return;
            foreach (var part in colourParts)
            {
                if (part == null) continue;
                part.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, colour);
                _block.SetColor(EmissionId, colour * glow);
                part.SetPropertyBlock(_block);
            }
        }
    }
}
