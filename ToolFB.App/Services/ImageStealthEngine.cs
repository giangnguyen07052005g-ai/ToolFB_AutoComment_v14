using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace ToolFB.App.Services;

public static class ImageStealthEngine
{
    public static byte[]? OptimizeImageForUpload(string imagePath, int maxDim = 1200)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return null;

        try
        {
            var rng = Random.Shared;

            using var original = new Bitmap(imagePath);
            int origW = original.Width;
            int origH = original.Height;

            if (origW < 30 || origH < 30)
                return File.ReadAllBytes(imagePath);

            // 1. Vi cắt 1 - 3 pixel ở 4 cạnh
            int cropLeft = rng.Next(1, 4);
            int cropTop = rng.Next(1, 4);
            int cropRight = rng.Next(1, 4);
            int cropBottom = rng.Next(1, 4);

            int croppedW = Math.Max(20, origW - cropLeft - cropRight);
            int croppedH = Math.Max(20, origH - cropTop - cropBottom);

            // 2. Vi chỉnh scale 0.992x - 1.008x
            double scale = 0.992 + (rng.NextDouble() * (1.008 - 0.992));
            int scaledW = Math.Max(20, (int)(croppedW * scale));
            int scaledH = Math.Max(20, (int)(croppedH * scale));

            // Thu nhỏ nếu vượt quá maxDim
            if (scaledW > maxDim || scaledH > maxDim)
            {
                double r = Math.Min((double)maxDim / scaledW, (double)maxDim / scaledH);
                scaledW = (int)(scaledW * r);
                scaledH = (int)(scaledH * r);
            }

            // 3. Tạo Bitmap mới (tự động loại bỏ hoàn toàn metadata EXIF)
            using var destBitmap = new Bitmap(scaledW, scaledH, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(destBitmap))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                // Tinh chỉnh độ sáng & tương phản vi lượng (+- 1.5%) qua ColorMatrix
                float brightnessShift = (float)((rng.NextDouble() * 0.03) - 0.015);
                float contrast = 1.0f + (float)((rng.NextDouble() * 0.03) - 0.015);

                float t = 0.5f * (1.0f - contrast);
                var cm = new ColorMatrix(new[]
                {
                    new[] { contrast, 0, 0, 0, 0 },
                    new[] { 0, contrast, 0, 0, 0 },
                    new[] { 0, 0, contrast, 0, 0 },
                    new[] { 0, 0, 0, 1.0f, 0 },
                    new[] { t + brightnessShift, t + brightnessShift, t + brightnessShift, 0, 1 }
                });

                using var ia = new ImageAttributes();
                ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                var srcRect = new Rectangle(cropLeft, cropTop, croppedW, croppedH);
                var destRect = new Rectangle(0, 0, scaledW, scaledH);
                g.DrawImage(original, destRect, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height, GraphicsUnit.Pixel, ia);
            }

            // 4. Vi chỉnh hạt màu góc biên
            if (scaledW > 4 && scaledH > 4)
            {
                var corners = new[] { (0, 0), (scaledW - 1, 0), (0, scaledH - 1), (scaledW - 1, scaledH - 1) };
                foreach (var (cx, cy) in corners)
                {
                    try
                    {
                        var c = destBitmap.GetPixel(cx, cy);
                        int r = (c.R + rng.Next(1, 10)) % 256;
                        int gCol = (c.G + rng.Next(1, 10)) % 256;
                        int b = (c.B + rng.Next(1, 10)) % 256;
                        destBitmap.SetPixel(cx, cy, Color.FromArgb(r, gCol, b));
                    }
                    catch { }
                }
            }

            // 5. Nén JPEG ngẫu nhiên 83-92%
            using var ms = new MemoryStream();
            var encoder = GetEncoder(ImageFormat.Jpeg);
            if (encoder != null)
            {
                var encoderParams = new EncoderParameters(1);
                long quality = rng.Next(83, 93);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                destBitmap.Save(ms, encoder, encoderParams);
            }
            else
            {
                destBitmap.Save(ms, ImageFormat.Jpeg);
            }

            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageDecoders();
        return codecs.FirstOrDefault(codec => codec.FormatID == format.Guid);
    }
}
