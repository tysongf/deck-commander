using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;

namespace AjazzDeck.Core
{
    /// <summary>Draws the 85x85 tiles that get pushed to the deck.</summary>
    public static class KeyRenderer
    {
        public const int Size = DeckImage.KeySize;

        static readonly Color Empty = Color.FromArgb(24, 26, 31);
        static readonly Color NavBack = Color.FromArgb(38, 42, 52);
        static readonly Color Accent = Color.FromArgb(90, 160, 255);

        public static Color ParseColour(string hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            try
            {
                var s = hex.Trim().TrimStart('#');
                if (s.Length == 6)
                    return Color.FromArgb(
                        Convert.ToInt32(s.Substring(0, 2), 16),
                        Convert.ToInt32(s.Substring(2, 2), 16),
                        Convert.ToInt32(s.Substring(4, 2), 16));
            }
            catch { }
            return fallback;
        }

        static Bitmap NewTile(Color background, out Graphics g)
        {
            var bmp = new Bitmap(Size, Size);
            g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(background);
            return bmp;
        }

        static void DrawCentredText(Graphics g, string text, float emSize, Color colour,
                                    RectangleF area, FontStyle style = FontStyle.Bold)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var f = new Font("Segoe UI", emSize, style, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(colour))
            using (var sf = new StringFormat
                   {
                       Alignment = StringAlignment.Center,
                       LineAlignment = StringAlignment.Center,
                       Trimming = StringTrimming.EllipsisCharacter
                   })
            {
                g.DrawString(text, f, b, area, sf);
            }
        }

