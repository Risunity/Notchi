using System;
using System.IO;

namespace WindowsNotch.Conversion
{
    public static class ConversionPaths
    {
        public static bool TryCreateOutputPath(
            string inputPath,
            ConversionTarget target,
            string? destinationDirectory,
            out string outputPath)
        {
            outputPath = string.Empty;

            try
            {
                if (!File.Exists(inputPath)) return false;

                string directory = string.IsNullOrWhiteSpace(destinationDirectory)
                    ? Path.GetDirectoryName(inputPath) ?? string.Empty
                    : destinationDirectory;
                string baseName = Path.GetFileNameWithoutExtension(inputPath);
                string extension = ConversionFormats.GetExtension(target);

                string candidate = Path.Combine(directory, $"{baseName}{extension}");
                int index = 2;

                while (File.Exists(candidate))
                {
                    candidate = Path.Combine(
                        directory,
                        $"{baseName} ({index}){extension}");
                    index++;
                }

                outputPath = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string CreateTempOutputPath(string outputPath, ConversionTarget target)
        {
            string extension = ConversionFormats.GetExtension(target);
            return $"{outputPath}.{Guid.NewGuid():N}.part{extension}";
        }

        public static string GetDownloadsDirectory()
        {
            return KnownFolders.GetDownloadsDirectory();
        }
    }
}
