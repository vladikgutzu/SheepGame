using SheepGame.Core.Meta;
using TMPro;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// Badge on a booster button showing how many are left, and a plus sign when there
    /// are none.
    /// </summary>
    // The empty state has to look like an offer rather than a disabled button: a booster
    // showing "0" reads as broken, while one showing "+" reads as something to tap. The
    // button stays enabled either way — pressing an empty booster is what opens the shop.
    public class UIItemCounter : MonoBehaviour
    {
        [Tooltip("Item this badge counts. See ItemIds.")]
        public string itemId;

        public TMP_Text countLabel;
        [Tooltip("Shown instead of the number when the player has none.")]
        public GameObject emptyIcon;

        void OnEnable()
        {
            GameState.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => GameState.Changed -= Refresh;

        void Refresh()
        {
            int count = string.IsNullOrEmpty(itemId) ? 0 : GameState.ItemCount(itemId);
            bool empty = count <= 0;

            if (countLabel != null)
            {
                countLabel.gameObject.SetActive(!empty);
                if (!empty) countLabel.text = count.ToString();
            }

            if (emptyIcon != null) emptyIcon.SetActive(empty);
        }
    }
}
