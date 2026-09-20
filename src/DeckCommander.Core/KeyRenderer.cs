using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using DeckCommander.Devices;

namespace DeckCommander.Core
{
    /// <summary>
    /// Draws the tiles that get pushed to the deck. Sizes run from 60px on the knob decks
    /// to 112px on the N4, so every measurement is expressed relative to the 85px tile the
    /// artwork was designed at and scaled on the way out.
    /// </summary>
    public sealed class KeyRenderer
    {
        /// <summary>The size the layout constants were authored at.</summary>
        public const int BaseSize = DeckImage.KeySize;

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>Scale from the authored 85px tile to this renderer's tile.</summary>
        readonly float _s;

        static readonly Color Empty = Color.FromArgb(24, 26, 31);
        static readonly Color NavBack = Color.FromArgb(38, 42, 52);
        static readonly Color Accent = Color.FromArgb(90, 160, 255);

        public KeyRenderer(int size) : this(size, size) { }

        public KeyRenderer(int width, int height)
        {
            if (width < 8 || height < 8) throw new ArgumentOutOfRangeException("width");
            Width = width;
            Height = height;
            _s = Math.Min(width, height) / (float)BaseSize;
        }

        /// <summary>An 85px renderer, matching the protocol v1 decks and the editor preview.</summary>
        public static readonly KeyRenderer Default = new KeyRenderer(BaseSize);

        /// <summary>The renderer for one key of a specific deck.</summary>
        public static KeyRenderer For(DeckModel model, int grid)
        {
            int px = model.KeyPixels(grid);
            return px == BaseSize ? Default : new KeyRenderer(px);
        }

        float S(float authored) { return authored * _s; }

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

        Bitmap NewTile(Color background, out Graphics g)
        {
            var bmp = new Bitmap(Width, Height);
            g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(background);
            return bmp;
        }

