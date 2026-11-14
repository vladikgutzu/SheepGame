using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using SheepGame.Core;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace SheepGame.EditorTools
{
    /// <summary>
    /// Keeps the Android test workflow separate from the Play upload workflow. The test APK
    /// must be quick to install on a phone; a Play bundle must never be made without the
    /// credentials and commerce integration a real release needs.
    /// </summary>
    public static class AndroidBuildTools
    {
        const string PackageId = "com.spikygames.baggagejam";
        const string OutputFolder = "Builds/Android";
        const string AppIconPath = "Assets/_Game/Branding/AppIcon_Android.png";
        // Deliberately outside the Unity project: these files contain the Play upload key and
        // its password and must remain local to the machine that signs Play uploads.
        const string LocalSigningFolder = "F:/Desktop/Android/BaggageJam/Signing";
        const string LocalSigningConfigPath = LocalSigningFolder + "/credentials.local.json";
        const string LocalKeystorePath = LocalSigningFolder + "/BaggageJam-upload-key.keystore";
        const string UploadAlias = "baggagejam_upload";
        internal const string ExpectedUploadKeySha1 = "91:A6:8D:8B:F6:AA:5A:6A:E9:EB:A2:F6:D9:D3:1B:2D:29:07:8F:C1";

        [Serializable]
        sealed class LocalSigningConfig
        {
            public string keystorePath;
            public string keystorePassword;
            public string keyAlias;
            public string keyAliasPassword;
        }

        [MenuItem("SheepGame/Android/Apply Android App Icon")]
        public static void ApplyAndroidAppIcon()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconPath);
            if (icon == null) throw new BuildFailedException("Android app icon is missing: " + AppIconPath);

            var target = NamedBuildTarget.Android;
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(target))
            {
                var slots = PlayerSettings.GetPlatformIcons(target, kind);
                foreach (var slot in slots)
                {
                    // Unity owns the resolution variants. Supplying the same safe-area source
                    // to each required layer produces a consistent legacy and adaptive icon.
                    var layers = Enumerable.Repeat(icon, slot.maxLayerCount).ToArray();
                    slot.SetTextures(layers);
                }
                PlayerSettings.SetPlatformIcons(target, kind, slots);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Android] Assigned AppIcon_Android to all Android launcher-icon slots.");
        }

        [MenuItem("SheepGame/Android/Configure Android Test Build")]
        public static void ConfigureTestBuild()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            // API 36 meets Play's current submission requirement; min API 25 keeps the
            // existing device support while giving current Android behaviour on test phones.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.optimizedFramePacing = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.stripEngineCode = true;
            EditorUserBuildSettings.buildAppBundle = false;

            AssetDatabase.SaveAssets();
            Debug.Log("[Android] Test build configured: portrait, IL2CPP, ARM64, min API 25, target API 36. " +
                      "Use SheepGame/Android/Build Test APK after switching to Android.");
        }

        [MenuItem("SheepGame/Android/Build Test APK")]
        public static void BuildTestApk()
        {
            ConfigureTestBuild();
            SwitchToAndroid();
            EditorUserBuildSettings.buildAppBundle = false;
            Build("BaggageJam-test.apk", BuildOptions.Development | BuildOptions.AllowDebugging);
        }

        [MenuItem("SheepGame/Android/Build LevelPlay Diagnostic APK")]
        public static void BuildLevelPlayDiagnosticApk()
        {
            ConfigureTestBuild();
            SwitchToAndroid();
            EditorUserBuildSettings.buildAppBundle = false;

            var ads = AssetDatabase.LoadAssetAtPath<AdsConfig>("Assets/_Game/Data/AdsConfig.asset");
            if (ads == null) throw new BuildFailedException("AdsConfig asset is missing.");
            bool previous = ads.launchTestSuite;
            try
            {
                ads.launchTestSuite = true;
                EditorUtility.SetDirty(ads);
                AssetDatabase.SaveAssets();
                Build("BaggageJam-levelplay-diagnostic.apk",
                      BuildOptions.Development | BuildOptions.AllowDebugging);
            }
            finally
            {
                // A player build can trigger an asset-database reload and invalidate the
                // ScriptableObject reference captured above. Reload it by path before restoring
                // the diagnostic-only flag so it can never leak into the next Play build.
                ads = AssetDatabase.LoadAssetAtPath<AdsConfig>("Assets/_Game/Data/AdsConfig.asset");
                if (ads == null)
                    throw new BuildFailedException("AdsConfig disappeared while restoring the diagnostic flag.");
                ads.launchTestSuite = previous;
                EditorUtility.SetDirty(ads);
                AssetDatabase.SaveAssets();
            }
        }

        [MenuItem("SheepGame/Android/Disable LevelPlay Diagnostic Mode")]
        public static void DisableLevelPlayDiagnosticMode()
        {
            var ads = AssetDatabase.LoadAssetAtPath<AdsConfig>("Assets/_Game/Data/AdsConfig.asset");
            if (ads == null) throw new BuildFailedException("AdsConfig asset is missing.");
            ads.launchTestSuite = false;
            EditorUtility.SetDirty(ads);
            AssetDatabase.SaveAssets();
            Debug.Log("[Android] LevelPlay diagnostic mode is disabled for normal builds.");
        }

        [MenuItem("SheepGame/Android/Validate Play Release")]
        public static void ValidatePlayRelease()
        {
            ConfigureTestBuild();
            string problems = ReleaseProblems();
            if (string.IsNullOrEmpty(problems))
                Debug.Log("[Android] Play release validation passed. You can create the signed AAB.");
            else
                Debug.LogWarning("[Android] Play release is not ready:\n" + problems);
        }

        [MenuItem("SheepGame/Android/Build Signed AAB for Play")]
        public static void BuildPlayAab()
        {
            ConfigureTestBuild();
            string problems = ReleaseProblems();
            if (!string.IsNullOrEmpty(problems))
                throw new BuildFailedException("Play release is blocked:\n" + problems);

            SwitchToAndroid();
            EditorUserBuildSettings.buildAppBundle = true;
            string version = PlayerSettings.bundleVersion.Replace(' ', '_');
            Build($"BaggageJam-{version}-{PlayerSettings.Android.bundleVersionCode}.aab", BuildOptions.None);
        }

        [MenuItem("SheepGame/Android/Create Local Play Upload Key")]
        public static void CreateLocalPlayUploadKey()
        {
            if (File.Exists(ProjectPath(LocalKeystorePath)) || File.Exists(ProjectPath(LocalSigningConfigPath)))
                throw new BuildFailedException("A local upload key already exists. It will not be overwritten. " +
                                               "Back up the external Signing folder before changing signing credentials.");

            byte[] secretBytes = new byte[18];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(secretBytes);
            // Hex keeps the password safe to pass to keytool without shell escaping.
            string password = "Bj" + BitConverter.ToString(secretBytes).Replace("-", string.Empty).ToLowerInvariant();
            var config = new LocalSigningConfig
            {
                keystorePath = LocalKeystorePath,
                keystorePassword = password,
                keyAlias = UploadAlias,
                keyAliasPassword = password
            };

            string keyPath = ProjectPath(LocalKeystorePath);
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath));
            string keytool = Path.Combine(UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath, "bin", "keytool.exe");
            if (!File.Exists(keytool)) keytool = "keytool";

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = keytool,
                Arguments = $"-genkeypair -v -keystore \"{keyPath}\" -alias {UploadAlias} " +
                            $"-keyalg RSA -keysize 2048 -validity 10000 -storepass {password} -keypass {password} " +
                            "-dname \"CN=Spiky Games, OU=Mobile, O=Spiky Games, L=Chisinau, ST=Chisinau, C=MD\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 || !File.Exists(keyPath))
                throw new BuildFailedException("Could not create the Play upload key:\n" + output + "\n" + error);

            string configPath = ProjectPath(LocalSigningConfigPath);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            File.WriteAllText(configPath, JsonUtility.ToJson(config, true));
            ApplyLocalSigning(config);
            Debug.Log("[Android] Created and configured a local Play upload key. Back up the external Signing folder securely; " +
                      "it is required for every future update uploaded to Google Play.");
        }

        [MenuItem("SheepGame/Android/Apply Local Play Upload Key")]
        public static void ApplyLocalPlayUploadKey()
        {
            ApplyAndValidateLocalSigning();
            AssetDatabase.SaveAssets();
            Debug.Log("[Android] Local Play upload key and passwords were applied to Player Settings for this Editor session.");
        }

        [MenuItem("SheepGame/Android/Build Signed AAB for Internal Test")]
        public static void BuildInternalTestAab()
        {
            ConfigureTestBuild();
            ApplyAndValidateLocalSigning();
            SwitchToAndroid();
            EditorUserBuildSettings.buildAppBundle = true;
            string version = PlayerSettings.bundleVersion.Replace(' ', '_');
            Build($"BaggageJam-internal-{version}-{PlayerSettings.Android.bundleVersionCode}.aab", BuildOptions.None);
            Debug.LogWarning("[Android] Internal-test AAB created. It is signed and suitable for the Play internal track, " +
                             "but the current local shop and unconfigured Android ads are not production-ready.");
        }

        [MenuItem("SheepGame/Android/Increment Android Version Code")]
        public static void IncrementAndroidVersionCode()
        {
            PlayerSettings.Android.bundleVersionCode++;
            AssetDatabase.SaveAssets();
            Debug.Log($"[Android] Version code increased to {PlayerSettings.Android.bundleVersionCode}.");
        }

        static string ReleaseProblems()
        {
            var problems = new System.Collections.Generic.List<string>();
            if (PlayerSettings.Android.targetSdkVersion != AndroidSdkVersions.AndroidApiLevel36)
                problems.Add("Target API must be Android 16 / API 36.");
            if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0)
                problems.Add("ARM64 must be enabled.");
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
                problems.Add("Android scripting backend must be IL2CPP.");
            if (!PlayerSettings.Android.useCustomKeystore || string.IsNullOrWhiteSpace(PlayerSettings.Android.keystoreName) ||
                string.IsNullOrWhiteSpace(PlayerSettings.Android.keyaliasName))
                problems.Add("Configure the Play upload keystore and key alias in Player Settings before a release build.");
            if (!UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Any(p => p.name == "com.unity.purchasing"))
                problems.Add("Unity IAP is not installed. The current shop is a local test store and cannot be shipped.");
            var ads = AssetDatabase.LoadAssetAtPath<AdsConfig>("Assets/_Game/Data/AdsConfig.asset");
            if (ads == null)
                problems.Add("AdsConfig asset is missing.");
            else if (string.IsNullOrWhiteSpace(ads.androidAppKey) ||
                     string.IsNullOrWhiteSpace(ads.androidInterstitialId) ||
                     string.IsNullOrWhiteSpace(ads.androidHintRewardedId) ||
                     string.IsNullOrWhiteSpace(ads.androidShuffleRewardedId) ||
                     string.IsNullOrWhiteSpace(ads.androidRemoveRewardedId))
                problems.Add("Enter the Android LevelPlay App Key and all Interstitial/Rewarded Ad Unit IDs before release.");
            return string.Join("\n• ", problems.Prepend(string.Empty));
        }

        static void SwitchToAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) return;
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Could not switch Unity to the Android build target.");
        }

        static LocalSigningConfig LoadLocalSigningConfig()
        {
            string configPath = ProjectPath(LocalSigningConfigPath);
            if (!File.Exists(configPath))
                throw new BuildFailedException("No local upload key found. Run SheepGame/Android/Create Local Play Upload Key first.");
            var config = JsonUtility.FromJson<LocalSigningConfig>(File.ReadAllText(configPath));
            if (config == null || string.IsNullOrWhiteSpace(config.keystorePath) ||
                string.IsNullOrWhiteSpace(config.keystorePassword) || string.IsNullOrWhiteSpace(config.keyAlias))
                throw new BuildFailedException("The local Android signing configuration is incomplete: " + LocalSigningConfigPath);

            // Keep the credentials file portable when the whole Signing folder is moved. The
            // sibling keystore takes precedence over a stale path stored inside the JSON file.
            string siblingKeystore = Path.Combine(Path.GetDirectoryName(configPath), Path.GetFileName(LocalKeystorePath));
            if (File.Exists(siblingKeystore))
                config.keystorePath = siblingKeystore;
            if (!File.Exists(ProjectPath(config.keystorePath)))
                throw new BuildFailedException("The local upload key is missing: " + config.keystorePath);
            return config;
        }

        static void ApplyLocalSigning(LocalSigningConfig config)
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = ProjectPath(config.keystorePath);
            PlayerSettings.Android.keystorePass = config.keystorePassword;
            PlayerSettings.Android.keyaliasName = config.keyAlias;
            PlayerSettings.Android.keyaliasPass = config.keyAliasPassword;
        }

        internal static void ApplyAndValidateLocalSigning()
        {
            var config = LoadLocalSigningConfig();
            ApplyLocalSigning(config);

            string keyPath = ProjectPath(config.keystorePath);
            string keytool = Path.Combine(UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath, "bin", "keytool.exe");
            if (!File.Exists(keytool)) keytool = "keytool";

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = keytool,
                Arguments = $"-list -v -keystore \"{keyPath}\" -storepass {config.keystorePassword} -alias {config.keyAlias}",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new BuildFailedException("Could not validate the Android upload key:\n" + error);
            if (output.IndexOf(ExpectedUploadKeySha1, StringComparison.OrdinalIgnoreCase) < 0)
                throw new BuildFailedException("Android App Bundle signing was blocked: the configured upload key does not match Google Play. " +
                                               "Expected SHA1: " + ExpectedUploadKeySha1);
        }

        static string ProjectPath(string relativePath) =>
            Path.Combine(Directory.GetCurrentDirectory(), relativePath).Replace('\\', '/');

        static void Build(string fileName, BuildOptions options)
        {
            string output = Path.Combine(Directory.GetCurrentDirectory(), OutputFolder);
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(output, fileName),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = options
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Android build failed. See the Unity Console for details.");
            Debug.Log($"[Android] Built {report.summary.outputPath} ({report.summary.totalSize / (1024f * 1024f):0.0} MB).");
        }
    }

    /// <summary>
    /// Prevents an AAB made from Unity's regular Build window from being signed with a debug,
    /// temporary, or otherwise incorrect key. APK development builds remain unaffected.
    /// </summary>
    sealed class AndroidAabSigningPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android || !EditorUserBuildSettings.buildAppBundle)
                return;

            AndroidBuildTools.ApplyAndValidateLocalSigning();
            Debug.Log("[Android] Verified Play upload certificate before AAB build. SHA1: " +
                      AndroidBuildTools.ExpectedUploadKeySha1);
        }
    }
}
