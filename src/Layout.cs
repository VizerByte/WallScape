using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WallScape
{
    internal enum WallpaperFit
    {
        Fill,
        Fit,
        Stretch,
        Center,
        Tile,
        Span
    }

    internal static class Layout
    {
        public static WallpaperFit ReadFitMode()
        {
            string tile = Native.ReadDesktopValue("TileWallpaper");
            if (tile != null && tile.Trim() == "1")
                return WallpaperFit.Tile;

            int style = Native.ReadDesktopInt("WallpaperStyle", 10);
            switch (style)
            {
                case 0: return WallpaperFit.Center;
                case 2: return WallpaperFit.Stretch;
                case 6: return WallpaperFit.Fit;
                case 10: return WallpaperFit.Fill;
                case 22: return WallpaperFit.Span;
                default: return WallpaperFit.Fill;
            }
        }

        public static Size TranscodeSize(int width, int height)
        {
            int maxVirtual = Native.ReadDesktopInt("MaxVirtualDesktopDimension", 0);
            int maxMonitor = Native.ReadDesktopInt("MaxMonitorDimension", 0);
            if (maxVirtual <= 0 || maxMonitor <= 0 || width <= 0 || height <= 0)
                return new Size(width, height);

            double aspect = (double)width / height;
            int limit = (int)Math.Max(maxMonitor * aspect, maxMonitor / aspect);
            limit = Math.Max(maxVirtual, limit);

            if (width <= limit && height <= limit)
                return new Size(width, height);

            // Truncate, never round: Windows does the same here, and a tile
            // grid would drift by a pixel on every repeat otherwise.
            int w = Math.Max(1, Math.Min(limit, (int)(limit / (double)height * width)));
            int h = Math.Max(1, Math.Min(limit, (int)(limit / (double)width * height)));
            return new Size(w, h);
        }

        /// <summary>
        /// Renders <paramref name="source"/> the way Windows will paint it on
        /// <paramref name="target"/> (one monitor), in physical pixels.
        /// </summary>
        public static Bitmap Render(Bitmap source, Rectangle target, WallpaperFit fit,
            List<Rectangle> monitors, Color background)
        {
            Size capped = TranscodeSize(source.Width, source.Height);
            Bitmap prepared = source;
            if (capped.Width != source.Width || capped.Height != source.Height)
            {
                prepared = Resize(source, capped.Width, capped.Height);
            }

            try
            {
                WallpaperFit effective = fit;
                if ((fit == WallpaperFit.Fill || fit == WallpaperFit.Fit) &&
                    WantsAutospan(prepared, monitors))
                {
                    effective = WallpaperFit.Span;
                }

                if (effective == WallpaperFit.Span)
                    return RenderSpan(prepared, target, monitors, background, fit != WallpaperFit.Fit);
                if (effective == WallpaperFit.Tile)
                    return RenderTile(prepared, target, monitors, background);
                return RenderSingle(prepared, target, effective, background);
            }
            finally
            {
                if (!object.ReferenceEquals(prepared, source))
                    prepared.Dispose();
            }
        }

        private static Bitmap RenderSingle(Bitmap source, Rectangle target, WallpaperFit fit, Color background)
        {
            Bitmap output = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(output))
            {
                g.Clear(background);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.None;

                Bitmap scaled;
                int ox;
                int oy;

                if (fit == WallpaperFit.Stretch)
                {
                    scaled = Resize(source, target.Width, target.Height);
                    ox = 0;
                    oy = 0;
                }
                else if (fit == WallpaperFit.Center)
                {
                    scaled = source;
                    ox = (target.Width - source.Width) / 2;
                    oy = (target.Height - source.Height) / 2;
                }
                else
                {
                    bool cover = fit == WallpaperFit.Fill;
                    scaled = CoverOrContain(source, target.Width, target.Height, cover);
                    ox = (target.Width - scaled.Width) / 2;
                    oy = (target.Height - scaled.Height) / (cover ? 3 : 2);
                }

                g.DrawImageUnscaled(scaled, ox, oy);
                if (!object.ReferenceEquals(scaled, source))
                    scaled.Dispose();
            }
            return output;
        }

        private static Bitmap RenderSpan(Bitmap source, Rectangle target, List<Rectangle> monitors, Color background, bool cover)
        {
            Rectangle virtualRect = Union(monitors);
            int vw = Math.Max(1, virtualRect.Width);
            int vh = Math.Max(1, virtualRect.Height);

            Bitmap scaled = CoverOrContain(source, vw, vh, cover);
            int x = virtualRect.Left + (vw - scaled.Width) / 2;
            int y = virtualRect.Top + (vh - scaled.Height) / (cover ? 3 : 2);

            Bitmap output = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(output))
            {
                g.Clear(background);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImageUnscaled(scaled, x - target.Left, y - target.Top);
            }
            scaled.Dispose();
            return output;
        }

        private static Bitmap RenderTile(Bitmap source, Rectangle target, List<Rectangle> monitors, Color background)
        {
            Rectangle virtualRect = Union(monitors);
            int vw = Math.Max(1, virtualRect.Width);
            int vh = Math.Max(1, virtualRect.Height);
            if (source.Width <= 0 || source.Height <= 0)
                return RenderSingle(source, target, WallpaperFit.Center, background);

            Bitmap output = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(output))
            {
                g.Clear(background);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                int startX = virtualRect.Left - target.Left;
                int startY = virtualRect.Top - target.Top;
                for (int ty = 0; ty < vh; ty += source.Height)
                {
                    for (int tx = 0; tx < vw; tx += source.Width)
                    {
                        g.DrawImageUnscaled(source, startX + tx, startY + ty);
                    }
                }
            }
            return output;
        }

        private static bool WantsAutospan(Bitmap image, List<Rectangle> monitors)
        {
            if (monitors == null || monitors.Count < 2)
                return false;
            if (image.Width <= 0 || image.Height <= 0 || image.Height > image.Width)
                return false;
            if (!MonitorsTileRectangle(monitors))
                return false;

            int raw = Native.ReadDesktopInt("PanoramaThreshold", 0);
            float threshold = raw > 0 ? raw / 1000f : 2.22f;
            return image.Width / (float)image.Height > threshold;
        }

        private static bool MonitorsTileRectangle(List<Rectangle> monitors)
        {
            if (monitors == null || monitors.Count == 0)
                return false;

            int w = monitors[0].Width;
            int h = monitors[0].Height;
            int left = monitors[0].Left;
            int top = monitors[0].Top;
            int right = monitors[0].Right;
            int bottom = monitors[0].Bottom;
            long area = 0;

            foreach (Rectangle r in monitors)
            {
                if (r.Width != w || r.Height != h)
                    return false;
                left = Math.Min(left, r.Left);
                top = Math.Min(top, r.Top);
                right = Math.Max(right, r.Right);
                bottom = Math.Max(bottom, r.Bottom);
                area += (long)r.Width * r.Height;
            }

            return area == (long)(right - left) * (bottom - top);
        }

        private static Rectangle Union(List<Rectangle> monitors)
        {
            if (monitors == null || monitors.Count == 0)
                return Rectangle.Empty;

            int left = monitors[0].Left;
            int top = monitors[0].Top;
            int right = monitors[0].Right;
            int bottom = monitors[0].Bottom;
            for (int i = 1; i < monitors.Count; i++)
            {
                left = Math.Min(left, monitors[i].Left);
                top = Math.Min(top, monitors[i].Top);
                right = Math.Max(right, monitors[i].Right);
                bottom = Math.Max(bottom, monitors[i].Bottom);
            }
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        public static Bitmap CoverOrContain(Bitmap source, int width, int height, bool cover)
        {
            float sx = width / (float)source.Width;
            float sy = height / (float)source.Height;
            float scale = cover ? Math.Max(sx, sy) : Math.Min(sx, sy);
            int w = (int)(source.Width * scale + 0.5f);
            int h = (int)(source.Height * scale + 0.5f);
            if (w < 1) w = 1;
            if (h < 1) h = 1;
            return Resize(source, w, h);
        }

        public static Bitmap Resize(Bitmap source, int width, int height)
        {
            if (width < 1) width = 1;
            if (height < 1) height = 1;
            Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(output))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height,
                        GraphicsUnit.Pixel, attributes);
                }
            }
            return output;
        }
    }
}
