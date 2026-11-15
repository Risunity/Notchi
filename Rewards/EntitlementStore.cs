using System;
using System.IO;
using System.Text.Json;

namespace WindowsNotch.Rewards
{
    public sealed class StoredEntitlement
    {
        public string Token { get; set; } = string.Empty;
        public EntitlementPayload? Payload { get; set; }
    }

    public static class EntitlementStore
    {
        private static readonly object FileLock = new();

        public static string RewardsDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Notchi",
            "rewards");

        public static string EntitlementPath { get; } = Path.Combine(RewardsDirectory, "entitlement.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static StoredEntitlement? Load()
        {
            lock (FileLock)
            {
                try
                {
                    if (!File.Exists(EntitlementPath)) return null;

                    string json = File.ReadAllText(EntitlementPath);
                    var stored = JsonSerializer.Deserialize<StoredEntitlement>(json, JsonOptions);
                    if (stored == null || string.IsNullOrWhiteSpace(stored.Token)) return null;

                    // Verify locally
                    var verifiedPayload = RewardCrypto.VerifyAndParseToken(stored.Token);
                    if (verifiedPayload == null)
                    {
                        // Invalid token on disk, clean it up
                        Clear();
                        return null;
                    }

                    stored.Payload = verifiedPayload;
                    return stored;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static void Save(string token, EntitlementPayload payload)
        {
            lock (FileLock)
            {
                try
                {
                    Directory.CreateDirectory(RewardsDirectory);
                    var stored = new StoredEntitlement
                    {
                        Token = token,
                        Payload = payload
                    };

                    string json = JsonSerializer.Serialize(stored, JsonOptions);
                    string tmp = EntitlementPath + ".tmp";
                    File.WriteAllText(tmp, json);
                    File.Move(tmp, EntitlementPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    CrashLogger.Log("EntitlementStore.Save", ex);
                }
            }
        }

        public static void Clear()
        {
            lock (FileLock)
            {
                try
                {
                    if (File.Exists(EntitlementPath))
                    {
                        File.Delete(EntitlementPath);
                    }
                }
                catch { }
            }
        }
    }
}
