using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace AjazzDeck
{
    /// <summary>
    /// The AKP153E expects a rotated/mirrored 85x85 JPEG. The exact orientation is
    /// determined empirically: see DeckProbe --imagetest.
    /// </summary>
    public static class DeckImage
    {
        public const int KeySize = 85;

        /// <summary>
        /// Verified on AKP153E hardware: the panel rotates what it is given, so images
        /// must be pre-rotated 270 degrees. (Equivalent to the "rotate 90 and mirror both
        /// axes" described in the reverse-engineering notes.)
        /// </summary>
        public const RotateFlipType DefaultOrientation = RotateFlipType.Rotate270FlipNone;

        /// <summary>Encodes a bitmap for the deck using the verified orientation.</summary>
        public static byte[] ToKeyJpeg(Bitmap source) { return ToKeyJpeg(source, DefaultOrientation); }

        public static readonly RotateFlipType[] AllOrientations =
        {
            RotateFlipType.RotateNoneFlipNone,  // 1
            RotateFlipType.Rotate90FlipNone,    // 2
            RotateFlipType.Rotate180FlipNone,   // 3
            RotateFlipType.Rotate270FlipNone,   // 4
            RotateFlipType.RotateNoneFlipX,     // 5
            RotateFlipType.Rotate90FlipX,       // 6
            RotateFlipType.Rotate180FlipX,      // 7
            RotateFlipType.Rotate270FlipX       // 8
        };

        /// <summary>Encodes a bitmap as a JPEG at the size and orientation the deck expects.</summary>
        public static byte[] ToKeyJpeg(Bitmap source, RotateFlipType orientation, long quality = 90L)
        {
            using (var scaled = new Bitmap(KeySize, KeySize))
            {
                using (var g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source, 0, 0, KeySize, KeySize);
                }
                scaled.RotateFlip(orientation);

                var codec = GetJpegCodec();
                using (var ms = new MemoryStream())
                using (var p = new EncoderParameters(1))
                {
                    p.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                    scaled.Save(ms, codec, p);
                    return ms.ToArray();
                }
            }
        }

        static ImageCodecInfo GetJpegCodec()
        {
            foreach (var c in ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
            throw new InvalidOperationException("No JPEG encoder available.");
        }

        /// <summary>Crude placeholder tile: solid colour, a bold glyph, and a corner marker.</summary>
        public static Bitmap MakeTestTile(string glyph, Color background)
        {
            var bmp = new Bitmap(KeySize, KeySize);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.Clear(background);

                // white wedge in the TOP-LEFT corner, so mirroring is unambiguous
                g.FillPolygon(Brushes.White, new[]
                {
                    new Point(0, 0), new Point(28, 0), new Point(0, 28)
                });

                using (var f = new Font("Segoe UI", 44f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat
                       { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(glyph, f, Brushes.White,
                        new RectangleF(0, 0, KeySize, KeySize), sf);
                }
                g.DrawRectangle(Pens.White, 0, 0, KeySize - 1, KeySize - 1);
            }
            return bmp;
        }
    }
}

