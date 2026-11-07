using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace WallScape
{
    internal class Config
    {
        public const string AppName = "WallScape";

        public string Hotkey = "Alt+E";
        public List<string> Folders = new List<string>();
        public string Animation = "circle";
        public int DurationMs = 1200;
        public int HoldMs = 220;
        public bool AutoStart = false;
        public int GalleryWidth = 220;
        public int GalleryGap = 16;
        public int GalleryCornerRadius = 12;
        public string GalleryOrientation = "landscape";
        public string GalleryStyle = "cards";
        public int GalleryLean = 0;
        public string Accent = "auto";
        public int CacheMaxMb = 100;
        public int CacheDays = 30;
        public float Dim = 0.35f;

        public static string BaseDir
        {
            get
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(location))
                    location = AppDomain.CurrentDomain.BaseDirectory;
                return Path.GetDirectoryName(location);
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(BaseDir, "WallScape.ini"); }
        }

        public static Config Load()
        {
            Config config = new Config();
            if (!File.Exists(ConfigPath))
                return config;

            try
            {
                string section = "general";
                string[] lines = File.ReadAllLines(ConfigPath, Encoding.UTF8);
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                        continue;
                    }

                    int split = line.IndexOf('=');
                    if (split <= 0)
                        continue;
                    string key = line.Substring(0, split).Trim().ToLowerInvariant();
                    string value = line.Substring(split + 1).Trim();

                    if (section == "general")
                    {
                        if (key == "hotkey") config.Hotkey = value;
                        else if (key == "animation") config.Animation = value;
                        else if (key == "duration_ms") config.DurationMs = ParseInt(value, config.DurationMs);
                        else if (key == "hold_ms") config.HoldMs = ParseInt(value, config.HoldMs);
                        else if (key == "autostart") config.AutoStart = ParseBool(value, config.AutoStart);
                    }
                    else if (section == "folders")
                    {
                        if (value.Length == 0)
                            continue;
                        value = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                        if (Directory.Exists(value))
                            config.Folders.Add(value);
                    }
                    else if (section == "gallery")
                    {
                        if (key == "image_width") config.GalleryWidth = ParseInt(value, config.GalleryWidth);
                        else if (key == "gap") config.GalleryGap = ParseInt(value, config.GalleryGap);
                        else if (key == "corner_radius") config.GalleryCornerRadius = ParseInt(value, config.GalleryCornerRadius);
                        else if (key == "orientation") config.GalleryOrientation = value;
                        else if (key == "style") config.GalleryStyle = value;
                        else if (key == "lean") config.GalleryLean = ParseInt(value, config.GalleryLean);
                        else if (key == "accent") config.Accent = value;
                        else if (key == "dim") config.Dim = ParseFloat(value, config.Dim);
                    }
                    else if (section == "cache")
                    {
                        if (key == "max_mb") config.CacheMaxMb = ParseInt(value, config.CacheMaxMb);
                        else if (key == "days") config.CacheDays = ParseInt(value, config.CacheDays);
                    }
                }
            }
            catch
            {
            }

            config.Normalize();
            return config;
        }

        public void Normalize()
        {
            GalleryWidth = Clamp(GalleryWidth, 96, 640);
            GalleryGap = Clamp(GalleryGap, 0, 96);
            GalleryCornerRadius = Clamp(GalleryCornerRadius, 0, 50);
            DurationMs = Clamp(DurationMs, 100, 5000);
            HoldMs = Clamp(HoldMs, 0, 1500);
            CacheMaxMb = Clamp(CacheMaxMb, 10, 2000);
            CacheDays = Clamp(CacheDays, 1, 3650);
            if (Dim < 0f) Dim = 0f;
            if (Dim > 0.85f) Dim = 0.85f;

            if (string.IsNullOrEmpty(Hotkey))
                Hotkey = "Alt+E";
            if (string.IsNullOrEmpty(Animation))
                Animation = "circle";
            Animation = Animation.ToLowerInvariant();
            if (Animation != "circle" && Animation != "slide_top" && Animation != "diamond" &&
                Animation != "split" && Animation != "none")
                Animation = "circle";

            if (GalleryOrientation == null)
                GalleryOrientation = "landscape";
            GalleryOrientation = GalleryOrientation.ToLowerInvariant();
            if (GalleryOrientation != "portrait")
                GalleryOrientation = "landscape";

            if (string.IsNullOrEmpty(GalleryStyle))
                GalleryStyle = "cards";
            GalleryStyle = GalleryStyle.ToLowerInvariant();
            if (GalleryStyle != "strip" && GalleryStyle != "cards")
                GalleryStyle = "cards";
            GalleryLean = Clamp(GalleryLean, 0, 300);
        }

        public void Save()
        {
            try
            {
                Normalize();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; WallScape settings");
                sb.AppendLine("[general]");
                sb.AppendLine("hotkey=" + Hotkey);
                sb.AppendLine("animation=" + Animation);
                sb.AppendLine("duration_ms=" + DurationMs.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("hold_ms=" + HoldMs.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("autostart=" + (AutoStart ? "1" : "0"));
                sb.AppendLine();
                sb.AppendLine("[folders]");
                for (int i = 0; i < Folders.Count; i++)
                    sb.AppendLine((i + 1).ToString(CultureInfo.InvariantCulture) + "=" + Folders[i]);
                sb.AppendLine();
                sb.AppendLine("[gallery]");
                sb.AppendLine("image_width=" + GalleryWidth.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("gap=" + GalleryGap.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("corner_radius=" + GalleryCornerRadius.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("orientation=" + GalleryOrientation);
                sb.AppendLine("style=" + GalleryStyle);
                sb.AppendLine("lean=" + GalleryLean.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("accent=" + Accent);
                sb.AppendLine("dim=" + Dim.ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine();
                sb.AppendLine("[cache]");
                sb.AppendLine("max_mb=" + CacheMaxMb.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("days=" + CacheDays.ToString(CultureInfo.InvariantCulture));

                File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        public void AddFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                return;
            folder = folder.Trim().Trim('"');
            if (folder.Length == 0)
                return;
            foreach (string existing in Folders)
            {
                if (string.Equals(existing, folder, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            Folders.Add(folder);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return fallback;
        }

        private static float ParseFloat(string value, float fallback)
        {
            float parsed;
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return fallback;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (value == null)
                return fallback;
            value = value.Trim().ToLowerInvariant();
            if (value == "1" || value == "true" || value == "yes" || value == "on")
                return true;
            if (value == "0" || value == "false" || value == "no" || value == "off")
                return false;
            return fallback;
        }
    }
}
