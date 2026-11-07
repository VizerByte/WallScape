using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace WallScape
{
    internal static class ThumbnailCache
    {
        private static readonly object SaveLock = new object();

        public static string DirectoryPath
        {
            get { return Path.Combine(Config.BaseDir, "cache", "thumbs"); }
        }

        public static Bitmap Get(string imagePath, int width, int height)
        {
            if (string.IsNullOrEmpty(imagePath) || width < 1 || height < 1)
                return null;

            try
            {
                Directory.CreateDirectory(DirectoryPath);

                string key = Key(imagePath, width, height);
                string cacheFile = Path.Combine(DirectoryPath, key + ".jpg");

                if (File.Exists(cacheFile))
                {
                    Bitmap cached = ImageUtil.LoadBitmap(cacheFile);
                    if (cached != null)
                        return cached;
                }

                Bitmap source = ImageUtil.LoadBitmap(imagePath);
                if (source == null)
                    return null;

                Bitmap thumb;
                try
                {
                    thumb = ImageUtil.CreateThumbnail(source, width, height);
                }
                finally
                {
                    source.Dispose();
                }

                Save(cacheFile, thumb);
                return thumb;
            }
            catch
            {
                return null;
            }
        }

        private static void Save(string cacheFile, Bitmap thumb)
        {
            lock (SaveLock)
            {
                string temp = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    ImageUtil.SaveJpeg(thumb, temp, 88);
                    if (!File.Exists(cacheFile))
                        File.Move(temp, cacheFile);
                    else
                        File.Delete(temp);
                }
                catch
                {
                    try
                    {
                        if (File.Exists(temp))
                            File.Delete(temp);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static string Key(string imagePath, int width, int height)
        {
            long ticks = 0;
            try
            {
                ticks = File.GetLastWriteTimeUtc(imagePath).Ticks;
            }
            catch
            {
            }

            string material = imagePath.ToLowerInvariant() + "|" + ticks.ToString() + "|" + width + "x" + height;
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < material.Length; i++)
            {
                hash ^= material[i];
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }

        public static void Prune(int maxMegabytes, int maxDays)
        {
            try
            {
                if (!Directory.Exists(DirectoryPath))
                    return;

                long maxBytes = Math.Max(1, maxMegabytes) * 1024L * 1024L;
                DateTime cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, maxDays));

                FileInfo[] files = new DirectoryInfo(DirectoryPath).GetFiles("*.jpg");
                List<FileInfo> alive = new List<FileInfo>();
                long total = 0;

                foreach (FileInfo file in files)
                {
                    try
                    {
                        if (file.LastWriteTimeUtc < cutoff)
                        {
                            file.Delete();
                            continue;
                        }
                        total += file.Length;
                        alive.Add(file);
                    }
                    catch
                    {
                    }
                }

                if (total <= maxBytes)
                    return;

                alive.Sort(delegate(FileInfo a, FileInfo b)
                {
                    return a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc);
                });

                foreach (FileInfo file in alive)
                {
                    if (total <= maxBytes)
                        break;
                    try
                    {
                        total -= file.Length;
                        file.Delete();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }
    }
}
