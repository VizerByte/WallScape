using System;
using System.IO;
using System.Runtime.InteropServices;

namespace WallScape
{
    internal static class WallpaperApi
    {
        [ComImport]
        [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId,
                [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        }

        [ComImport]
        [Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
        private class DesktopWallpaperClass
        {
        }

        public static bool SetWallpaper(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string full = Path.GetFullPath(path);
            string monitor = Native.GetPrimaryMonitorInterfacePath();

            try
            {
                IDesktopWallpaper desktop = (IDesktopWallpaper)new DesktopWallpaperClass();
                try
                {
                    desktop.SetWallpaper(monitor, full);
                }
                finally
                {
                    if (Marshal.IsComObject(desktop))
                        Marshal.FinalReleaseComObject(desktop);
                }
                return true;
            }
            catch
            {
                try
                {
                    return Native.SystemParametersInfo(Native.SPI_SETDESKWALLPAPER, 0, full,
                        Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static string GetCurrentWallpaper()
        {
            string monitor = Native.GetPrimaryMonitorInterfacePath();

            try
            {
                IDesktopWallpaper desktop = (IDesktopWallpaper)new DesktopWallpaperClass();
                try
                {
                    string path = desktop.GetWallpaper(monitor);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        return path;
                }
                finally
                {
                    if (Marshal.IsComObject(desktop))
                        Marshal.FinalReleaseComObject(desktop);
                }
            }
            catch
            {
            }

            string transcoded = Native.GetTranscodedWallpaperPath();
            if (File.Exists(transcoded))
                return transcoded;

            return null;
        }

        public static string GetCurrentWallpaperOrTranscoded()
        {
            string current = GetCurrentWallpaper();
            if (current != null)
                return current;

            string transcoded = Native.GetTranscodedWallpaperPath();
            if (File.Exists(transcoded))
                return transcoded;
            return null;
        }
    }
}
