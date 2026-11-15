using System;
using System.IO;
using System.Runtime.InteropServices;

namespace WindowsNotch
{
    public static class KnownFolders
    {
        private static readonly Guid DownloadsFolderId =
            new("374DE290-123F-4565-9164-39C4925E467B");

        public static string GetDownloadsDirectory()
        {
            IntPtr pathPtr = IntPtr.Zero;

            try
            {
                var folderId = DownloadsFolderId;
                if (SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out pathPtr) == 0 &&
                    pathPtr != IntPtr.Zero)
                {
                    string? path = Marshal.PtrToStringUni(pathPtr);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (pathPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pathPtr);
                }
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(
            ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
    }
}
