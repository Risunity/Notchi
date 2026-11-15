using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace WindowsNotch.Rewards
{
    public static class RewardAssetCache
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static string GetExpandPath(string bgId)
        {
            return Path.Combine(EntitlementStore.RewardsDirectory, $"{bgId}_expand.webp");
        }

        public static string GetCompactPath(string bgId)
        {
            return Path.Combine(EntitlementStore.RewardsDirectory, $"{bgId}_compact.webp");
        }

        public static bool HasAssets(string bgId)
        {
            if (string.IsNullOrWhiteSpace(bgId)) return false;
            return File.Exists(GetExpandPath(bgId)) && File.Exists(GetCompactPath(bgId));
        }

        public static async Task<bool> EnsureAssetsDownloadedAsync(string bgId, string? expandUrl, string? compactUrl)
        {
            if (string.IsNullOrWhiteSpace(bgId)) return false;

            Directory.CreateDirectory(EntitlementStore.RewardsDirectory);

            string expandPath = GetExpandPath(bgId);
            string compactPath = GetCompactPath(bgId);

            bool ok = true;

            if (!File.Exists(expandPath) && !string.IsNullOrWhiteSpace(expandUrl))
            {
                ok &= await DownloadFileAsync(expandUrl, expandPath);
            }

            if (!File.Exists(compactPath) && !string.IsNullOrWhiteSpace(compactUrl))
            {
                ok &= await DownloadFileAsync(compactUrl, compactPath);
            }

            return ok && HasAssets(bgId);
        }

        private static async Task<bool> DownloadFileAsync(string url, string destinationPath)
        {
            try
            {
                byte[] data = await Http.GetByteArrayAsync(url);
                string tmp = destinationPath + ".tmp";
                await File.WriteAllBytesAsync(tmp, data);
                File.Move(tmp, destinationPath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                CrashLogger.Log("RewardAssetCache.DownloadFileAsync", ex);
                return false;
            }
        }

        public static BitmapImage? LoadBitmap(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                CrashLogger.Log("RewardAssetCache.LoadBitmap", ex);
                return null;
            }
        }

        public static void ClearAssets(string bgId)
        {
            try
            {
                string expand = GetExpandPath(bgId);
                string compact = GetCompactPath(bgId);
                if (File.Exists(expand)) File.Delete(expand);
                if (File.Exists(compact)) File.Delete(compact);
            }
            catch { }
        }
    }
}
