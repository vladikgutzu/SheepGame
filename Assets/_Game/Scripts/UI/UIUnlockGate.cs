using System;
using SheepGame.Core.Meta;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// Keeps pieces of the interface off screen until the player has reached the level they
    /// belong to -- a booster before it has been handed over, the coin balance on the very
    /// first level.
    /// </summary>
    // Lives on a parent that is always on and switches its targets, never itself: a component
    // on the thing it hides is never woken to show it again.
    public class UIUnlockGate : MonoBehaviour
    {
        public const string Coins = "hud.coins";
        public const string Daily = "menu.daily";

        [Serializable]
        public struct Entry
        {
            [Tooltip("An item id from ItemIds, or hud.coins / menu.daily.")]
            public string key;
            public GameObject target;
        }

        public GameManager gameManager;
        public Entry[] entries;

        void OnEnable()
        {
            if (gameManager != null) gameManager.LevelStateChanged += HandleLevelState;
            GameState.Changed += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            if (gameManager != null) gameManager.LevelStateChanged -= HandleLevelState;
            GameState.Changed -= Refresh;
        }

        void HandleLevelState(int level, int tapsLeft, int budget) => Refresh();

        public void Refresh()
        {
            if (entries == null) return;

            foreach (var entry in entries)
            {
                if (entry.target == null) continue;
                bool open = gameManager == null || gameManager.IsUnlocked(entry.key);

                // A target that knows how to look locked stays on screen and says when it opens.
                var lockable = entry.target.GetComponent<UILockableButton>();
                if (lockable != null)
                {
                    if (!entry.target.activeSelf) entry.target.SetActive(true);
                    lockable.SetLocked(!open, gameManager != null ? gameManager.UnlockLevelOf(entry.key) : 0);
                    continue;
                }

                if (entry.target.activeSelf != open) entry.target.SetActive(open);
            }
        }
    }
}
