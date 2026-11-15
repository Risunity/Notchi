using System;
using System.Windows.Media;

namespace WindowsNotch
{
    internal sealed record SourceAppInfo(
        string Name,
        Color Color,
        string? LogoKey,
        string GlowImage);

    internal static class SourceApps
    {
        public static SourceAppInfo? Find(string? appId)
        {
            string id = (appId ?? string.Empty).ToLowerInvariant();

            if (id.Contains("spotify"))
            {
                return new SourceAppInfo(
                    "Spotify",
                    Color.FromRgb(0x1D, 0xB9, 0x54),
                    "SpotifyLogoImage",
                    "layer%20blur%20green.png");
            }

            if (id.Contains("chrome"))
            {
                return new SourceAppInfo(
                    "Chrome",
                    Color.FromRgb(0xEA, 0x43, 0x35),
                    "ChromeLogoImage",
                    "layer%20blur%20red.png");
            }

            if (id.Contains("youtube"))
            {
                return new SourceAppInfo(
                    "YouTube",
                    Color.FromRgb(0xFF, 0x00, 0x00),
                    "YouTubeLogoImage",
                    "layer%20blur%20blue.png");
            }

            if (id.Contains("msedge") || id.Contains("edge"))
            {
                return new SourceAppInfo(
                    "Edge",
                    Color.FromRgb(0x00, 0x78, 0xD4),
                    null,
                    "layer%20blur%20blue.png");
            }

            if (id.Contains("firefox"))
            {
                return new SourceAppInfo(
                    "Firefox",
                    Color.FromRgb(0xFF, 0x71, 0x39),
                    null,
                    "layer%20blur%20blue.png");
            }

            if (id.Contains("foobar"))
            {
                return new SourceAppInfo(
                    "foobar2000",
                    Color.FromRgb(0xC8, 0xC8, 0xD0),
                    null,
                    "layer%20blur%20blue.png");
            }

            return null;
        }
    }
}
