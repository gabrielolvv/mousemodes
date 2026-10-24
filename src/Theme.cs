using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MouseModes
{
    public enum GlyphKind
    {
        None,
        Bolt,
        Monitor,
        Gauge,
        Gear,
        Power,
        Mouse,
        Close,
        Minimize
    }

    /// <summary>Paleta, fontes e funções de desenho do visual escuro/neon.</summary>
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(10, 13, 20);
        public static readonly Color Panel = Color.FromArgb(17, 22, 33);
        public static readonly Color Panel2 = Color.FromArgb(24, 30, 43);
        public static readonly Color Border = Color.FromArgb(37, 46, 64);
        public static readonly Color Text = Color.FromArgb(233, 237, 245);
        public static readonly Color Muted = Color.FromArgb(127, 138, 160);
        public static readonly Color Faint = Color.FromArgb(74, 84, 104);
        public static readonly Color Danger = Color.FromArgb(255, 59, 92);

        public static readonly Color PerfA = Color.FromArgb(255, 61, 46);
        public static readonly Color PerfB = Color.FromArgb(255, 159, 28);
        public static readonly Color DailyA = Color.FromArgb(46, 123, 255);
        public static readonly Color DailyB = Color.FromArgb(0, 224, 255);

        public static Color AccentA(Mode m) { return m == Mode.Performance ? PerfA : DailyA; }
        public static Color AccentB(Mode m) { return m == Mode.Performance ? PerfB : DailyB; }

        // ---------- Cores ----------

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c.R, c.G, c.B);
        }

        public static float Approach(float value, float target, float speed)
        {
            float next = value + (target - value) * speed;
            return Math.Abs(target - next) < 0.01f ? target : next;
        }

        // ---------- Fontes ----------

        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();
        private static string displayFamily;

        /// <summary>Fonte de títulos: Bahnschrift (vem no Windows 10/11), com reserva para Segoe UI.</summary>
        public static Font Display(float size, FontStyle style = FontStyle.Bold)
        {
            if (displayFamily == null) displayFamily = Installed("Bahnschrift") ? "Bahnschrift" : "Segoe UI";
            return GetFont(displayFamily, size, style);
        }

        public static Font Ui(float size, FontStyle style = FontStyle.Regular)
        {
            return GetFont("Segoe UI", size, style);
        }

        public static Font UiSemibold(float size)
        {
            return Installed("Segoe UI Semibold")
                ? GetFont("Segoe UI Semibold", size, FontStyle.Regular)
                : GetFont("Segoe UI", size, FontStyle.Bold);
        }

        private static readonly Dictionary<string, bool> InstalledCache = new Dictionary<string, bool>();

        private static bool Installed(string family)
        {
            bool result;
            if (InstalledCache.TryGetValue(family, out result)) return result;
            try
            {
                using (var f = new FontFamily(family)) result = string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase);
            }
            catch { result = false; }
            InstalledCache[family] = result;
            return result;
        }

        private static Font GetFont(string family, float size, FontStyle style)
        {
            string key = family + "|" + size + "|" + (int)style;
            Font f;
            if (!FontCache.TryGetValue(key, out f))
            {
                f = new Font(family, size, style, GraphicsUnit.Point);
                FontCache[key] = f;
            }
            return f;
        }

        // ---------- Desenho ----------

        public static void HighQuality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, RectangleF r, float radius, Brush brush)
        {
            using (GraphicsPath p = Rounded(r, radius)) g.FillPath(brush, p);
        }

        public static void FillRounded(Graphics g, RectangleF r, float radius, Color color)
        {
            using (var b = new SolidBrush(color)) FillRounded(g, r, radius, b);
        }

        public static void StrokeRounded(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (var pen = new Pen(color, width)) g.DrawPath(pen, p);
        }

        public static void StrokeRounded(Graphics g, RectangleF r, float radius, Brush brush, float width = 1f)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (var pen = new Pen(brush, width)) g.DrawPath(pen, p);
        }

        /// <summary>Brilho neon ao redor de um retângulo arredondado (várias bordas com transparência).</summary>
        public static void Glow(Graphics g, RectangleF r, float radius, Color color, float strength, int layers = 7, float spread = 1.4f)
        {
            if (strength <= 0.01f) return;
            for (int i = layers; i >= 1; i--)
            {
                float t = (float)i / layers;
                int alpha = (int)(70 * strength * (1 - t) * (1 - t) + 6 * strength);
                RectangleF rr = RectangleF.Inflate(r, i * spread, i * spread);
                StrokeRounded(g, rr, radius + i * spread, Alpha(color, alpha), spread + 0.6f);
            }
        }

        public static LinearGradientBrush Gradient(RectangleF r, Color a, Color b, float angle = 0f)
        {
            RectangleF safe = new RectangleF(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height));
            return new LinearGradientBrush(safe, a, b, angle);
        }

        // ---------- Texto com espaçamento entre letras ----------

        private static StringFormat typographic;

        private static StringFormat Typographic
        {
            get
            {
                if (typographic == null)
                {
                    typographic = (StringFormat)StringFormat.GenericTypographic.Clone();
                    typographic.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                }
                return typographic;
            }
        }

        public static SizeF MeasureSpaced(Graphics g, string text, Font font, float spacing)
        {
            float w = 0, h = 0;
            for (int i = 0; i < text.Length; i++)
            {
                SizeF s = g.MeasureString(text[i].ToString(), font, PointF.Empty, Typographic);
                w += s.Width + (i < text.Length - 1 ? spacing : 0);
                h = Math.Max(h, s.Height);
            }
            if (text.Length == 0) h = font.GetHeight(g);
            return new SizeF(w, h);
        }

        public static void DrawSpaced(Graphics g, string text, Font font, Brush brush, float x, float y, float spacing)
        {
            for (int i = 0; i < text.Length; i++)
            {
                string ch = text[i].ToString();
                g.DrawString(ch, font, brush, x, y, Typographic);
                x += g.MeasureString(ch, font, PointF.Empty, Typographic).Width + spacing;
            }
        }

        public static void DrawSpaced(Graphics g, string text, Font font, Color color, float x, float y, float spacing)
        {
            using (var b = new SolidBrush(color)) DrawSpaced(g, text, font, b, x, y, spacing);
        }

        /// <summary>Texto com espaçamento centralizado num retângulo.</summary>
        public static void DrawSpacedCentered(Graphics g, string text, Font font, Brush brush, RectangleF r, float spacing)
        {
            SizeF size = MeasureSpaced(g, text, font, spacing);
            DrawSpaced(g, text, font, brush, r.X + (r.Width - size.Width) / 2, r.Y + (r.Height - size.Height) / 2, spacing);
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, RectangleF r,
            StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Near, bool wrap = true)
        {
            using (var b = new SolidBrush(color))
            using (var f = new StringFormat { Alignment = h, LineAlignment = v, Trimming = StringTrimming.EllipsisCharacter })
            {
                if (!wrap) f.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(text ?? "", font, b, r, f);
            }
        }

        // ---------- Chips de informação (cartão do modo e medidor) ----------

        public static void DrawChips(Graphics g, RectangleF area, string[] labels, string[] values, Color accent)
        {
            int n = labels.Length;
            if (n == 0) return;
            const float gap = 8f;
            float w = (area.Width - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                var r = new RectangleF(area.X + i * (w + gap), area.Y, w, area.Height);
                FillRounded(g, r, 8, Panel2);
                using (var b = new SolidBrush(Alpha(accent, 160)))
                    g.FillRectangle(b, r.X + 10, r.Y + 8, 3, 3);
                DrawSpaced(g, labels[i], Ui(6.5f, FontStyle.Bold), Muted, r.X + 18, r.Y + 5, 1.2f);
                DrawText(g, values[i], Display(11f), Text, new RectangleF(r.X + 9, r.Y + 20, r.Width - 12, r.Height - 22), wrap: false);
            }
        }

        // ---------- Ícones vetoriais ----------

        public static void DrawGlyph(Graphics g, GlyphKind kind, RectangleF r, Brush brush, float stroke)
        {
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            using (var pen = new Pen(brush, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (kind)
                {
                    case GlyphKind.Bolt:
                        g.FillPolygon(brush, new[]
                        {
                            new PointF(x + w * 0.58f, y),
                            new PointF(x + w * 0.16f, y + h * 0.58f),
                            new PointF(x + w * 0.46f, y + h * 0.58f),
                            new PointF(x + w * 0.38f, y + h),
                            new PointF(x + w * 0.84f, y + h * 0.40f),
                            new PointF(x + w * 0.54f, y + h * 0.40f)
                        });
                        break;

                    case GlyphKind.Monitor:
                        using (GraphicsPath screen = Rounded(new RectangleF(x + w * 0.06f, y + h * 0.10f, w * 0.88f, h * 0.60f), w * 0.08f))
                            g.DrawPath(pen, screen);
                        g.DrawLine(pen, x + w * 0.50f, y + h * 0.72f, x + w * 0.50f, y + h * 0.88f);
                        g.DrawLine(pen, x + w * 0.30f, y + h * 0.90f, x + w * 0.70f, y + h * 0.90f);
                        break;

                    case GlyphKind.Gauge:
                        g.DrawArc(pen, x + w * 0.08f, y + h * 0.14f, w * 0.84f, h * 0.84f, 160, 220);
                        g.DrawLine(pen, x + w * 0.50f, y + h * 0.56f, x + w * 0.74f, y + h * 0.32f);
                        g.FillEllipse(brush, x + w * 0.43f, y + h * 0.49f, w * 0.14f, h * 0.14f);
                        break;

                    case GlyphKind.Gear:
                        float cx = x + w / 2, cy = y + h / 2;
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(pen,
                                cx + (float)Math.Cos(a) * w * 0.30f, cy + (float)Math.Sin(a) * h * 0.30f,
                                cx + (float)Math.Cos(a) * w * 0.44f, cy + (float)Math.Sin(a) * h * 0.44f);
                        }
                        g.DrawEllipse(pen, cx - w * 0.30f, cy - h * 0.30f, w * 0.60f, h * 0.60f);
                        g.DrawEllipse(pen, cx - w * 0.11f, cy - h * 0.11f, w * 0.22f, h * 0.22f);
                        break;

                    case GlyphKind.Power:
                        g.DrawArc(pen, x + w * 0.14f, y + h * 0.16f, w * 0.72f, h * 0.72f, -50, 280);
                        g.DrawLine(pen, x + w * 0.50f, y + h * 0.06f, x + w * 0.50f, y + h * 0.46f);
                        break;

                    case GlyphKind.Mouse:
                        using (GraphicsPath body = Rounded(new RectangleF(x + w * 0.22f, y + h * 0.04f, w * 0.56f, h * 0.92f), w * 0.28f))
                            g.DrawPath(pen, body);
                        g.DrawLine(pen, x + w * 0.50f, y + h * 0.18f, x + w * 0.50f, y + h * 0.34f);
                        break;

                    case GlyphKind.Close:
                        g.DrawLine(pen, x, y, x + w, y + h);
                        g.DrawLine(pen, x + w, y, x, y + h);
                        break;

                    case GlyphKind.Minimize:
                        g.DrawLine(pen, x, y + h / 2, x + w, y + h / 2);
                        break;
                }
            }
        }
    }
}