        void DrawCentredText(Graphics g, string text, float emSize, Color colour,
                             RectangleF area, FontStyle style = FontStyle.Bold)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var f = new Font("Segoe UI", Math.Max(6f, S(emSize)), style, GraphicsUnit.Pixel))
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
            using (var pen = new Pen(colour, Math.Max(1.5f, r * 0.28f))
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
        public Bitmap Render(KeyDefinition key)
        {
            if (key == null || key.IsEmpty) return RenderEmpty();

            float labelBand = S(24);
            Graphics g;
            var bmp = NewTile(ParseColour(key.Background, Empty), out g);
            using (g)
            {
                bool hasArt = false;

                if (!string.IsNullOrWhiteSpace(key.ImagePath) && File.Exists(key.ImagePath))
                {
                    try
                    {
                        using (var src = new Bitmap(key.ImagePath))
                        {
                            // fit inside the tile, leaving room for a label if present
                            float box = string.IsNullOrWhiteSpace(key.Label) ? Height : Height - S(22);
                            float scale = Math.Min(Width / (float)src.Width, box / src.Height);
                            int w = Math.Max(1, (int)(src.Width * scale));
                            int h = Math.Max(1, (int)(src.Height * scale));
                            g.DrawImage(src, (Width - w) / 2, (int)((box - h) / 2), w, h);
                            hasArt = true;
                        }
                    }
                    catch { /* unreadable image: fall through to the label */ }
                }

                if (!hasArt && key.Glyph != KeyGlyph.None)
                {
                    float box = string.IsNullOrWhiteSpace(key.Label) ? Height : Height - S(22);
                    DrawGlyph(g, key.Glyph,
                        new RectangleF(S(8), S(6), Width - S(16), box - S(12)), Color.White);
                    hasArt = true;
                }

                if (!string.IsNullOrWhiteSpace(key.Label))
                {
                    if (hasArt)
                    {
                        using (var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                            g.FillRectangle(shade, 0, Height - labelBand, Width, labelBand);
                        DrawCentredText(g, key.Label, 15f, Color.White,
                            new RectangleF(S(2), Height - labelBand, Width - S(4), labelBand));
                    }
                    else
                    {
                        DrawCentredText(g, key.Label, 17f, Color.White,
                            new RectangleF(S(3), S(3), Width - S(6), Height - S(6)));
                    }
                }
            }
            return bmp;
        }

        public Bitmap RenderEmpty()
        {
            Graphics g;
            var bmp = NewTile(Empty, out g);
            using (g)
            using (var pen = new Pen(Color.FromArgb(44, 48, 58), 1))
                g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
            return bmp;
        }

        /// <summary>Page up / page down arrows.</summary>
        public Bitmap RenderArrow(bool up, bool enabled)
        {
            Graphics g;
            var bmp = NewTile(NavBack, out g);
            using (g)
            {
                var colour = enabled ? Accent : Color.FromArgb(70, 76, 90);
                using (var b = new SolidBrush(colour))
                {
                    float cx = Width / 2f, cy = Height / 2f, r = Math.Min(Width, Height) * 0.26f;
                    PointF[] tri = up
                        ? new[] { new PointF(cx, cy - r), new PointF(cx + r * 1.05f, cy + r * 0.72f),
                                  new PointF(cx - r * 1.05f, cy + r * 0.72f) }
                        : new[] { new PointF(cx, cy + r), new PointF(cx + r * 1.05f, cy - r * 0.72f),
                                  new PointF(cx - r * 1.05f, cy - r * 0.72f) };
                    g.FillPolygon(b, tri);
                }
            }
            return bmp;
        }

        /// <summary>The "3 of 7" indicator, or a cancel glyph while the selector is open.</summary>
        public Bitmap RenderIndicator(int currentPage, int totalPages, bool selectorOpen)
        {
            Graphics g;
            var bmp = NewTile(selectorOpen ? Color.FromArgb(90, 40, 44) : NavBack, out g);
            using (g)
            {
                if (selectorOpen)
                {
                    using (var pen = new Pen(Color.FromArgb(255, 120, 120), Math.Max(2f, S(7))))
                    {
                        g.DrawLine(pen, S(28), S(28), S(57), S(57));
                        g.DrawLine(pen, S(57), S(28), S(28), S(57));
                    }
                    DrawCentredText(g, "CANCEL", 11f, Color.FromArgb(255, 170, 170),
                        new RectangleF(0, Height - S(22), Width, S(18)));
                }
                else
                {
                    DrawCentredText(g, currentPage.ToString(), 34f, Color.White,
                        new RectangleF(0, S(4), Width, S(44)));
                    using (var pen = new Pen(Color.FromArgb(80, 88, 105), 1))
                        g.DrawLine(pen, S(22), S(50), Width - S(22), S(50));
                    DrawCentredText(g, "of " + totalPages, 16f, Color.FromArgb(165, 175, 195),
                        new RectangleF(0, S(52), Width, S(26)), FontStyle.Regular);
                }
            }
            return bmp;
        }

        /// <summary>A page tile inside the page selector.</summary>
        public Bitmap RenderPageTile(int pageNumber, string name, bool isCurrent)
        {
            Graphics g;
            var bmp = NewTile(isCurrent ? Color.FromArgb(30, 70, 130) : Color.FromArgb(30, 34, 42), out g);
            using (g)
            {
                if (isCurrent)
                    using (var pen = new Pen(Accent, Math.Max(1.5f, S(3))))
                        g.DrawRectangle(pen, 2, 2, Width - 5, Height - 5);

                DrawCentredText(g, pageNumber.ToString(), 30f, Color.White,
                    new RectangleF(0, S(6), Width, S(40)));
                DrawCentredText(g, name ?? "", 12f, Color.FromArgb(180, 190, 210),
                    new RectangleF(S(3), S(48), Width - S(6), S(32)), FontStyle.Regular);
            }
            return bmp;
        }

        /// <summary>A knob's screen: its label over the event it fires.</summary>
        public Bitmap RenderEncoder(EncoderDefinition enc)
        {
            Graphics g;
            var bmp = NewTile(Empty, out g);
            using (g)
            {
                if (enc == null || enc.IsEmpty)
                {
                    using (var pen = new Pen(Color.FromArgb(44, 48, 58), 1))
                        g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
                    return bmp;
                }
                DrawCentredText(g, enc.Label, 17f, Color.White,
                    new RectangleF(S(3), S(3), Width - S(6), Height - S(6)));
            }
            return bmp;
        }
    }
}
