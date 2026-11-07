using System;
using System.Collections.Generic;
using System.IO;

namespace WallScape
{
    internal static class Scanner
    {
        private static readonly string[] Extensions =
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"
        };

        public static string[] Find(IEnumerable<string> folders)
        {
            List<string> files = new List<string>();
            if (folders != null)
            {
                foreach (string folder in folders)
                {
                    if (string.IsNullOrEmpty(folder))
                        continue;
                    if (!Directory.Exists(folder))
                        continue;
                    Walk(folder, files, 0);
                }
            }

            files.Sort(CompareLogical);
            return files.ToArray();
        }

        private static void Walk(string directory, List<string> files, int depth)
        {
            if (depth > 24)
                return;

            try
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    if (IsImage(file))
                        files.Add(file);
                }
            }
            catch
            {
            }

            try
            {
                foreach (string sub in Directory.EnumerateDirectories(directory))
                {
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(sub);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch
                    {
                        continue;
                    }
                    Walk(sub, files, depth + 1);
                }
            }
            catch
            {
            }
        }

        private static bool IsImage(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            foreach (string candidate in Extensions)
            {
                if (string.Equals(candidate, extension, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static int CompareLogical(string a, string b)
        {
            return Native.StrCmpLogicalW(a, b);
        }
    }
}
