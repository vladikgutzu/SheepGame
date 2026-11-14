using SheepGame.Core.Meta;
using TMPro;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// Writes the coin balance into the text it sits on, and keeps it right.
    /// </summary>
    // A component rather than a field on GameManager because the balance is shown in
    // three places at once — the HUD pill, the shop header, the lose panel — and a single
    // reference could only ever feed one of them. Each label minds itself, so adding a
    // fourth is a component, not a wiring change.
    [RequireComponent(typeof(TMP_Text))]
    public class UIWalletLabel : MonoBehaviour
    {
        [Tooltip("{0} is the coin balance.")]
        public string format = "{0}";

        TMP_Text _text;

        void Awake() => _text = GetComponent<TMP_Text>();

        void OnEnable()
        {
            GameState.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => GameState.Changed -= Refresh;

        void Refresh()
        {
            if (_text == null) _text = GetComponent<TMP_Text>();
            if (_text != null) _text.text = string.Format(format, Compact(GameState.Coins));
        }

        // The coin pill is a fixed width with a coin on one side and a plus on the other, so
        // the number between them has about four characters' room. The shop sells 5,000 at a
        // time, so five and six digit balances are routine and would otherwise run into the
        // plus — TMP is set to overflow, not shrink, so it would not stop on its own.
        static string Compact(int amount)
        {
            if (amount >= 1_000_000) return (amount / 1_000_000f).ToString("0.#") + "M";
            if (amount >= 10_000) return (amount / 1_000f).ToString("0.#") + "k";
            return amount.ToString();
        }
    }
}