        /// <summary>Draws a placeholder glyph centred in the given box.</summary>
        public static void DrawGlyph(Graphics g, KeyGlyph glyph, RectangleF box, Color colour)
        {
            if (glyph == KeyGlyph.None) return;
            float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
            float r = Math.Min(box.Width, box.Height) * 0.34f;

            using (var brush = new SolidBrush(colour))
            using (var pen = new Pen(colour, Math.Max(3f, r * 0.28f))
                   { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (glyph)
                {
                    case KeyGlyph.Circle:
                        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2); break;
                    case KeyGlyph.Square:
                        g.DrawRectangle(pen, cx - r, cy - r, r * 2, r * 2); break;
                    case KeyGlyph.Triangle:
                        g.FillPolygon(brush, new[] {
                            new PointF(cx, cy - r), new PointF(cx + r, cy + r), new PointF(cx - r, cy + r) });
                        break;
                    case KeyGlyph.Diamond:
                        g.FillPolygon(brush, new[] {
                            new PointF(cx, cy - r), new PointF(cx + r, cy),
                            new PointF(cx, cy + r), new PointF(cx - r, cy) });
                        break;
                    case KeyGlyph.Cross:
                        g.DrawLine(pen, cx - r, cy - r, cx + r, cy + r);
                        g.DrawLine(pen, cx + r, cy - r, cx - r, cy + r); break;
                    case KeyGlyph.Check:
                        g.DrawLines(pen, new[] {
                            new PointF(cx - r, cy), new PointF(cx - r * 0.25f, cy + r * 0.7f),
                            new PointF(cx + r, cy - r * 0.7f) });
                        break;
                    case KeyGlyph.ArrowUp:
                        g.FillPolygon(brush, new[] {
                            new PointF(cx, cy - r), new PointF(cx + r, cy + r * 0.4f),
                            new PointF(cx - r, cy + r * 0.4f) });
                        break;
                    case KeyGlyph.ArrowDown:
                        g.FillPolygon(brush, new[] {
                            new PointF(cx, cy + r), new PointF(cx + r, cy - r * 0.4f),
                            new PointF(cx - r, cy - r * 0.4f) });
                        break;
                    case KeyGlyph.ArrowLeft:
                        g.FillPolygon(brush, new[] {
                            new PointF(cx - r, cy), new PointF(cx + r * 0.4f, cy - r),
                            new PointF(cx + r * 0.4f, cy + r) });
                        break;
                    case KeyGlyph.ArrowRight:
                        g.FillPolygon(brush, new[] {
                            new PointF(cx + r, cy), new PointF(cx - r * 0.4f, cy - r),
                            new PointF(cx - r * 0.4f, cy + r) });
                        break;
                    case KeyGlyph.Play:
                        g.FillPolygon(brush, new[] {
                            new PointF(cx + r, cy), new PointF(cx - r * 0.7f, cy - r),
                            new PointF(cx - r * 0.7f, cy + r) });
                        break;
                    case KeyGlyph.Stop:
                        g.FillRectangle(brush, cx - r * 0.85f, cy - r * 0.85f, r * 1.7f, r * 1.7f); break;
                    case KeyGlyph.Pause:
                        g.FillRectangle(brush, cx - r * 0.75f, cy - r, r * 0.55f, r * 2);
                        g.FillRectangle(brush, cx + r * 0.2f,  cy - r, r * 0.55f, r * 2); break;
                    case KeyGlyph.Star:
                        {
                            var pts = new PointF[10];
                            for (int i = 0; i < 10; i++)
                            {
                                double ang = -Math.PI / 2 + i * Math.PI / 5;
                                float rad = (i % 2 == 0) ? r : r * 0.45f;
                                pts[i] = new PointF(cx + (float)(Math.Cos(ang) * rad),
                                                    cy + (float)(Math.Sin(ang) * rad));
                            }
                            g.FillPolygon(brush, pts);
                        }
                        break;
                    case KeyGlyph.Warning:
                        g.FillPolygon(brush, new[] {
                            new PointF(cx, cy - r), new PointF(cx + r, cy + r * 0.8f),
                            new PointF(cx - r, cy + r * 0.8f) });
                        using (var dark = new SolidBrush(Color.FromArgb(30, 30, 30)))
                        {
                            g.FillRectangle(dark, cx - r * 0.12f, cy - r * 0.35f, r * 0.24f, r * 0.75f);
                            g.FillEllipse(dark, cx - r * 0.14f, cy + r * 0.5f, r * 0.28f, r * 0.28f);
                        }
                        break;
                    case KeyGlyph.Power:
                        g.DrawArc(pen, cx - r, cy - r * 0.8f, r * 2, r * 2, -60, 300);
                        g.DrawLine(pen, cx, cy - r, cx, cy); break;
                    case KeyGlyph.Target:
                        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                        g.FillEllipse(brush, cx - r * 0.3f, cy - r * 0.3f, r * 0.6f, r * 0.6f); break;
                }
            }
        }

