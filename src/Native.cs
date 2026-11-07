using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WallScape
{
    internal static class Native
    {
        // ---- window messages / styles -------------------------------------
        public const int WM_HOTKEY = 0x0312;
        public const uint WM_SPAWN_WORKER = 0x052C;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_MOUSEHWHEEL = 0x020E;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const int WS_CHILD = 0x40000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int SW_SHOW = 5;
        public const int SW_SHOWNOACTIVATE = 4;

        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public const uint SMTO_NORMAL = 0x0000;

        public const int MONITORINFOF_PRIMARY = 1;
        public const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

        public const uint SPI_SETDESKWALLPAPER = 0x0014;
        public const uint SPIF_UPDATEINIFILE = 0x01;
        public const uint SPIF_SENDCHANGE = 0x02;

        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        public const uint ULW_ALPHA = 0x00000002;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;

        public const int HWND_BROADCAST = 0xFFFF;

        // ---- structs -------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;

            public SIZE(int w, int h)
            {
                cx = w;
                cy = h;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        // ---- delegates -----------------------------------------------------
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        // ---- user32 --------------------------------------------------------
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE info, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint param, string value, uint winIni);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int StrCmpLogicalW(string a, string b);

        // ---- gdi32 ---------------------------------------------------------
        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool DeleteObject(IntPtr obj);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr dstDc, ref POINT dstPos, ref SIZE size,
            IntPtr srcDc, ref POINT srcPos, uint colorKey, ref BLENDFUNCTION blend, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors; // one-palette slot; unused for 32bpp BI_RGB
        }

        /// <summary>
        /// Creates a bottom-up-free (top-down) 32bpp DIB section so GDI+ and
        /// UpdateLayeredWindow can share one buffer with no copy per frame.
        /// </summary>
        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits,
            IntPtr hSection, uint offset);

        // ---- helpers -------------------------------------------------------

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int index)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, index);
            return new IntPtr(GetWindowLong32(hWnd, index));
        }

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, index, value);
            return new IntPtr(SetWindowLong32(hWnd, index, value.ToInt32()));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);

        public static List<RECT> GetMonitors()
        {
            List<RECT> monitors = new List<RECT>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data)
            {
                monitors.Add(rect);
                return true;
            }, IntPtr.Zero);
            return monitors;
        }

        public static RECT GetPrimaryMonitor()
        {
            RECT found = new RECT();
            bool got = false;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data)
            {
                MONITORINFOEX info = new MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(monitor, ref info) && (info.dwFlags & MONITORINFOF_PRIMARY) != 0)
                {
                    found = info.rcMonitor;
                    got = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            if (!got)
            {
                int w = GetSystemMetrics(SM_CXSCREEN);
                int h = GetSystemMetrics(SM_CYSCREEN);
                found.Left = 0;
                found.Top = 0;
                found.Right = w;
                found.Bottom = h;
            }
            return found;
        }

        public static string GetPrimaryDeviceName()
        {
            string device = null;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data)
            {
                MONITORINFOEX info = new MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(monitor, ref info) && (info.dwFlags & MONITORINFOF_PRIMARY) != 0)
                {
                    device = info.szDevice;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return device;
        }

        public static string GetPrimaryMonitorInterfacePath()
        {
            string device = GetPrimaryDeviceName();
            if (device == null)
                return null;

            for (uint i = 0; i < 4; i++)
            {
                DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (!EnumDisplayDevices(device, i, ref dd, EDD_GET_DEVICE_INTERFACE_NAME))
                    break;
                if (!string.IsNullOrEmpty(dd.DeviceID))
                    return dd.DeviceID;
            }
            return null;
        }

        /// <summary>
        /// Win32 normally refuses SetForegroundWindow for a process that was not
        /// started by the user (for example one woken by a global hotkey).
        /// Briefly attaching to the foreground thread lifts that restriction.
        /// </summary>
        public static void ForceForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return;

            uint processId;
            uint foregroundThread = 0;
            IntPtr foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero)
                foregroundThread = GetWindowThreadProcessId(foreground, out processId);

            uint thisThread = GetCurrentThreadId();
            bool attached = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != thisThread)
                    attached = AttachThreadInput(foregroundThread, thisThread, true);

                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(foregroundThread, thisThread, false);
            }
        }

        public static IntPtr FindWorkerW()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                IntPtr unused;
                SendMessageTimeout(progman, WM_SPAWN_WORKER, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out unused);
            }

            IntPtr worker = IntPtr.Zero;
            if (progman != IntPtr.Zero)
            {
                EnumChildWindows(progman, delegate(IntPtr hWnd, IntPtr data)
                {
                    StringBuilder name = new StringBuilder(256);
                    GetClassName(hWnd, name, name.Capacity);
                    if (name.ToString() == "WorkerW")
                    {
                        worker = hWnd;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            if (worker == IntPtr.Zero)
            {
                EnumWindows(delegate(IntPtr hWnd, IntPtr data)
                {
                    if (FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    {
                        IntPtr candidate = FindWindowEx(IntPtr.Zero, hWnd, "WorkerW", null);
                        if (candidate != IntPtr.Zero)
                        {
                            worker = candidate;
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }

            // A hidden WorkerW would hide the overlay too (YASB does the same).
            if (worker != IntPtr.Zero)
                ShowWindow(worker, SW_SHOW);

            return worker;
        }

        public static string ReadDesktopValue(string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (key == null)
                        return null;
                    object value = key.GetValue(name);
                    if (value == null)
                        return null;
                    return value.ToString();
                }
            }
            catch
            {
                return null;
            }
        }

        public static int ReadDesktopInt(string name, int fallback)
        {
            string raw = ReadDesktopValue(name);
            if (raw == null)
                return fallback;
            int parsed;
            if (int.TryParse(raw, out parsed))
                return parsed;
            return fallback;
        }

        public static System.Drawing.Color GetDesktopBackgroundColor()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors"))
                {
                    if (key != null)
                    {
                        string raw = key.GetValue("Background") as string;
                        if (!string.IsNullOrEmpty(raw))
                        {
                            string[] parts = raw.Split(' ');
                            if (parts.Length >= 3)
                            {
                                int r, g, b;
                                if (int.TryParse(parts[0], out r) && int.TryParse(parts[1], out g) &&
                                    int.TryParse(parts[2], out b))
                                    return System.Drawing.Color.FromArgb(
                                        Math.Max(0, Math.Min(255, r)),
                                        Math.Max(0, Math.Min(255, g)),
                                        Math.Max(0, Math.Min(255, b)));
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return System.Drawing.Color.Black;
        }

        public static string GetTranscodedWallpaperPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return System.IO.Path.Combine(appData, @"Microsoft\Windows\Themes\TranscodedWallpaper");
        }
    }
}
