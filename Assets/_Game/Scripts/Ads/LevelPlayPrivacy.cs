using System;
using UnityEngine;
#if SHEEP_LEVELPLAY
using Unity.Services.LevelPlay;
#endif

namespace SheepGame.Ads
{
    /// <summary>Persists the player's ad-data choice and applies it before LevelPlay starts.</summary>
    public static class LevelPlayPrivacy
    {
        const string ChoicePreference = "BaggageJam.Privacy.PersonalizedAds";

        public static event Action ChoiceChanged;

        public static bool HasChoice => PlayerPrefs.HasKey(ChoicePreference);
        public static bool Personalized => PlayerPrefs.GetInt(ChoicePreference, 0) != 0;

        public static void SetChoice(bool personalized)
        {
            PlayerPrefs.SetInt(ChoicePreference, personalized ? 1 : 0);
            PlayerPrefs.Save();
            Apply(personalized);
            ChoiceChanged?.Invoke();
        }

        public static void ApplyStoredChoice()
        {
            if (HasChoice) Apply(Personalized);
        }

        static void Apply(bool personalized)
        {
#if SHEEP_LEVELPLAY
            LevelPlayPrivacySettings.SetGDPRConsent(personalized);
            LevelPlayPrivacySettings.SetCCPA(!personalized);
            LevelPlayPrivacySettings.SetCOPPA(false);
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ChoiceChanged = null;
    }
}