        /// <summary>Renders a configured action key.</summary>
        public static Bitmap Render(KeyDefinition key)
        {
            if (key == null || key.IsEmpty) return RenderEmpty();

            Graphics g;
            var bmp = NewTile(ParseColour(key.Background, Empty), out g);
            using (g)
            {
                if (!string.IsNullOrWhiteSpace(key.ImagePath) && File.Exists(key.ImagePath))
                {
                    try
                    {
                        using (var src = new Bitmap(key.ImagePath))
                        {
                            // fit inside the tile, leaving room for a label if present
                            int box = string.IsNullOrWhiteSpace(key.Label) ? Size : Size - 22;
                            float scale = Math.Min((float)box / src.Width, (float)box / src.Height);
                            int w = Math.Max(1, (int)(src.Width * scale));
                            int h = Math.Max(1, (int)(src.Height * scale));
                            g.DrawImage(src, (Size - w) / 2, (box - h) / 2, w, h);
                        }
                    }
                    catch { /* unreadable image: fall through to the label */ }
                }

                bool hasArt = !string.IsNullOrWhiteSpace(key.ImagePath) && File.Exists(key.ImagePath);
                if (!hasArt && key.Glyph != KeyGlyph.None)
                {
                    int box = string.IsNullOrWhiteSpace(key.Label) ? Size : Size - 22;
                    DrawGlyph(g, key.Glyph, new RectangleF(8, 6, Size - 16, box - 12), Color.White);
                    hasArt = true;
                }

                if (!string.IsNullOrWhiteSpace(key.Label))
                {
                    bool hasImage = hasArt;
                    if (hasImage)
                    {
                        using (var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                            g.FillRectangle(shade, 0, Size - 24, Size, 24);
                        DrawCentredText(g, key.Label, 15f, Color.White,
                            new RectangleF(2, Size - 24, Size - 4, 24));
                    }
                    else
                    {
                        DrawCentredText(g, key.Label, 17f, Color.White,
                            new RectangleF(3, 3, Size - 6, Size - 6));
                    }
                }
            }
            return bmp;
        }

        public static Bitmap RenderEmpty()
        {
            Graphics g;
            var bmp = NewTile(Empty, out g);
            using (g)
            using (var pen = new Pen(Color.FromArgb(44, 48, 58), 1))
                g.DrawRectangle(pen, 1, 1, Size - 3, Size - 3);
            return bmp;
        }

        /// <summary>Page up / page down arrows.</summary>
        public static Bitmap RenderArrow(bool up, bool enabled)
        {
            Graphics g;
            var bmp = NewTile(NavBack, out g);
            using (g)
            {
                var colour = enabled ? Accent : Color.FromArgb(70, 76, 90);
                using (var b = new SolidBrush(colour))
                {
                    Point[] tri = up
                        ? new[] { new Point(42, 26), new Point(64, 56), new Point(20, 56) }
                        : new[] { new Point(42, 59), new Point(64, 29), new Point(20, 29) };
                    g.FillPolygon(b, tri);
                }
            }
            return bmp;
        }

        /// <summary>The "3 / 7" indicator, or a cancel glyph while the selector is open.</summary>
        public static Bitmap RenderIndicator(int currentPage, int totalPages, bool selectorOpen)
        {
            Graphics g;
            var bmp = NewTile(selectorOpen ? Color.FromArgb(90, 40, 44) : NavBack, out g);
            using (g)
            {
                if (selectorOpen)
                {
                    using (var pen = new Pen(Color.FromArgb(255, 120, 120), 7f))
                    {
                        g.DrawLine(pen, 28, 28, 57, 57);
                        g.DrawLine(pen, 57, 28, 28, 57);
                    }
                    DrawCentredText(g, "CANCEL", 11f, Color.FromArgb(255, 170, 170),
                        new RectangleF(0, Size - 22, Size, 18));
                }
                else
                {
                    DrawCentredText(g, currentPage.ToString(), 34f, Color.White,
                        new RectangleF(0, 4, Size, 44));
                    using (var pen = new Pen(Color.FromArgb(80, 88, 105), 1))
                        g.DrawLine(pen, 22, 50, Size - 22, 50);
                    DrawCentredText(g, "of " + totalPages, 16f, Color.FromArgb(165, 175, 195),
                        new RectangleF(0, 52, Size, 26), FontStyle.Regular);
                }
            }
            return bmp;
        }

        /// <summary>A page tile inside the page selector.</summary>
        public static Bitmap RenderPageTile(int pageNumber, string name, bool isCurrent)
        {
            Graphics g;
            var bmp = NewTile(isCurrent ? Color.FromArgb(30, 70, 130) : Color.FromArgb(30, 34, 42), out g);
            using (g)
            {
                if (isCurrent)
                    using (var pen = new Pen(Accent, 3f))
                        g.DrawRectangle(pen, 2, 2, Size - 5, Size - 5);

                DrawCentredText(g, pageNumber.ToString(), 30f, Color.White,
                    new RectangleF(0, 6, Size, 40));
                DrawCentredText(g, name ?? "", 12f, Color.FromArgb(180, 190, 210),
                    new RectangleF(3, 48, Size - 6, 32), FontStyle.Regular);
            }
            return bmp;
        }
    }
}

