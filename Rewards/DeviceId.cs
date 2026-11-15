using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace WindowsNotch.Rewards
{
    public static class DeviceId
    {
        private const string AppDeviceSalt = "notchi-device-v1";

        public static string GetDeviceHash()
        {
            string machineGuid = GetMachineGuid();
            string combined = $"{machineGuid}:{AppDeviceSalt}";

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string GetMachineGuid()
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

                using var subKey = key.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                var value = subKey?.GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            catch
            {
                // Fallback to machine name
            }

            return Environment.MachineName.ToLowerInvariant();
        }
    }
}
