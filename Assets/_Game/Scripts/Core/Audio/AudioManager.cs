using System.Collections;
using SheepGame.Core.Meta;
using UnityEngine;

namespace SheepGame.Audio
{
    /// <summary>
    /// One object for every clip in the game: two music beds, an ambience loop for the belt,
    /// and the one-shot cues for taps, pieces, panels and rounds. Everything else calls the
    /// static methods; nothing else touches an AudioSource.
    /// </summary>
    // A single SFX source rather than a pool: PlayOneShot layers a clip onto whatever the
    // source is already playing instead of cutting it off, and nothing here fires more than a
    // couple of times a second, so a pool would be spare capacity for a problem this game
    // does not have.
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Music")]
        public AudioClip menuMusic;
        public AudioClip gameplayMusic;
        [Range(0f, 1f)] public float musicVolume = 0.5f;
        public float musicFadeTime = 0.4f;

        [Header("Ambience")]
        [Tooltip("Loops behind a round. The belt scrolls on unscaled time from the moment " +
                 "the scene loads, so this starts once and is never stopped.")]
        public AudioClip conveyorLoop;
        [Range(0f, 1f)] public float ambienceVolume = 0.35f;

        [Header("SFX")]
        public AudioClip tap;
        public AudioClip blockedTap;
        public AudioClip pieceLeave;
        [Tooltip("Plays right on top of the tap click, a beat later, so it has to sit under it.")]
        [Range(0f, 1f)] public float pieceLeaveVolumeScale = 0.5f;
        public AudioClip pieceLand;
        public AudioClip win;
        public AudioClip lose;
        public AudioClip panelClose;
        public AudioClip modeSwoosh;
        public AudioClip timerTick;
        [Range(0f, 1f)] public float sfxVolume = 0.8f;

        AudioSource _music;
        AudioSource _ambience;
        AudioSource _sfx;
        Coroutine _musicFade;
        AudioClip _currentMusic;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // No DontDestroyOnLoad: Boot loads this scene once and nothing reloads it, so
            // there is no second load for the object to need surviving.

            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;

            _ambience = gameObject.AddComponent<AudioSource>();
            _ambience.loop = true;
            _ambience.playOnAwake = false;

            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
        }

        void OnEnable()
        {
            GameState.Changed += ApplyToggles;
            ApplyToggles();
        }

        void OnDisable() => GameState.Changed -= ApplyToggles;

        // The music and SFX toggles can change under a live game (once a settings screen
        // exists), so every source re-reads them rather than baking a value in at Play time.
        void ApplyToggles()
        {
            if (_musicFade != null)
            {
                StopCoroutine(_musicFade);
                _musicFade = null;
            }

            _music.mute = !GameState.MusicOn;
            _music.volume = musicVolume;
            _ambience.mute = !GameState.SoundOn;
            _ambience.volume = ambienceVolume;
            _sfx.mute = !GameState.SoundOn;
        }

        // ---- music -----------------------------------------------------------------

        public static void PlayMenuMusic() => Instance?.SwitchMusic(Instance.menuMusic);
        public static void PlayGameplayMusic() => Instance?.SwitchMusic(Instance.gameplayMusic);

        void SwitchMusic(AudioClip clip)
        {
            if (clip == null || clip == _currentMusic) return;
            _currentMusic = clip;
            if (_musicFade != null) StopCoroutine(_musicFade);
            _musicFade = StartCoroutine(CrossfadeMusic(clip));
        }

        // Dip to silence, swap the clip, rise back — a hard cut between the menu bed and the
        // gameplay bed reads as a glitch; a fade reads as a scene change.
        IEnumerator CrossfadeMusic(AudioClip clip)
        {
            float target = GameState.MusicOn ? musicVolume : 0f;
            float half = Mathf.Max(0.01f, musicFadeTime * 0.5f);

            float from = _music.volume;
            for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
            {
                _music.volume = Mathf.Lerp(from, 0f, t / half);
                yield return null;
            }

            _music.clip = clip;
            _music.volume = 0f;
            _music.Play();

            for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
            {
                _music.volume = Mathf.Lerp(0f, target, t / half);
                yield return null;
            }

            _music.volume = target;
            _musicFade = null;
        }

        // ---- ambience ----------------------------------------------------------------

        public static void PlayConveyor()
        {
            var fx = Instance;
            if (fx == null || fx.conveyorLoop == null || fx._ambience.isPlaying) return;
            fx._ambience.clip = fx.conveyorLoop;
            fx._ambience.volume = fx.ambienceVolume;
            fx._ambience.mute = !GameState.SoundOn;
            fx._ambience.Play();
        }

        // ---- one-shots -----------------------------------------------------------------

        public static void Tap() => Play(Instance, Instance != null ? Instance.tap : null);
        public static void BlockedTap() => Play(Instance, Instance != null ? Instance.blockedTap : null);

        // Plays at the jump onto the belt (see Board.HopOntoBelt), a beat after the tap
        // click, and quieter than the rest of the SFX -- it is layered right on top of that
        // click rather than replacing it, so it has to sit under it, not compete with it.
        public static void PieceLeave() =>
            Play(Instance, Instance != null ? Instance.pieceLeave : null,
                 Instance != null ? Instance.pieceLeaveVolumeScale : 1f);

        public static void PieceLand() => Play(Instance, Instance != null ? Instance.pieceLand : null);
        public static void Win() => Play(Instance, Instance != null ? Instance.win : null);
        public static void Lose() => Play(Instance, Instance != null ? Instance.lose : null);
        public static void PanelClose() => Play(Instance, Instance != null ? Instance.panelClose : null);
        public static void ModeSwoosh() => Play(Instance, Instance != null ? Instance.modeSwoosh : null);
        public static void TimerTick() => Play(Instance, Instance != null ? Instance.timerTick : null);

        static void Play(AudioManager fx, AudioClip clip, float volumeScale = 1f)
        {
            if (fx == null || clip == null || !GameState.SoundOn) return;
            fx._sfx.PlayOneShot(clip, fx.sfxVolume * volumeScale);
        }
    }
}
