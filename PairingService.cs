using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace WindowsNotch
{
    public static class PairingService
    {
        private static readonly object Gate = new();
        private static string? _currentPairingToken;
        private static DateTime _tokenExpiresAt;

        // Rate limiting: IP -> List of failure timestamps in the last minute
        private static readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> FailedAttempts = new();

        /// <summary>
        /// Dipicu saat pairing disetujui, agar jendela QR yang masih terbuka
        /// bisa langsung ditutup.
        /// </summary>
        public static event Action? PairingApproved;

        public static string GeneratePairingToken()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(32);
            string token = WebEncoders.Base64UrlEncode(bytes);

            lock (Gate)
            {
                _currentPairingToken = token;
                _tokenExpiresAt = DateTime.UtcNow.AddMinutes(3);
            }

            return token;
        }

        public static bool IsPairingTokenValid(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;

            lock (Gate)
            {
                if (_currentPairingToken == null || DateTime.UtcNow > _tokenExpiresAt)
                {
                    _currentPairingToken = null;
                    return false;
                }

                return string.Equals(_currentPairingToken, token, StringComparison.Ordinal);
            }
        }

        public static void InvalidatePairingToken()
        {
            lock (Gate)
            {
                _currentPairingToken = null;
            }
        }

        public static void NotifyPairingApproved()
        {
            try
            {
                PairingApproved?.Invoke();
            }
            catch
            {
                // Jangan gagalkan pairing hanya karena handler UI error
            }
        }

        public static string GenerateSessionToken()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(32);
            return WebEncoders.Base64UrlEncode(bytes);
        }

        public static string GetLocalIpAddress()
        {
            try
            {
                // Prioritaskan adapter aktif yang merupakan Ethernet atau Wireless80211 dan Up
                foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (netInterface.OperationalStatus == OperationalStatus.Up &&
                        (netInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                         netInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                    {
                        var ipProperties = netInterface.GetIPProperties();
                        foreach (var unicast in ipProperties.UnicastAddresses)
                        {
                            if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                                !IPAddress.IsLoopback(unicast.Address))
                            {
                                return unicast.Address.ToString();
                            }
                        }
                    }
                }
            }
            catch { }

            return "127.0.0.1";
        }

        public static string BuildQrPayload(string host, int port, string token, string pcId, string pcName)
        {
            string encodedName = Uri.EscapeDataString(pcName);
            return $"notchi://pair?host={host}&port={port}&token={token}&pc={pcId}&name={encodedName}&v=1";
        }

        public static bool CheckRateLimit(string clientIp)
        {
            if (string.IsNullOrEmpty(clientIp)) return true;

            var queue = FailedAttempts.GetOrAdd(clientIp, _ => new ConcurrentQueue<DateTime>());
            var now = DateTime.UtcNow;

            // Bersihkan yang lebih dari 1 menit
            while (queue.TryPeek(out var timestamp) && (now - timestamp).TotalSeconds > 60)
            {
                queue.TryDequeue(out _);
            }

            return queue.Count < 5;
        }

        public static void RecordFailedAttempt(string clientIp)
        {
            if (string.IsNullOrEmpty(clientIp)) return;
            var queue = FailedAttempts.GetOrAdd(clientIp, _ => new ConcurrentQueue<DateTime>());
            queue.Enqueue(DateTime.UtcNow);
        }
    }
}
