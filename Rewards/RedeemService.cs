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

        public static string BaseUrl
        {
            get
            {
                string? envUrl = Environment.GetEnvironmentVariable("NOTCHI_API_BASE_URL");
                if (!string.IsNullOrWhiteSpace(envUrl)) return envUrl.TrimEnd('/');
                return DefaultBaseUrl;
            }
        }

        public static async Task<string> ResolveBaseUrlAsync(string? explicitBaseUrl = null)
        {
            if (!string.IsNullOrWhiteSpace(explicitBaseUrl)) return explicitBaseUrl.TrimEnd('/');

            string? envUrl = Environment.GetEnvironmentVariable("NOTCHI_API_BASE_URL");
            if (!string.IsNullOrWhiteSpace(envUrl)) return envUrl.TrimEnd('/');

            // Otomatis deteksi server lokal Astro jika sedang berjalan di localhost:4321
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var checkResp = await Http.GetAsync("http://localhost:4321/favicon.ico", cts.Token);
                if (checkResp.IsSuccessStatusCode || (int)checkResp.StatusCode < 500)
                {
                    return "http://localhost:4321";
                }
            }
            catch
            {
                // Server lokal tidak aktif atau timeout, gunakan URL production
            }

            return DefaultBaseUrl;
        }

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

        public static async Task<RedeemResult> RedeemCodeAsync(string rawCode, string? baseUrl = null)
        {
            if (string.IsNullOrWhiteSpace(rawCode))
            {
                return new RedeemResult(false, "empty_code", "Kode redeem tidak boleh kosong.", null);
            }

            string targetBaseUrl = await ResolveBaseUrlAsync(baseUrl);
            string code = rawCode.Trim().ToUpperInvariant();
            string deviceHash = DeviceId.GetDeviceHash();

            try
            {
                var body = new { code, deviceHash };
                string jsonBody = JsonSerializer.Serialize(body);
                using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                string url = $"{targetBaseUrl}/api/redeem";
                using var response = await Http.PostAsync(url, content);
                string responseText = await response.Content.ReadAsStringAsync();

                RedeemApiResponse? result = null;
                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    try
                    {
                        result = JsonSerializer.Deserialize<RedeemApiResponse>(responseText, JsonOpts);
                    }
                    catch (JsonException)
                    {
                        // Respon bukan format JSON yang valid (misal teks/HTML error Vercel)
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    string msg = result?.Message
                        ?? GetLocalizedErrorMessage(result?.Error)
                        ?? $"Server mengembalikan status error {(int)response.StatusCode}. Silakan coba lagi nanti.";
                    return new RedeemResult(false, result?.Error ?? "server_error", msg, null);
                }

                if (result == null)
                {
                    return new RedeemResult(false, "invalid_response", "Respon server tidak valid.", null);
                }

                if (!result.Success)
                {
                    string msg = result.Message ?? GetLocalizedErrorMessage(result.Error) ?? "Gagal memproses kode klaim.";
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
                await FetchAndCacheAssetsAsync(result.Entitlement, targetBaseUrl);

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

        public static async Task<bool> FetchAndCacheAssetsAsync(string entitlementToken, string? baseUrl = null)
        {
            try
            {
                string targetBaseUrl = await ResolveBaseUrlAsync(baseUrl);
                string url = $"{targetBaseUrl}/api/assets";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", entitlementToken);

                using var response = await Http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return false;

                string text = await response.Content.ReadAsStringAsync();
                AssetsApiResponse? assetResp = null;
                try
                {
                    assetResp = JsonSerializer.Deserialize<AssetsApiResponse>(text, JsonOpts);
                }
                catch (JsonException)
                {
                    return false;
                }

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

        public static async Task<ReleaseResult> ReleaseDeviceAsync(string code, string? baseUrl = null)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return new ReleaseResult(false, "Kode tidak tersedia.");
            }

            string targetBaseUrl = await ResolveBaseUrlAsync(baseUrl);
            string deviceHash = DeviceId.GetDeviceHash();

            try
            {
                var body = new { code = code.Trim().ToUpperInvariant(), deviceHash };
                string jsonBody = JsonSerializer.Serialize(body);
                using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                string url = $"{targetBaseUrl}/api/release-device";
                using var response = await Http.PostAsync(url, content);
                string responseText = await response.Content.ReadAsStringAsync();

                ReleaseApiResponse? result = null;
                try
                {
                    result = JsonSerializer.Deserialize<ReleaseApiResponse>(responseText, JsonOpts);
                }
                catch (JsonException)
                {
                    // Ignore JSON deserialization error during release
                }

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

        private static string? GetLocalizedErrorMessage(string? errorCode)
        {
            if (string.IsNullOrWhiteSpace(errorCode)) return null;

            return errorCode switch
            {
                "code_not_found" => "Kode klaim tidak ditemukan atau sudah kadaluarsa.",
                "device_limit" => "Batas maksimal 3 perangkat telah tercapai untuk kode ini. Lepas salah satu perangkat lama terlebih dahulu.",
                "rate_limited" => "Terlalu banyak percobaan. Silakan coba lagi beberapa saat lagi.",
                "invalid_code_format" => "Format kode klaim salah. Contoh: NOTCHI-XXXX-XXXX",
                "invalid_request" => "Permintaan klaim tidak lengkap.",
                _ => null
            };
        }
    }
}
