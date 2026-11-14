using System;
using SheepGame.Core.Meta;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// The two in-level boosters. Each one is a counted item in the player's inventory,
    /// spent at the moment it actually does something.
    /// </summary>
    public class BoosterManager : MonoBehaviour
    {
        Board _board;
        bool _removeModeActive;

        public bool RemoveModeActive => _removeModeActive;

        public int ShuffleCount => GameState.ItemCount(ItemIds.Shuffle);
        public int RemoveCount => GameState.ItemCount(ItemIds.Remove);
        public int HintCount => GameState.ItemCount(ItemIds.Hint);

        /// <summary>Raised whenever remove mode is armed or spent, so the HUD can mirror it.</summary>
        public event Action<bool> RemoveModeChanged;

        /// <summary>
        /// Raised with the item id when a booster is pressed and the player has none.
        /// The HUD answers this by opening the shop вЂ” pressing a booster you cannot use
        /// has to lead somewhere, or it reads as a dead button.
        /// </summary>
        public event Action<string> OutOfStock;

        /// <summary>Raised with the item id once a booster has actually been spent.</summary>
        public event Action<string> Used;

        /// <summary>
        /// How much a hint should prefer each free case. Set by the GameManager, which knows
        /// what the cart is waiting for; empty, any free case will do.
        /// </summary>
        public Func<PuzzlePiece, int> hintPriority;

        public void Setup(Board board)
        {
            _board = board;
        }

        /// <summary>Shuffles the remaining pieces' facings. Spends one shuffle booster.</summary>
        public bool ActivateShuffle()
        {
            if (_board == null) return false;

            if (!GameState.TryConsumeItem(ItemIds.Shuffle))
            {
                OutOfStock?.Invoke(ItemIds.Shuffle);
                return false;
            }

            _board.ShuffleRemaining();
            Used?.Invoke(ItemIds.Shuffle);
            return true;
        }

        /// <summary>
        /// Points at a piece that can leave right now. Spends one hint booster.
        /// </summary>
        // Charged only when a piece is actually pointed at: on an empty board, or one where
        // nothing can move, there is nothing to show and taking the booster anyway would be
        // taking it for nothing.
        public bool ActivateHint()
        {
            if (_board == null) return false;

            if (GameState.ItemCount(ItemIds.Hint) <= 0)
            {
                OutOfStock?.Invoke(ItemIds.Hint);
                return false;
            }

            if (!_board.ShowHint(hintPriority)) return false;

            GameState.TryConsumeItem(ItemIds.Hint);
            Used?.Invoke(ItemIds.Hint);
            return true;
        }

        /// <summary>
        /// Arms the "next tap removes a piece" mode. Arming is free and reversible; the
        /// booster is only spent once a piece is actually taken, so a player who arms it
        /// and changes their mind has lost nothing.
        /// </summary>
        public bool ToggleRemoveMode()
        {
            if (_removeModeActive)
            {
                SetRemoveMode(false);
                return true;
            }

            if (GameState.ItemCount(ItemIds.Remove) <= 0)
            {
                OutOfStock?.Invoke(ItemIds.Remove);
                return false;
            }

            SetRemoveMode(true);
            return true;
        }

        public bool ConsumeRemoveModeForClick(PuzzlePiece piece)
        {
            if (!_removeModeActive) return false;

            SetRemoveMode(false);

            // Checked again here rather than trusted from arming time: the shop can be
            // opened with the booster armed, and a refund or a second scene could have
            // moved the count underneath us.
            if (!GameState.TryConsumeItem(ItemIds.Remove))
            {
                OutOfStock?.Invoke(ItemIds.Remove);
                return false;
            }

            _board.ForceRemove(piece);
            Used?.Invoke(ItemIds.Remove);
            return true;
        }

        public void SetRemoveMode(bool active)
        {
            if (_removeModeActive == active) return;
            _removeModeActive = active;
            RemoveModeChanged?.Invoke(active);
        }
    }
}
