using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace AjazzDeck
{
    /// <summary>
    /// Encodes key art the way the panel expects it. Size and pre-rotation vary by model:
    /// 85px on protocol v1, 95px (with an 82px sixth column) on the v3 AKP153s, 112px on
    /// the N4 and 293 V3, 60 or 64px on the knob decks. See <see cref="DeckModel"/>.
    /// </summary>
    public static class DeckImage
    {
        /// <summary>Protocol v1 AKP153 key size, and the size the editor previews at.</summary>
        public const int KeySize = 85;

        /// <summary>
        /// Verified on AKP153E hardware: the panel rotates what it is given, so images
        /// must be pre-rotated 270 degrees. (Equivalent to the "rotate 90 and mirror both
        /// axes" described in the reference implementations.)
        /// </summary>
        public const RotateFlipType DefaultOrientation = RotateFlipType.Rotate270FlipNone;

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

        /// <summary>
        /// Undoes a pre-rotation. The panel rotates whatever it is given, so an on-screen
        /// deck has to apply the same rotation in reverse to show what the hardware shows.
        /// The four flip variants are reflections and so are their own inverse; only the
        /// quarter turns need swapping.
        /// </summary>
        public static RotateFlipType InverseOrientation(RotateFlipType orientation)
        {
            switch (orientation)
            {
                case RotateFlipType.Rotate90FlipNone: return RotateFlipType.Rotate270FlipNone;
                case RotateFlipType.Rotate270FlipNone: return RotateFlipType.Rotate90FlipNone;
                default: return orientation;
            }
        }

        /// <summary>Encodes a bitmap for the deck using the default v1 size and orientation.</summary>
        public static byte[] ToKeyJpeg(Bitmap source)
        {
            return ToKeyJpeg(source, DefaultOrientation, KeySize, KeySize);
        }

        public static byte[] ToKeyJpeg(Bitmap source, RotateFlipType orientation, long quality = 90L)
        {
            return ToKeyJpeg(source, orientation, KeySize, KeySize, quality);
        }

        /// <summary>Encodes a bitmap for a specific key of a specific model.</summary>
        public static byte[] ToKeyJpeg(Bitmap source, DeckModel model, int grid, long quality = 90L)
        {
            if (model == null) throw new ArgumentNullException("model");
            int px = model.KeyPixels(grid);
            return ToKeyJpeg(source, model.Orientation, px, px, quality);
        }

        /// <summary>Encodes a bitmap for one of the knob screens.</summary>
        public static byte[] ToEncoderJpeg(Bitmap source, DeckModel model, long quality = 90L)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (!model.HasEncoderScreens)
                throw new InvalidOperationException(model.Name + " has no knob screens.");
            return ToKeyJpeg(source, model.Orientation,
                model.EncoderPixels.Width, model.EncoderPixels.Height, quality);
        }

        /// <summary>Encodes a bitmap as a JPEG at the given size and orientation.</summary>
        public static byte[] ToKeyJpeg(Bitmap source, RotateFlipType orientation,
                                       int width, int height, long quality = 90L)
        {
            using (var scaled = new Bitmap(width, height))
            {
                using (var g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source, 0, 0, width, height);
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
        public static Bitmap MakeTestTile(string glyph, Color background, int size = KeySize)
        {
            float s = size / (float)KeySize;
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.Clear(background);

                // white wedge in the TOP-LEFT corner, so mirroring is unambiguous
                g.FillPolygon(Brushes.White, new[]
                {
                    new PointF(0, 0), new PointF(28 * s, 0), new PointF(0, 28 * s)
                });

                using (var f = new Font("Segoe UI", 44f * s, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat
                       { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(glyph, f, Brushes.White,
                        new RectangleF(0, 0, size, size), sf);
                }
                g.DrawRectangle(Pens.White, 0, 0, size - 1, size - 1);
            }
            return bmp;
        }
    }
}
