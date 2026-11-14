using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGame.Core
{
    /// <summary>
    /// The cart at the top and the queue at the bottom. Draws what ColourMatchRules decides,
    /// with a little suitcase flying from the board to wherever each case went.
    /// </summary>
    // Every case on screen is a token that knows where it is headed and nothing else. The
    // rules can move a case again before it lands -- it is queued, then the cart it was waiting
    // for pulls in while it is still in the air -- and a token simply turns towards its new
    // place. Nothing here keeps a timeline, so nothing can fall out of step with the rules.
    public class UIColourMatchHud : MonoBehaviour
    {
        [Header("Cart")]
        [Tooltip("The card that punches when a cart fills and a new one pulls in.")]
        public RectTransform cartCard;
        [Tooltip("Painted the colour of the cart that is waiting.")]
        public Image cartFill;
        [Tooltip("Where boarding cases sit, one per seat.")]
        public RectTransform[] cartSeats;
        [Tooltip("The colours of the carts after this one, nearest first.")]
        public Image[] nextChips;
        [Tooltip("Optional. How many carts are still to come.")]
        public TMP_Text cartsLeftLabel;
        [Tooltip("Shown in place of the seats once the last cart has gone.")]
        public GameObject doneMark;

        [Header("Queue")]
        public RectTransform queueTray;
        [Tooltip("Every place the queue can ever have, left to right. Only as many as the " +
                 "level allows are shown.")]
        public RectTransform[] queueSlots;
        [Tooltip("Where a case with nowhere to go hangs, shaking.")]
        public RectTransform overflowAnchor;
        [Tooltip("Optional. The booster row: the tray sits above it when any booster is " +
                 "showing and drops into its place when none is.")]
        public RectTransform boosterRow;
        public float trayYWithBoosters = 470f;
        public float trayYWithoutBoosters = 230f;

        [Header("Tokens")]
        [Tooltip("Parent for the flying suitcases. Stretched over the canvas.")]
        public RectTransform tokenLayer;
        [Tooltip("An inactive suitcase to copy. Its first child image is the part that is coloured.")]
        public RectTransform tokenTemplate;
        public float flightTime = 0.42f;
        [Tooltip("How far the flight bows upwards, in canvas units.")]
        public float arc = 140f;
        public float departTime = 0.32f;

        sealed class Token
        {
            public int Id;
            public RectTransform Rect;
            public RectTransform Target;
            public Vector3 From;
            public float T;
            public bool Landed;
            public int Cart = -1;
            public bool Leaving;
            public float LeaveT;
        }

        readonly List<Token> _tokens = new List<Token>();
        readonly Queue<RectTransform> _pool = new Queue<RectTransform>();
        readonly HashSet<int> _filled = new HashSet<int>();

        readonly List<Color> _carts = new List<Color>();
        int _capacity = 3;
        int _queueCapacity;
        int _shownCart;
        bool _departing;
        float _departT;
        float _cardPunch;

        Canvas _canvas;
        bool _drawBags = true;

        /// <summary>No token is in the air and no cart is on its way out.</summary>
        public bool Idle
        {
            get
            {
                if (_departing || _filled.Contains(_shownCart)) return false;
                foreach (var t in _tokens) if (!t.Landed || t.Leaving) return false;
                return true;
            }
        }

        float Scale
        {
            get
            {
                if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
                return _canvas != null ? _canvas.rootCanvas.scaleFactor : 1f;
            }
        }

        /// <summary>
        /// Whether the cart and the queue are drawn here at all. Off when the check-in lane in
        /// the scene already shows both -- the terminal is the cart, the bays are the queue --
        /// and two copies of every suitcase on screen is one too many.
        /// </summary>
        public void SetDrawBags(bool on)
        {
            _drawBags = on;
            if (!on) Clear();
            // The card's slot carries its lip, so the whole slot goes.
            if (cartCard != null)
            {
                var slot = cartCard.parent != null && cartCard.parent != transform ? cartCard.parent : cartCard;
                slot.gameObject.SetActive(on);
            }
            if (queueTray != null) queueTray.gameObject.SetActive(on);
        }

        /// <summary>A new round: these carts, this many seats in each, this much queue.</summary>
        public void Begin(IReadOnlyList<Color> carts, int capacity, int queueCapacity)
        {
            Clear();
            if (!_drawBags) return;
            _carts.AddRange(carts);
            _capacity = Mathf.Max(1, capacity);
            _shownCart = 0;
            SetQueueCapacity(queueCapacity, animate: false);
            RefreshCart(animate: false);
        }

        public void Clear()
        {
            foreach (var t in _tokens) Recycle(t.Rect);
            _tokens.Clear();
            _filled.Clear();
            _carts.Clear();
            _departing = false;
            _cardPunch = 0f;
            if (cartCard != null) cartCard.localScale = Vector3.one;
        }

        public void SetQueueCapacity(int capacity, bool animate = true)
        {
            int before = _queueCapacity;
            _queueCapacity = capacity;

            if (queueSlots == null || !_drawBags) return;
            for (int i = 0; i < queueSlots.Length; i++)
            {
                if (queueSlots[i] == null) continue;
                bool on = i < capacity;
                queueSlots[i].gameObject.SetActive(on);
                if (on && animate && i >= before) queueSlots[i].localScale = Vector3.one * 0.2f;
            }
        }

        // ---- what the rules decided ----------------------------------------------------

        public void BagToCart(int id, Color colour, int cart, int seat, Vector3 screenFrom)
        {
            if (!_drawBags) return;
            var token = Find(id) ?? Spawn(id, colour, screenFrom);
            token.Cart = cart;
            Send(token, Seat(seat));
        }

        public void BagToQueue(int id, Color colour, int slot, Vector3 screenFrom)
        {
            if (!_drawBags) return;
            var token = Find(id) ?? Spawn(id, colour, screenFrom);
            token.Cart = -1;
            Send(token, Slot(slot));
        }

        public void QueueToCart(int id, int cart, int seat)
        {
            var token = Find(id);
            if (token == null) return;
            token.Cart = cart;
            Send(token, Seat(seat));
        }

        /// <summary>The queue closed up. The ids are the waiting cases, front first.</summary>
        public void QueueReordered(IReadOnlyList<int> ids)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                var token = Find(ids[i]);
                if (token == null || token.Cart >= 0) continue;
                var slot = Slot(i);
                if (token.Target != slot) Send(token, slot);
            }
        }

        public void Overflow(int id, Color colour, Vector3 screenFrom)
        {
            if (!_drawBags) return;
            var token = Find(id) ?? Spawn(id, colour, screenFrom);
            token.Cart = -1;
            Send(token, overflowAnchor != null ? overflowAnchor : queueTray);
        }

        public void CartFilled(int cart)
        {
            if (_drawBags) _filled.Add(cart);
        }

        // ---- drawing -------------------------------------------------------------------

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float scale = Scale;

            PlaceTray();

            for (int i = _tokens.Count - 1; i >= 0; i--)
            {
                var t = _tokens[i];

                if (t.Leaving)
                {
                    t.LeaveT += dt / Mathf.Max(0.05f, departTime);
                    float k = Mathf.Clamp01(t.LeaveT);
                    if (t.Target != null) t.Rect.position = t.Target.position + Vector3.up * (k * k * 160f * scale);
                    t.Rect.localScale = Vector3.one * Mathf.Lerp(1f, 0f, k * k);
                    if (k >= 1f)
                    {
                        Recycle(t.Rect);
                        _tokens.RemoveAt(i);
                    }
                    continue;
                }

                if (t.Target == null) continue;
                Vector3 to = t.Target.position;

                if (!t.Landed)
                {
                    t.T += dt / Mathf.Max(0.05f, flightTime);
                    float k = Mathf.Clamp01(t.T);
                    float ease = 1f - (1f - k) * (1f - k);
                    t.Rect.position = Vector3.LerpUnclamped(t.From, to, ease)
                                    + Vector3.up * (Mathf.Sin(k * Mathf.PI) * arc * scale);
                    t.Rect.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, k);

                    if (k >= 1f)
                    {
                        t.Landed = true;
                        if (t.Cart >= 0) _cardPunch = 1f;
                    }
                    continue;
                }

                // Landed: stays with its place, which may itself be moving, and a case with
                // nowhere to go shakes where it hangs.
                Vector3 shake = t.Target == overflowAnchor
                    ? Vector3.right * (Mathf.Sin(Time.unscaledTime * 40f) * 8f * scale)
                    : Vector3.zero;
                t.Rect.position = to + shake;
                t.Rect.localScale = Vector3.one;
            }

            TickDeparture(dt);
            TickSlots(dt);

            if (cartCard != null)
            {
                _cardPunch = Mathf.MoveTowards(_cardPunch, 0f, dt * 5f);
                cartCard.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(_cardPunch * Mathf.PI));
            }
        }

        // A full cart waits until every case bound for it has landed, then its cases lift off
        // together and the next colour pulls in.
        void TickDeparture(float dt)
        {
            if (!_departing)
            {
                if (!_filled.Contains(_shownCart)) return;

                int aboard = 0;
                foreach (var t in _tokens)
                {
                    if (t.Cart != _shownCart) continue;
                    if (!t.Landed) return;
                    aboard++;
                }
                if (aboard < _capacity) return;

                foreach (var t in _tokens)
                    if (t.Cart == _shownCart) { t.Leaving = true; t.LeaveT = 0f; }

                _departing = true;
                _departT = 0f;
                SheepGame.Audio.AudioManager.PieceLand();
                return;
            }

            _departT += dt / Mathf.Max(0.05f, departTime);
            if (_departT < 1f) return;

            _departing = false;
            _filled.Remove(_shownCart);
            _shownCart++;
            _cardPunch = 1f;
            RefreshCart(animate: true);
        }

        void TickSlots(float dt)
        {
            if (queueSlots == null) return;
            foreach (var slot in queueSlots)
                if (slot != null && slot.gameObject.activeSelf && slot.localScale.x < 1f)
                    slot.localScale = Vector3.one * Mathf.MoveTowards(slot.localScale.x, 1f, dt * 4f);
        }

        void RefreshCart(bool animate)
        {
            bool done = _shownCart >= _carts.Count;

            if (cartFill != null)
                cartFill.color = done ? new Color(0.36f, 0.78f, 0.45f) : _carts[_shownCart];

            if (cartSeats != null)
                foreach (var seat in cartSeats)
                    if (seat != null) seat.gameObject.SetActive(!done);

            if (doneMark != null) doneMark.SetActive(done);

            if (nextChips != null)
            {
                for (int i = 0; i < nextChips.Length; i++)
                {
                    if (nextChips[i] == null) continue;
                    int index = _shownCart + 1 + i;
                    bool on = index < _carts.Count;
                    // The chip's parent carries its outline, so the whole chip goes.
                    var chip = nextChips[i].transform.parent != null && nextChips[i].transform.parent != transform
                        ? nextChips[i].transform.parent.gameObject
                        : nextChips[i].gameObject;
                    chip.SetActive(on);
                    if (on) nextChips[i].color = _carts[index];
                }
            }

            if (cartsLeftLabel != null)
                cartsLeftLabel.text = done ? "" : "x" + (_carts.Count - _shownCart);
        }

        void PlaceTray()
        {
            if (queueTray == null || boosterRow == null) return;

            bool anyBooster = false;
            foreach (Transform child in boosterRow)
                if (child.gameObject.activeSelf && child.name.EndsWith("Slot")) { anyBooster = true; break; }

            var p = queueTray.anchoredPosition;
            p.y = anyBooster ? trayYWithBoosters : trayYWithoutBoosters;
            queueTray.anchoredPosition = p;
        }

        // ---- tokens --------------------------------------------------------------------

        RectTransform Seat(int seat)
        {
            if (cartSeats == null || cartSeats.Length == 0) return cartCard;
            return cartSeats[Mathf.Clamp(seat, 0, cartSeats.Length - 1)];
        }

        RectTransform Slot(int slot)
        {
            if (queueSlots == null || queueSlots.Length == 0) return queueTray;
            return queueSlots[Mathf.Clamp(slot, 0, queueSlots.Length - 1)];
        }

        Token Find(int id)
        {
            foreach (var t in _tokens) if (t.Id == id && !t.Leaving) return t;
            return null;
        }

        static void Send(Token token, RectTransform target)
        {
            token.From = token.Rect.position;
            token.Target = target;
            token.T = 0f;
            token.Landed = false;
        }

        Token Spawn(int id, Color colour, Vector3 screenFrom)
        {
            RectTransform rect = _pool.Count > 0 ? _pool.Dequeue() : null;
            if (rect == null && tokenTemplate != null)
                rect = Instantiate(tokenTemplate, tokenLayer != null ? tokenLayer : transform);

            if (rect == null) return new Token { Id = id, Rect = new GameObject("Token", typeof(RectTransform)).GetComponent<RectTransform>() };

            rect.gameObject.SetActive(true);
            rect.SetAsLastSibling();
            rect.position = new Vector3(screenFrom.x, screenFrom.y, 0f);
            rect.localScale = Vector3.one * 1.25f;

            // The template's own image is the dark outline; the first image under it is the case.
            var images = rect.GetComponentsInChildren<Image>(true);
            if (images.Length > 1) images[1].color = colour;
            else if (images.Length == 1) images[0].color = colour;

            var token = new Token { Id = id, Rect = rect, From = rect.position };
            _tokens.Add(token);
            return token;
        }

        void Recycle(RectTransform rect)
        {
            if (rect == null) return;
            rect.gameObject.SetActive(false);
            _pool.Enqueue(rect);
        }
    }
}
