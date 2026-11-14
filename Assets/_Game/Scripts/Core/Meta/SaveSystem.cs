using System;
using System.IO;
using UnityEngine;

namespace SheepGame.Core.Meta
{
    /// <summary>
    /// Reads and writes the single save file. Nothing else in the game touches the disk.
    /// </summary>
    public static class SaveSystem
    {
        const string FileName = "save.json";
        const string TempName = "save.tmp";
        const string BackupName = "save.bak";

        static string Dir => Application.persistentDataPath;
        static string Path => System.IO.Path.Combine(Dir, FileName);
        static string TempPath => System.IO.Path.Combine(Dir, TempName);
        static string BackupPath => System.IO.Path.Combine(Dir, BackupName);

        public static bool FileExists => File.Exists(Path);

        public static SaveData Load()
        {
            var data = TryRead(Path);

            // A save killed mid-write leaves a truncated file and a good backup, which is
            // the whole point of keeping one. Losing a session's progress beats losing
            // the account.
            if (data == null) data = TryRead(BackupPath);

            if (data == null) return new SaveData();

            Migrate(data);
            return data;
        }

        public static void Save(SaveData data)
        {
            if (data == null) return;

            data.schemaVersion = SaveData.CurrentSchema;
            data.lastSavedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(TempPath, JsonUtility.ToJson(data, true));

                // Write to a temp file and swap, never straight over the live one: a kill
                // during WriteAllText (the OS taking the app down on a phone needs no
                // warning) would otherwise leave a half-written save and nothing else.
                if (File.Exists(Path))
                {
                    // Replace is atomic where the platform supports it and keeps the old
                    // file as the backup in the same step.
                    try { File.Replace(TempPath, Path, BackupPath); }
                    catch (Exception)
                    {
                        // Some Android storage drivers do not implement Replace. Fall back
                        // to a copy-then-move, which is still never a truncated target.
                        File.Copy(Path, BackupPath, true);
                        File.Delete(Path);
                        File.Move(TempPath, Path);
                    }
                }
                else
                {
                    File.Move(TempPath, Path);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Could not write the save file: {e.Message}");
            }
        }

        public static void Delete()
        {
            foreach (var p in new[] { Path, BackupPath, TempPath })
            {
                try { if (File.Exists(p)) File.Delete(p); }
                catch (Exception e) { Debug.LogWarning($"[SaveSystem] Could not delete {p}: {e.Message}"); }
            }
        }

        static SaveData TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var data = JsonUtility.FromJson<SaveData>(json);
                // FromJson returns a default-constructed object for JSON that parses but
                // holds nothing of ours, so a zero schema means the file is not a save.
                return data != null && data.schemaVersion > 0 ? data : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] {path} is unreadable and will be skipped: {e.Message}");
                return null;
            }
        }

        // Runs oldest step first and falls through, so a save can climb several versions
        // in one load.
        static void Migrate(SaveData data)
        {
            if (data.schemaVersion >= SaveData.CurrentSchema) return;

            // The ladder was redrawn: level 7 is no longer the board a version-1 save cleared as
            // level 7, so resuming there would drop the player into the middle of a curve they
            // never climbed. Wallet, boosters and purchases are untouched.
            if (data.schemaVersion < 2)
            {
                data.lastLevelPlayed = 1;
                data.highestLevelReached = 1;
                data.levels.Clear();
            }

            data.schemaVersion = SaveData.CurrentSchema;
        }
    }
}
