using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Several of the SFX mp3s ship with real, audible leading silence baked into the file --
    // up to 245ms on landOnConveer -- an export/encoder artifact, not a Unity playback delay
    // (confirmed by reading the decoded PCM back: DecompressOnLoad was already on for all of
    // them). PlayOneShot has no start-offset, so the fix is a trimmed copy: read the clip's
    // own decoded samples, cut everything before the first one past a small threshold (minus
    // a short preroll so the attack transient is not clipped), write the rest out as a WAV.
    // Source mp3s are left untouched -- re-run this after replacing any of them.
    public static class AudioTrimBuilder
    {
        const string SourceDir = "Assets/_Game/Audio";
        const string TrimmedDir = "Assets/_Game/Audio/Trimmed";
        const float SilenceThreshold = 0.02f;
        const int PrerollSamples = 120; // ~2.5ms at 48kHz -- keeps the attack from clicking

        static readonly string[] Names =
        {
            "click_1", "click2", "jumpOnConveer", "landOnConveer",
            "success", "fail_1", "close", "swoosh", "tickking"
        };

        [MenuItem("SheepGame/Trim SFX Leading Silence")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(TrimmedDir))
                AssetDatabase.CreateFolder(SourceDir, "Trimmed");

            foreach (var name in Names)
            {
                string srcPath = $"{SourceDir}/{name}.mp3";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(srcPath);
                if (clip == null) { Debug.LogWarning($"Trim: no clip at {srcPath}"); continue; }

                clip.LoadAudioData();
                int channels = clip.channels;
                var data = new float[clip.samples * channels];
                clip.GetData(data, 0);

                int firstFrame = -1;
                for (int i = 0; i < data.Length; i += channels)
                {
                    bool loud = false;
                    for (int c = 0; c < channels; c++)
                        if (Mathf.Abs(data[i + c]) > SilenceThreshold) { loud = true; break; }
                    if (loud) { firstFrame = i / channels; break; }
                }

                int startFrame = firstFrame > 0 ? Mathf.Max(0, firstFrame - PrerollSamples) : 0;
                int keepFrames = clip.samples - startFrame;
                if (keepFrames <= 0) { Debug.LogWarning($"Trim: {name} is silent throughout, skipped."); continue; }

                var trimmed = new float[keepFrames * channels];
                System.Array.Copy(data, startFrame * channels, trimmed, 0, trimmed.Length);

                string outPath = $"{TrimmedDir}/{name}.wav";
                WriteWav(outPath, trimmed, channels, clip.frequency);
                AssetDatabase.ImportAsset(outPath);

                float ms = startFrame / (float)clip.frequency * 1000f;
                Debug.Log($"{name}: trimmed {startFrame} frames ({ms:F0}ms) -> {outPath}");
            }

            AssetDatabase.Refresh();
        }

        static void WriteWav(string assetPath, float[] samples, int channels, int sampleRate)
        {
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            using var stream = new FileStream(fullPath, FileMode.Create);
            using var writer = new BinaryWriter(stream);

            int byteRate = sampleRate * channels * 2;
            int dataSize = samples.Length * 2;

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);              // PCM
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);

            foreach (var s in samples)
            {
                short v = (short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767);
                writer.Write(v);
            }
        }
    }
}
