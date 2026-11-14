using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SheepGame.Core
{
    /// <summary>
    /// Chooses a conservative mobile render profile from the physical RAM reported by Android.
    /// The active URP asset is cloned before it is changed, so Play Mode and project assets are
    /// never dirtied by a runtime decision.
    /// </summary>
    public static class AdaptiveDeviceQuality
    {
        public enum Tier
        {
            Low,
            Medium,
            High
        }

        readonly struct Profile
        {
            public readonly int FrameRate;
            public readonly float RenderScale;
            public readonly int Msaa;
            public readonly bool Hdr;
            public readonly float ShadowDistance;
            public readonly bool PostProcessing;
            public readonly int MipmapLimit;
            public readonly float LodBias;

            public Profile(int frameRate, float renderScale, int msaa, bool hdr,
                float shadowDistance, bool postProcessing, int mipmapLimit, float lodBias)
            {
                FrameRate = frameRate;
                RenderScale = renderScale;
                Msaa = msaa;
                Hdr = hdr;
                ShadowDistance = shadowDistance;
                PostProcessing = postProcessing;
                MipmapLimit = mipmapLimit;
                LodBias = lodBias;
            }
        }

        // Android normally reports slightly less memory than the amount printed on the box.
        // 4 GB devices therefore sit safely inside Low, 6 GB inside Medium, and 8 GB+ in High.
        const int LowMemoryCeilingMb = 4800;
        const int MediumMemoryCeilingMb = 7500;

        static readonly Profile LowProfile = new Profile(
            30, 0.68f, 1, false, 25f, false, 1, 0.75f);
        static readonly Profile MediumProfile = new Profile(
            60, 0.78f, 2, true, 45f, true, 0, 0.9f);
        static readonly Profile HighProfile = new Profile(
            60, 0.9f, 4, true, 70f, true, 0, 1f);

        static UniversalRenderPipelineAsset _runtimePipeline;
        static bool _installed;

        public static Tier CurrentTier { get; private set; } = Tier.Medium;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_installed) return;
            _installed = true;

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            CurrentTier = ChooseTier(SystemInfo.systemMemorySize);
            Apply(CurrentTier, "startup");
#endif
        }

        static Tier ChooseTier(int memoryMb)
        {
            if (memoryMb <= 0) return Tier.Medium;
            if (memoryMb <= LowMemoryCeilingMb) return Tier.Low;
            if (memoryMb <= MediumMemoryCeilingMb) return Tier.Medium;
            return Tier.High;
        }

        static Profile GetProfile(Tier tier)
        {
            switch (tier)
            {
                case Tier.Low: return LowProfile;
                case Tier.High: return HighProfile;
                default: return MediumProfile;
            }
        }

        static void Apply(Tier tier, string reason)
        {
            Profile profile = GetProfile(tier);
            DeviceRuntime.SetPreferredFrameRate(profile.FrameRate);

            QualitySettings.globalTextureMipmapLimit = profile.MipmapLimit;
            QualitySettings.lodBias = profile.LodBias;
            QualitySettings.pixelLightCount = tier == Tier.Low ? 1 : 2;
            QualitySettings.realtimeReflectionProbes = tier == Tier.High;
            QualitySettings.anisotropicFiltering = tier == Tier.Low
                ? AnisotropicFiltering.Disable
                : AnisotropicFiltering.Enable;
            QualitySettings.shadows = tier == Tier.Low
                ? UnityEngine.ShadowQuality.HardOnly
                : UnityEngine.ShadowQuality.All;
            QualitySettings.shadowDistance = profile.ShadowDistance;

            EnsureRuntimePipeline();
            if (_runtimePipeline != null)
            {
                _runtimePipeline.renderScale = profile.RenderScale;
                _runtimePipeline.msaaSampleCount = profile.Msaa;
                _runtimePipeline.supportsHDR = profile.Hdr;
                _runtimePipeline.shadowDistance = profile.ShadowDistance;
            }

            ApplySceneObjects(profile, tier);

            Debug.Log($"[AdaptiveQuality] {tier} ({reason}): RAM={SystemInfo.systemMemorySize} MB, " +
                      $"GPU='{SystemInfo.graphicsDeviceName}', scale={profile.RenderScale:0.00}, " +
                      $"MSAA={profile.Msaa}x, HDR={profile.Hdr}, post={profile.PostProcessing}, " +
                      $"shadows={profile.ShadowDistance:0}, FPS={profile.FrameRate}.");
        }

        static void EnsureRuntimePipeline()
        {
            if (_runtimePipeline != null) return;

            var source = UniversalRenderPipeline.asset;
            if (source == null)
            {
                Debug.LogWarning("[AdaptiveQuality] URP is not active; only generic quality settings were applied.");
                return;
            }

            _runtimePipeline = Object.Instantiate(source);
            _runtimePipeline.name = source.name + " (Runtime Adaptive)";
            _runtimePipeline.hideFlags = HideFlags.HideAndDontSave;
            QualitySettings.renderPipeline = _runtimePipeline;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplySceneObjects(GetProfile(CurrentTier), CurrentTier);
        }

        static void ApplySceneObjects(Profile profile, Tier tier)
        {
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            foreach (var camera in cameras)
            {
                camera.allowHDR = profile.Hdr;
                if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                    data.renderPostProcessing = profile.PostProcessing;
            }

            if (tier == Tier.High) return;

            // Soft shadows are disproportionately expensive on older Adreno GPUs. Keep the
            // shadow shape on Low/Medium, but use a single hard sample.
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
            foreach (var light in lights)
                if (light.shadows == LightShadows.Soft) light.shadows = LightShadows.Hard;
        }

        /// <summary>Called by DeviceRuntime when Android signals real memory pressure.</summary>
        public static void HandleLowMemory()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Tier lower = CurrentTier == Tier.High ? Tier.Medium : Tier.Low;
            if (lower == CurrentTier) return;
            CurrentTier = lower;
            Apply(CurrentTier, "low-memory signal");
#endif
        }
    }
}
