using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace WallScape
{
    internal static class ImageUtil
    {
        public static Bitmap LoadBitmap(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                byte[] data = File.ReadAllBytes(path);
                using (MemoryStream stream = new MemoryStream(data))
                {
                    using (Image image = Image.FromStream(stream, false, true))
                    {
                        int orientation = ReadOrientation(image);
                        Bitmap copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
                        using (Graphics g = Graphics.FromImage(copy))
                        {
                            g.CompositingMode = CompositingMode.SourceCopy;
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
                        }
                        ApplyOrientation(copy, orientation);
                        return copy;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static int ReadOrientation(Image image)
        {
            try
            {
                const int orientationId = 0x0112;
                if (Array.IndexOf(image.PropertyIdList, orientationId) >= 0)
                {
                    PropertyItem item = image.GetPropertyItem(orientationId);
                    if (item != null && item.Value != null && item.Value.Length > 0)
                        return item.Value[0];
                }
            }
            catch
            {
            }
            return 1;
        }

        private static void ApplyOrientation(Bitmap bitmap, int orientation)
        {
            switch (orientation)
            {
                case 2: bitmap.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
                case 3: bitmap.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                case 4: bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY); break;
                case 5: bitmap.RotateFlip(RotateFlipType.Rotate90FlipX); break;
                case 6: bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                case 7: bitmap.RotateFlip(RotateFlipType.Rotate270FlipX); break;
                case 8: bitmap.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
            }
        }

        public static Bitmap CreateThumbnail(Bitmap source, int width, int height)
        {
            if (width < 1) width = 1;
            if (height < 1) height = 1;

            Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            float scale = Math.Max(width / (float)source.Width, height / (float)source.Height);
            int w = (int)(source.Width * scale + 0.5f);
            int h = (int)(source.Height * scale + 0.5f);
            int x = (width - w) / 2;
            int y = (height - h) / 2;

            using (Graphics g = Graphics.FromImage(output))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(source, new Rectangle(x, y, w, h), 0, 0, source.Width, source.Height,
                        GraphicsUnit.Pixel, attributes);
                }
            }
            return output;
        }

        public static void SaveJpeg(Bitmap bitmap, string path, long quality)
        {
            ImageCodecInfo codec = GetEncoder(ImageFormat.Jpeg);
            if (codec == null)
            {
                bitmap.Save(path, ImageFormat.Jpeg);
                return;
            }

            using (EncoderParameters parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                bitmap.Save(path, codec, parameters);
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                    return codec;
            }
            return null;
        }
    }
}
