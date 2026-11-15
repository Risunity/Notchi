using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace WindowsNotch.Rewards
{
    public sealed record RedeemResult(
        bool Success,
        string? ErrorCode,
        string? Message,
        EntitlementPayload? Payload);

    public sealed record ReleaseResult(
        bool Success,
        string? Message);

    public static class RedeemService
    {
        public const string DefaultBaseUrl = "https://notchi-web-tau.vercel.app";

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private sealed class RedeemApiResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("entitlement")]
            public string? Entitlement { get; set; }

            [JsonPropertyName("claimNumber")]
            public int ClaimNumber { get; set; }

            [JsonPropertyName("rarity")]
            public string? Rarity { get; set; }

            [JsonPropertyName("bgId")]
            public string? BgId { get; set; }

            [JsonPropertyName("badgeId")]
            public string? BadgeId { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("message")]
            public string? Message { get; set; }
        }

        private sealed class AssetsApiResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("bgId")]
            public string? BgId { get; set; }

            [JsonPropertyName("rarity")]
            public string? Rarity { get; set; }

            [JsonPropertyName("expand")]
            public string? Expand { get; set; }

            [JsonPropertyName("compact")]
            public string? Compact { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("message")]
            public string? Message { get; set; }
        }

        private sealed class ReleaseApiResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("released")]
            public bool Released { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("message")]
            public string? Message { get; set; }
        }

        public static async Task<RedeemResult> RedeemCodeAsync(string rawCode, string baseUrl = DefaultBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(rawCode))
            {
                return new RedeemResult(false, "empty_code", "Kode redeem tidak boleh kosong.", null);
            }

            string code = rawCode.Trim().ToUpperInvariant();
            string deviceHash = DeviceId.GetDeviceHash();

            try
            {
                var body = new { code, deviceHash };
                string jsonBody = JsonSerializer.Serialize(body);
                using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                string url = $"{baseUrl.TrimEnd('/')}/api/redeem";
                using var response = await Http.PostAsync(url, content);
                string responseText = await response.Content.ReadAsStringAsync();

                var result = JsonSerializer.Deserialize<RedeemApiResponse>(responseText, JsonOpts);

                if (result == null)
                {
                    return new RedeemResult(false, "invalid_response", "Respon server tidak valid.", null);
                }

                if (!response.IsSuccessStatusCode || !result.Success)
                {
                    string msg = result.Message ?? GetLocalizedErrorMessage(result.Error);
                    return new RedeemResult(false, result.Error ?? "unknown_error", msg, null);
                }

                if (string.IsNullOrWhiteSpace(result.Entitlement))
                {
                    return new RedeemResult(false, "missing_entitlement", "Server tidak mengirimkan entitlement.", null);
                }

                // Verify cryptographic signature locally
                var verifiedPayload = RewardCrypto.VerifyAndParseToken(result.Entitlement);
                if (verifiedPayload == null)
                {
                    return new RedeemResult(false, "invalid_signature", "Tanda tangan entitlement digital tidak valid.", null);
                }

                // Save entitlement to disk
                EntitlementStore.Save(result.Entitlement, verifiedPayload);

                // Fetch asset download URLs
                await FetchAndCacheAssetsAsync(result.Entitlement, baseUrl);

                return new RedeemResult(true, null, "Redeem berhasil!", verifiedPayload);
            }
            catch (HttpRequestException ex)
            {
                CrashLogger.Log("RedeemService.RedeemCodeAsync", ex);
                return new RedeemResult(false, "network_error", "Gagal terhubung ke server. Periksa koneksi internet Anda.", null);
            }
            catch (Exception ex)
            {
                CrashLogger.Log("RedeemService.RedeemCodeAsync", ex);
                return new RedeemResult(false, "internal_error", $"Terjadi kesalahan: {ex.Message}", null);
            }
        }

        public static async Task<bool> FetchAndCacheAssetsAsync(string entitlementToken, string baseUrl = DefaultBaseUrl)
        {
            try
            {
                string url = $"{baseUrl.TrimEnd('/')}/api/assets";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", entitlementToken);

                using var response = await Http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return false;

                string text = await response.Content.ReadAsStringAsync();
                var assetResp = JsonSerializer.Deserialize<AssetsApiResponse>(text, JsonOpts);
                if (assetResp == null || !assetResp.Success || string.IsNullOrWhiteSpace(assetResp.BgId))
                {
                    return false;
                }

                return await RewardAssetCache.EnsureAssetsDownloadedAsync(assetResp.BgId, assetResp.Expand, assetResp.Compact);
            }
            catch (Exception ex)
            {
                CrashLogger.Log("RedeemService.FetchAndCacheAssetsAsync", ex);
                return false;
            }
        }

        public static async Task<ReleaseResult> ReleaseDeviceAsync(string code, string baseUrl = DefaultBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return new ReleaseResult(false, "Kode tidak tersedia.");
            }

            string deviceHash = DeviceId.GetDeviceHash();

            try
            {
                var body = new { code = code.Trim().ToUpperInvariant(), deviceHash };
                string jsonBody = JsonSerializer.Serialize(body);
                using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                string url = $"{baseUrl.TrimEnd('/')}/api/release-device";
                using var response = await Http.PostAsync(url, content);
                string responseText = await response.Content.ReadAsStringAsync();

                var result = JsonSerializer.Deserialize<ReleaseApiResponse>(responseText, JsonOpts);

                // Clear locally regardless to unlink
                EntitlementStore.Clear();

                if (result != null && result.Success)
                {
                    return new ReleaseResult(true, "Perangkat berhasil dilepas.");
                }

                return new ReleaseResult(true, "Perangkat dilepas secara lokal.");
            }
            catch (Exception ex)
            {
                CrashLogger.Log("RedeemService.ReleaseDeviceAsync", ex);
                EntitlementStore.Clear();
                return new ReleaseResult(true, "Perangkat dilepas secara lokal.");
            }
        }

        private static string GetLocalizedErrorMessage(string? errorCode)
        {
            return errorCode switch
            {
                "code_not_found" => "Kode klaim tidak ditemukan atau sudah kadaluarsa.",
                "device_limit" => "Batas maksimal 3 perangkat telah tercapai untuk kode ini. Lepas salah satu perangkat lama terlebih dahulu.",
                "rate_limited" => "Terlalu banyak percobaan. Silakan coba lagi beberapa saat lagi.",
                "invalid_code_format" => "Format kode klaim salah. Contoh: NOTCHI-XXXX-XXXX",
                "invalid_request" => "Permintaan klaim tidak lengkap.",
                _ => "Gagal memproses kode klaim."
            };
        }
    }
}
