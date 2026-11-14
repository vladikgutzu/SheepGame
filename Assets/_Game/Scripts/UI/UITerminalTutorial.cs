using SheepGame.Core.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGame.Core
{
    /// <summary>One-time, world-anchored explanations of the terminal and its upcoming colours.</summary>
    public sealed class UITerminalTutorial : MonoBehaviour
    {
        const string TerminalKey = "checkin.terminal.v1";
        const string QueueKey = "checkin.upcoming.v1";
        GameManager _game;
        CheckInTerminal _terminal;
        Canvas _canvas;
        RectTransform _root, _card;
        readonly RectTransform[] _shade = new RectTransform[4];
        readonly RectTransform[] _outline = new RectTransform[4];
        TMP_Text _title, _body, _buttonText;
        Button _next;
        string _key;
        float _shownAt;
        int _dismissedFrame = -10;
        Transform _handParent;
        int _handSibling;
        bool _borrowedHand;

        public bool IsOpen => _key != null;
        public bool BlocksInput => IsOpen || Time.frameCount <= _dismissedFrame + 1;

        public void Setup(GameManager game) => _game = game;

        public bool TryShow()
        {
            if (IsOpen) return true;
            if (_game == null || _game.LevelOver || _game.Rules == null ||
                (_game.mainMenu != null && _game.mainMenu.IsOpen)) return false;
            _terminal = _game.board != null && _game.board.checkIn != null
                ? _game.board.checkIn.terminal : null;
            if (_terminal == null || !_terminal.gameObject.activeInHierarchy) return false;

            bool terminal = !GameState.IntroSeen(TerminalKey);
            if (!terminal && (GameState.IntroSeen(QueueKey) || FirstUpcoming() == null)) return false;
            if (!Build()) return false;
            _key = terminal ? TerminalKey : QueueKey;
            _title.text = terminal ? "MATCH THE TERMINAL" : "NEXT COLOURS";
            _body.text = terminal
                ? $"Send bags of the terminal's colour.\nEach light is one bag. Fill all {_game.Rules.CartCapacity} to finish the group."
                : "These show the colours coming next.\nThe closest one to the terminal goes first.";
            _buttonText.text = terminal && FirstUpcoming() != null && !GameState.IntroSeen(QueueKey)
                ? "NEXT" : "GOT IT!";
            _shownAt = Time.unscaledTime;
            _next.interactable = false;
            _root.gameObject.SetActive(true);
            BorrowHand();
            Canvas.ForceUpdateCanvases();
            Layout();
            return true;
        }

        Renderer FirstUpcoming()
        {
            if (_terminal.upcoming != null)
                foreach (var part in _terminal.upcoming)
                    if (part != null && part.gameObject.activeInHierarchy) return part;
            return null;
        }

        void Advance()
        {
            if (!IsOpen || Time.unscaledTime - _shownAt < 0.4f) return;
            GameState.MarkIntroSeen(_key);
            GameState.FlushNow();
            Cancel();
            if (!TryShow()) _game.BeginPlay();
        }

        public void Cancel()
        {
            _key = null;
            _dismissedFrame = Time.frameCount;
            ReturnHand();
            if (_root != null) _root.gameObject.SetActive(false);
        }

        void BorrowHand()
        {
            var hand = _game.tutorialHand;
            if (hand == null || hand.body == null) return;
            hand.Hide();
            _handParent = hand.body.transform.parent;
            _handSibling = hand.body.transform.GetSiblingIndex();
            hand.body.transform.SetParent(_root, true);
            _borrowedHand = true;
            // The overlay consumes all input, including the hand image itself.
        }

        void ReturnHand()
        {
            if (!_borrowedHand) return;
            _borrowedHand = false;
            var hand = _game != null ? _game.tutorialHand : null;
            if (hand == null || hand.body == null) return;
            hand.Hide();
            hand.body.transform.SetParent(_handParent, true);
            hand.body.transform.SetSiblingIndex(_handSibling);
        }

        void LateUpdate()
        {
            if (!IsOpen) return;
            if (_terminal == null || _game.LevelOver ||
                (_game.mainMenu != null && _game.mainMenu.IsOpen)) { Cancel(); return; }
            // Let the existing system-pause UI take over on app focus loss. No global
            // timeScale changes here, so resuming cannot leave the game frozen.
            bool paused = _game.uiBindings != null && _game.uiBindings.pausePanel != null &&
                          _game.uiBindings.pausePanel.IsOpen;
            _root.gameObject.SetActive(!paused);
            if (paused) return;
            _next.interactable = Time.unscaledTime - _shownAt >= 0.4f;
            Layout();
        }

        bool Build()
        {
            if (_root != null) return true;
            var sample = _game.levelLabel;
            _canvas = sample != null ? sample.GetComponentInParent<Canvas>() : null;
            if (_canvas == null) return false;
            _canvas = _canvas.rootCanvas;
            _root = Rect("TerminalTutorial", _canvas.transform);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = _root.offsetMax = Vector2.zero;
            var overlay = _root.gameObject.AddComponent<Canvas>();
            overlay.overrideSorting = true;
            overlay.sortingOrder = _canvas.sortingOrder + 100;
            _root.gameObject.AddComponent<GraphicRaycaster>();
            var input = _root.gameObject.AddComponent<Image>();
            input.color = Color.clear;
            for (int i = 0; i < 4; i++)
            {
                _shade[i] = ImageRect("Shade", _root, new Color(0.02f, 0.07f, 0.12f, 0.78f));
                _outline[i] = ImageRect("Focus", _root, new Color(1f, 0.84f, 0.25f));
            }
            _card = ImageRect("Explanation", _root, new Color(0.77f, 0.93f, 0.98f));
            var source = _game.modeBanner != null && _game.modeBanner.card != null
                ? _game.modeBanner.card.GetComponent<Image>() : null;
            // Reuse the project's themed frame rather than introducing another visual style.
            var cardImage = _card.GetComponent<Image>();
            if (source != null) { cardImage.sprite = source.sprite; cardImage.type = Image.Type.Sliced; }
            _title = Label("Heading", _card, sample, 46);
            _body = Label("ExplanationText", _card, sample, 36);
            var button = ImageRect("Continue", _card, new Color(0.20f, 0.68f, 0.94f));
            var buttonImage = button.GetComponent<Image>();
            buttonImage.raycastTarget = true;
            buttonImage.sprite = cardImage.sprite;
            buttonImage.type = Image.Type.Sliced;
            _next = button.gameObject.AddComponent<Button>();
            _next.targetGraphic = buttonImage;
            _next.onClick.AddListener(Advance);
            _next.navigation = new Navigation { mode = Navigation.Mode.None };
            _buttonText = Label("Label", button, sample, 40);
            return true;
        }

        void Layout()
        {
            var camera = _game.board.ViewCamera;
            if (camera == null) return;
            var parts = _key == TerminalKey ? _terminal.colourParts : _terminal.upcoming;
            bool found = false;
            Bounds bounds = default;
            if (parts != null) foreach (var part in parts)
            {
                if (part == null || !part.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = part.bounds; found = true; }
                else bounds.Encapsulate(part.bounds);
            }
            if (_key == TerminalKey && found && _terminal.seatLamps != null)
                foreach (var lamp in _terminal.seatLamps)
                    if (lamp != null && lamp.gameObject.activeInHierarchy) bounds.Encapsulate(lamp.bounds);
            if (!found) return;
            var uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            Vector2 low = new Vector2(float.MaxValue, float.MaxValue), high = -low;
            for (int i = 0; i < 8; i++)
            {
                var p = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, camera.WorldToScreenPoint(p), uiCamera, out var point);
                low = Vector2.Min(low, point); high = Vector2.Max(high, point);
            }
            Rect area = _root.rect;
            low = Vector2.Max(low - Vector2.one * 18, area.min);
            high = Vector2.Min(high + Vector2.one * 18, area.max);
            Box(_shade[0], area.xMin, area.yMin, low.x - area.xMin, area.height);
            Box(_shade[1], high.x, area.yMin, area.xMax - high.x, area.height);
            Box(_shade[2], low.x, area.yMin, high.x - low.x, low.y - area.yMin);
            Box(_shade[3], low.x, high.y, high.x - low.x, area.yMax - high.y);
            float line = 3f + Mathf.Sin(Time.unscaledTime * 3f) * 0.6f;
            Box(_outline[0], low.x, low.y, line, high.y - low.y);
            Box(_outline[1], high.x - line, low.y, line, high.y - low.y);
            Box(_outline[2], low.x, low.y, high.x - low.x, line);
            Box(_outline[3], low.x, high.y - line, high.x - low.x, line);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, Screen.safeArea.min, uiCamera, out var safeMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, Screen.safeArea.max, uiCamera, out var safeMax);
            float width = Mathf.Min(880f, safeMax.x - safeMin.x - 48f);
            float height = 420f;
            float x = (safeMin.x + safeMax.x - width) * 0.5f;
            float y = Mathf.Max(safeMin.y + 24f, low.y - height - 110f);
            Box(_card, x, y, width, height);
            Box(_title.rectTransform, 24, height - 90, width - 48, 64);
            Box(_body.rectTransform, 32, 148, width - 64, 165);
            var button = (RectTransform)_next.transform;
            Box(button, (width - 320) * 0.5f, 30, 320, 90);
            Box(_buttonText.rectTransform, 8, 10, 304, 72);

            var upcoming = FirstUpcoming();
            if (_game.tutorialHand != null && (_key == TerminalKey || upcoming != null))
            {
                var target = _key == TerminalKey ? _terminal.transform : upcoming.transform;
                var aim = _key == TerminalKey ? bounds.center : upcoming.bounds.center;
                _game.tutorialHand.PointAt(target, camera, aim - target.position);
            }
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            return rect;
        }

        static RectTransform ImageRect(string name, Transform parent, Color colour)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = colour; image.raycastTarget = false;
            return rect;
        }

        static TMP_Text Label(string name, Transform parent, TMP_Text sample, float size)
        {
            var label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = sample.font;
            label.fontSharedMaterial = sample.fontSharedMaterial;
            label.fontSize = size;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        static void Box(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(Mathf.Max(0, width), Mathf.Max(0, height));
        }

        void OnDisable() => Cancel();
        void OnDestroy()
        {
            ReturnHand();
            if (_root != null) Destroy(_root.gameObject);
        }
    }
}
