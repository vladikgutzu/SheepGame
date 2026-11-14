using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGame.Core
{
    /// <summary>
    /// The parts of one shop row, wired by the UI builder so the runtime never has to
    /// find them by name — and the row's own vertical layout, which depends on whether the
    /// product carries a badge.
    /// </summary>
    // Name lookup inside a cloned prefab is the classic silent breakage: renaming a child
    // in the builder leaves a row that instantiates fine and shows nothing. Explicit
    // references break loudly instead, at build time.
    //
    // The metrics live here rather than in the builder because the badge is only known at
    // runtime, when the catalogue is read. An editor-only layout would have to guess, and the
    // guess is exactly the empty band this class exists to avoid.
    public class ShopRowView : MonoBehaviour
    {
        // The sticker's outline, by which the Fill child is inset from the row. Everything
        // below is measured from the row's top edge; this is the one conversion into the
        // Fill's own space, where the parts actually live.
        public const float Outline = 6f;

        // The content block: title over description on the left, coin at one end, price pill
        // at the other. 100 tall, and the same whether the row carries a badge or not.
        public const float ContentHeight = 100f;
        public const float BadgeHeight = 44f;

        // A badged row: air, badge, gap, content, air.  8 + 44 + 20 + 100 + 28 = 200.
        public const float BadgeTopGap = 8f;
        public const float BadgeContentGap = 20f;
        public const float Padding = 28f;

        /// <summary>Height of a row that carries a badge.</summary>
        public const float BadgedHeight = BadgeTopGap + BadgeHeight + BadgeContentGap + ContentHeight + Padding;

        /// <summary>Height of a row that does not. The badge's band is given back, not left empty.</summary>
        public const float PlainHeight = Padding + ContentHeight + Padding;

        // Where each part sits inside the content block, from its top.
        const float TitleOffset = 22f;
        const float MiddleOffset = 46f;        // coin, price pill and owned tag share a line
        const float DescriptionOffset = 82f;

        // Which edge each part hangs from horizontally. Unchanged by the layout; named so the
        // pinning below cannot quietly move something sideways.
        const float LeftGroupAnchorX = 0.5f;   // title, description, coin — offset from centre
        const float RightGroupAnchorX = 1f;    // price pill and owned tag hug the right edge

        public TMP_Text title;
        public TMP_Text description;
        public TMP_Text price;
        public Button buyButton;

        [Tooltip("Corner flag: BEST VALUE and so on. Hidden when the product sets no badge, " +
                 "and the row shortens by its band when it is.")]
        public GameObject badge;
        public TMP_Text badgeLabel;

        [Tooltip("Shown in place of the price on a non-consumable the player already owns.")]
        public GameObject ownedTag;

        [Tooltip("The coin disc on the left. Referenced rather than found, like everything else.")]
        public RectTransform coinIcon;
        [Tooltip("The price pill's slot — the object that gets positioned, not the button.")]
        public RectTransform buySlot;

        [System.Serializable]
        public struct ProductArtwork
        {
            public string productId;
            public Sprite sprite;
            public Color background;
        }
        [Header("Product presentation")]
        public ProductArtwork[] productArtwork;

        public void ApplyArtwork(string productId)
        {
            if (productArtwork == null) return;
            foreach (var art in productArtwork)
            {
                if (art.productId != productId) continue;
                var image = coinIcon != null ? coinIcon.GetComponent<UnityEngine.UI.Image>() : null;
                if (image != null && art.sprite != null)
                {
                    image.sprite = art.sprite;
                    image.type = UnityEngine.UI.Image.Type.Simple;
                    image.preserveAspect = true;
                    image.color = Color.white;
                }
                var background = GetComponent<UnityEngine.UI.Image>();
                if (background != null) background.color = art.background;
                return;
            }
        }

        RectTransform _rect;
        LayoutElement _element;

        /// <summary>Shows or hides the badge and lays the row out to match.</summary>
        public void SetBadge(bool visible)
        {
            if (badge != null) badge.SetActive(visible);
            ApplyLayout(visible);
        }

        /// <summary>
        /// Positions every part and sets the row's height. Safe to call on a row laid out any
        /// other way — it pins the anchors as well as the offsets.
        /// </summary>
        // The parts hang from the Fill's top edge rather than its centre, so the height can
        // change underneath them without any of them moving. Anchored to the centre, every
        // offset would have to be recomputed from the new height, and the two layouts would
        // have to agree about a number neither of them owns.
        public void ApplyLayout(bool badged)
        {
            float top = (badged ? BadgeTopGap + BadgeHeight + BadgeContentGap : Padding) - Outline;

            Pin(title != null ? title.rectTransform : null, LeftGroupAnchorX, top + TitleOffset);
            Pin(description != null ? description.rectTransform : null, LeftGroupAnchorX, top + DescriptionOffset);
            Pin(coinIcon, LeftGroupAnchorX, top + MiddleOffset);
            Pin(buySlot, RightGroupAnchorX, top + MiddleOffset);
            Pin(ownedTag != null ? (RectTransform)ownedTag.transform : null, RightGroupAnchorX, top + MiddleOffset);

            SetHeight(badged ? BadgedHeight : PlainHeight);
        }

        // Distance is passed in downwards, because that is how the row reads; anchoredPosition
        // counts the other way.
        static void Pin(RectTransform rect, float anchorX, float distanceFromTop)
        {
            if (rect == null) return;

            rect.anchorMin = new Vector2(anchorX, 1f);
            rect.anchorMax = new Vector2(anchorX, 1f);

            var position = rect.anchoredPosition;
            position.y = -distanceFromTop;
            rect.anchoredPosition = position;
        }

        void SetHeight(float height)
        {
            if (_rect == null) _rect = (RectTransform)transform;
            if (_element == null) _element = GetComponent<LayoutElement>();

            var size = _rect.sizeDelta;
            size.y = height;
            _rect.sizeDelta = size;

            // The layout group is what actually spaces the rows, and with childControlHeight
            // off it reads the height from here rather than from the rect. Both are set, or the
            // row draws at one size and is given room for another.
            if (_element != null)
            {
                _element.minHeight = height;
                _element.preferredHeight = height;
            }
        }
    }
}
