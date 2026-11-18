using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
        Target,
        Close,
        Minimize,
        Shield,
        /// <summary>Dois controles deslizantes (sensibilidade).</summary>
        Sliders,
        /// <summary>Régua (medir o DPI real).</summary>
        Ruler,
        /// <summary>Barras de gráfico (Raio-X da partida).</summary>
        Chart,
        /// <summary>Relógio (partidas anteriores).</summary>
        Clock,
        /// <summary>Ponto de exclamação (aviso, confirmação de ação destrutiva).</summary>
        Alert,
        /// <summary>Visto (veredito bom).</summary>
        Check
    }

    /// <summary>
    /// Cores de um modo no visual Carbon. Fill é a cor sólida de superfícies com texto (com OnFill por cima); OnSurface
    /// é o texto de acento sobre Surface; Tint e Stroke marcam seleção; SigA→SigB é o gradiente de assinatura (logo,
    /// barra do cartão principal, ícone do modo ativo), nunca atrás de texto.
    /// </summary>
    public struct ModePalette
    {
        public Color Fill;
        public Color Hover;
        public Color Pressed;
        public Color OnFill;
        public Color OnSurface;
        public Color Tint;
        public Color Stroke;
        public Color SigA;
        public Color SigB;

        internal static ModePalette From(Color fill, Color hover, Color pressed, Color onFill, Color onSurface, Color sigA, Color sigB)
        {
            return new ModePalette
            {
                Fill = fill,
                Hover = hover,
                Pressed = pressed,
                OnFill = onFill,
                OnSurface = onSurface,
                Tint = Color.FromArgb(26, fill.R, fill.G, fill.B),
                Stroke = Color.FromArgb(160, fill.R, fill.G, fill.B),
                SigA = sigA,
                SigB = sigB
            };
        }
    }

    /// <summary>Paleta, fontes e funções de desenho do visual escuro (direção Carbon).</summary>
    public static class Theme
    {
        // ---------- Superfícies ----------
        public static readonly Color Bg = Color.FromArgb(14, 16, 20);
        public static readonly Color Surface = Color.FromArgb(22, 25, 31);
        public static readonly Color Surface2 = Color.FromArgb(29, 33, 41);
        public static readonly Color SurfaceHover = Color.FromArgb(35, 40, 51);
        public static readonly Color Border = Color.FromArgb(38, 43, 53);
        public static readonly Color BorderStrong = Color.FromArgb(58, 64, 76);
        /// <summary>Divisória translúcida (1 px) entre linhas e abaixo da barra de título.</summary>
        public static readonly Color Divider = Color.FromArgb(10, 255, 255, 255);
        /// <summary>Ornamento: marcas do slider, grade de gráfico, controle desabilitado. Nunca para texto.</summary>
        public static readonly Color Decor = Color.FromArgb(44, 50, 61);

        // ---------- Texto ----------
        public static readonly Color Text = Color.FromArgb(242, 244, 247);
        /// <summary>Frases de apoio, subtítulos (cerca de 8:1 sobre Bg).</summary>
        public static readonly Color TextSecondary = Color.FromArgb(164, 171, 184);
        /// <summary>Meta curta: contagem, data, dica (cerca de 6:1 sobre Bg).</summary>
        public static readonly Color TextTertiary = Color.FromArgb(138, 147, 166);

        // Nomes antigos, apontando para os tokens novos: as chamadas de antes continuam valendo.
        public static readonly Color Panel = Surface;
        public static readonly Color Panel2 = Surface2;
        public static readonly Color Muted = TextSecondary;
        /// <summary>Antes um cinza quase ilegível; agora é o TextTertiary (todo uso dele é texto). Ornamento usa Decor.</summary>
        public static readonly Color Faint = TextTertiary;

        // ---------- Semânticas (nunca as cores dos modos) ----------
        public static readonly Color Success = Color.FromArgb(61, 214, 140);
        public static readonly Color Warning = Color.FromArgb(245, 184, 61);
        public static readonly Color Danger = Color.FromArgb(240, 87, 90);
        /// <summary>Texto e ícone de perigo sobre superfície escura.</summary>
        public static readonly Color DangerText = Color.FromArgb(255, 138, 140);
        /// <summary>Fundo sólido da confirmação destrutiva (texto branco, cerca de 5:1).</summary>
        public static readonly Color DangerSolid = Color.FromArgb(201, 58, 67);

        // ---------- Assinatura dos modos (gradiente: logo, barra do cartão, ícone do modo ativo) ----------
        public static readonly Color PerfA = Color.FromArgb(255, 61, 46);
        public static readonly Color PerfB = Color.FromArgb(255, 159, 28);
        public static readonly Color DailyA = Color.FromArgb(46, 123, 255);
        public static readonly Color DailyB = Color.FromArgb(0, 224, 255);

        public static Color AccentA(Mode m) { return m == Mode.Performance ? PerfA : DailyA; }
        public static Color AccentB(Mode m) { return m == Mode.Performance ? PerfB : DailyB; }

        // ---------- Paletas ----------

        public static readonly ModePalette PerformancePalette = ModePalette.From(
            Color.FromArgb(255, 90, 31), Color.FromArgb(255, 112, 56), Color.FromArgb(224, 74, 18),
            Color.FromArgb(20, 6, 0), Color.FromArgb(255, 138, 91), PerfA, PerfB);

        public static readonly ModePalette DailyPalette = ModePalette.From(
            Color.FromArgb(46, 155, 255), Color.FromArgb(74, 171, 255), Color.FromArgb(31, 134, 230),
            Color.FromArgb(2, 18, 32), Color.FromArgb(108, 187, 255), DailyA, DailyB);

        /// <summary>Ação de um aviso (ex.: "144 Hz" na linha do monitor): âmbar sólido, texto escuro.</summary>
        public static readonly ModePalette WarningPalette = ModePalette.From(
            Color.FromArgb(245, 184, 61), Color.FromArgb(248, 198, 96), Color.FromArgb(222, 162, 42),
            Color.FromArgb(28, 18, 0), Color.FromArgb(245, 184, 61), Color.FromArgb(245, 184, 61), Color.FromArgb(255, 150, 30));

        public static ModePalette Palette(Mode m)
        {
            return m == Mode.Performance ? PerformancePalette : DailyPalette;
        }

        /// <summary>
        /// A mesma paleta com as cores de acento mais transparentes (troca de modo: some até 40%, troca a paleta, volta).
        /// OnFill fica opaco: o texto sobre o botão continua legível no meio da troca.
        /// </summary>
        public static ModePalette Faded(ModePalette p, float opacity)
        {
            if (opacity >= 0.999f) return p;
            opacity = Math.Max(0f, opacity);
            p.Fill = Times(p.Fill, opacity);
            p.Hover = Times(p.Hover, opacity);
            p.Pressed = Times(p.Pressed, opacity);
            p.OnSurface = Times(p.OnSurface, opacity);
            p.Tint = Times(p.Tint, opacity);
            p.Stroke = Times(p.Stroke, opacity);
            p.SigA = Times(p.SigA, opacity);
            p.SigB = Times(p.SigB, opacity);
            return p;
        }

        private static Color Times(Color c, float opacity)
        {
            return Color.FromArgb((int)Math.Round(c.A * opacity), c.R, c.G, c.B);
        }

        /// <summary>
        /// Paleta pela cor de acento que o controle recebeu (PerfA/PerfB → Performance, DailyA/DailyB → Dia a dia,
        /// Warning → aviso). Uma cor no meio do caminho (transição de modo) fica com a paleta mais próxima: troca de uma
        /// vez na metade, sem passar por roxo ou verde.
        /// </summary>
        public static ModePalette PaletteFor(Color accent)
        {
            if (accent.IsEmpty) return DailyPalette;
            int perf = Math.Min(Distance(accent, PerfA), Math.Min(Distance(accent, PerfB), Distance(accent, PerformancePalette.Fill)));
            int daily = Math.Min(Distance(accent, DailyA), Math.Min(Distance(accent, DailyB), Distance(accent, DailyPalette.Fill)));
            int warn = Distance(accent, Warning);
            if (warn < perf && warn < daily) return WarningPalette;
            return perf <= daily ? PerformancePalette : DailyPalette;
        }

        private static int Distance(Color a, Color b)
        {
            int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return dr * dr + dg * dg + db * db;
        }

        // ---------- Escala de tipo (pt), raio e espaço (px a 96 dpi; a janela escala) ----------

        public static class TypeScale
        {
            public const float NumXL = 34f;
            public const float NumL = 20f;
            public const float NumM = 12.5f;
            public const float Hero = 20f;
            public const float H1 = 13f;
            public const float Group = 11f;
            public const float Body = 9.5f;
            public const float Button = 9.5f;
            public const float Caption = 8.5f;
            public const float Overline = 8f;
        }

        public static class Radius
        {
            public const float Xs = 4f;   // keycap, selo
            public const float Sm = 6f;   // botão, campo, nav, segmento, item de lista
            public const float Md = 10f;  // linha, grupo, tile, gráfico
            public const float Lg = 12f;  // cartão principal, readout
        }

        public static class Space
        {
            public const int S4 = 4;
            public const int S8 = 8;
            public const int S12 = 12;
            public const int S16 = 16;
            public const int S20 = 20;
            public const int S24 = 24;
            public const int S32 = 32;
            public const int WindowMargin = 20;
            public const int BetweenGroups = 16;
            public const int InsideGroup = 8;
            public const int HeightInline = 32;
            public const int HeightControl = 36;
            public const int HeightCta = 44;
        }

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

        /// <summary>Cor translúcida já misturada sobre um fundo opaco (onde a transparência não compõe, como num ListBox).</summary>
        public static Color Over(Color back, Color top)
        {
            return Lerp(Color.FromArgb(255, back.R, back.G, back.B), Color.FromArgb(255, top.R, top.G, top.B), top.A / 255f);
        }

        public static float Approach(float value, float target, float speed)
        {
            float next = value + (target - value) * speed;
            return Math.Abs(target - next) < 0.01f ? target : next;
        }

        // ---------- Fontes ----------

        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();
        private static string uiFamily, uiSemiFamily, titleFamily, numFamily, displayFamily;

        /// <summary>Primeira família instalada da lista; null se nenhuma.</summary>
        private static string FirstInstalled(params string[] families)
        {
            foreach (string f in families)
                if (Installed(f)) return f;
            return null;
        }

        /// <summary>
        /// Texto de interface: Segoe UI. Esta fonte também vai para Label e TextBox, que desenham pelo GDI, e o GDI não
        /// suporta a Segoe UI Variable: em 8 a 9 pt ela sai com o "i" sem pingo e o ":" amassado. As métricas são as
        /// mesmas, então a troca não mexe em nenhum layout.
        /// </summary>
        public static Font Ui(float size, FontStyle style = FontStyle.Regular)
        {
            if (uiFamily == null) uiFamily = "Segoe UI";
            return GetFont(uiFamily, size, style);
        }

        /// <summary>
        /// Semibold de interface: Segoe UI Variable Text Semibold (Windows 11), senão Segoe UI Semibold. Só para texto
        /// pintado pelo GDI+ (controles desenhados à mão); num Label (GDI) use Ui(size, FontStyle.Bold).
        /// </summary>
        public static Font UiSemibold(float size)
        {
            if (uiSemiFamily == null)
                uiSemiFamily = FirstInstalled("Segoe UI Variable Text Semibold", "Segoe UI Semibold") ?? "";
            return uiSemiFamily.Length > 0
                ? GetFont(uiSemiFamily, size, FontStyle.Regular)
                : GetFont("Segoe UI", size, FontStyle.Bold);
        }

        /// <summary>
        /// Títulos (nome do modo, título de página): Segoe UI Variable Display Semibold, senão Segoe UI Semibold.
        /// Como a UiSemibold, só para texto pintado pelo GDI+.
        /// </summary>
        public static Font Title(float size)
        {
            // O GDI corta nomes de família em 31 letras: esta instância da Variable às vezes só existe com o nome cortado.
            if (titleFamily == null)
                titleFamily = FirstInstalled("Segoe UI Variable Display Semibold", "Segoe UI Variable Display Semib", "Segoe UI Semibold") ?? "";
            return titleFamily.Length > 0
                ? GetFont(titleFamily, size, FontStyle.Regular)
                : GetFont("Segoe UI", size, FontStyle.Bold);
        }

        /// <summary>Números (leituras, chips, contagens): Bahnschrift SemiBold, senão Segoe UI Semibold.</summary>
        public static Font Num(float size)
        {
            if (numFamily == null)
                numFamily = FirstInstalled("Bahnschrift SemiBold") ?? "";
            if (numFamily.Length > 0) return GetFont(numFamily, size, FontStyle.Regular);
            return Installed("Bahnschrift") ? GetFont("Bahnschrift", size, FontStyle.Bold) : UiSemibold(size);
        }

        /// <summary>
        /// Números digitados num campo (TextBox, desenhado pelo GDI): Bahnschrift SemiBold, como as leituras; sem ela,
        /// Segoe UI em negrito (a UiSemibold é só para o GDI+).
        /// </summary>
        public static Font NumField(float size)
        {
            return Installed("Bahnschrift SemiBold") ? Num(size) : Ui(size, FontStyle.Bold);
        }

        /// <summary>
        /// Fonte de números e títulos curtos de antes (Bahnschrift). No estilo padrão (negrito) é a Num, SemiBold, mais
        /// leve que o Bold antigo; outros estilos seguem na Bahnschrift.
        /// </summary>
        public static Font Display(float size, FontStyle style = FontStyle.Bold)
        {
            if (style == FontStyle.Bold) return Num(size);
            if (displayFamily == null) displayFamily = Installed("Bahnschrift") ? "Bahnschrift" : "Segoe UI";
            return GetFont(displayFamily, size, style);
        }

        /// <summary>Fonte de largura fixa (registro e resumo do sistema), do mesmo cache: não precisa liberar.</summary>
        public static Font Mono(float size)
        {
            return GetFont("Consolas", size, FontStyle.Regular);
        }

        private static readonly Dictionary<string, bool> InstalledCache = new Dictionary<string, bool>();

        private static bool Installed(string family)
        {
            lock (InstalledCache)
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
        }

        private static Font GetFont(string family, float size, FontStyle style)
        {
            string key = family + "|" + size + "|" + (int)style;
            // Com trava: as páginas dos Ajustes medem texto na construção, e os testes constroem janelas em paralelo.
            lock (FontCache)
            {
                Font f;
                if (!FontCache.TryGetValue(key, out f))
                {
                    f = new Font(family, size, style, GraphicsUnit.Point);
                    FontCache[key] = f;
                }
                return f;
            }
        }

        // ---------- Rolagem escura ----------

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        /// <summary>
        /// Barra de rolagem escura (tema nativo DarkMode_Explorer, Windows 10 1809 em diante). Vale também para o
        /// controle que ainda não tem handle; em versões antigas do Windows fica a barra clara de sempre.
        /// </summary>
        public static void DarkScroll(Control control)
        {
            if (control == null) return;
            control.HandleCreated += (s, e) => ApplyDarkScroll(control);
            if (control.IsHandleCreated) ApplyDarkScroll(control);
        }

        private static void ApplyDarkScroll(Control control)
        {
            try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); } catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private const int EM_SETCUEBANNER = 0x1501;

        /// <summary>
        /// Texto de exemplo nativo da caixa de texto vazia (EM_SETCUEBANNER), que fica até a pessoa digitar. Vale também
        /// para a caixa que ainda não tem handle.
        /// </summary>
        public static void CueBanner(TextBox box, string text)
        {
            if (box == null) return;
            // wParam 1: o exemplo continua visível com o foco no campo vazio (some ao digitar).
            EventHandler apply = (s, e) => { try { SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text ?? ""); } catch { } };
            box.HandleCreated += apply;
            if (box.IsHandleCreated) apply(box, EventArgs.Empty);
        }

        // ---------- Medida de texto ----------

        private const TextFormatFlags WrapFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

        /// <summary>
        /// Linhas do texto quebrado na largura, só nos espaços (como o GDI de um Label: "%APPDATA%\MouseModes" não se
        /// parte no "%", como faria a quebra do GDI+). Uma palavra mais larga que a linha fica sozinha nela.
        /// </summary>
        public static List<string> WrapLines(Graphics g, string text, Font font, float width)
        {
            var lines = new List<string>();
            using (var format = new StringFormat(StringFormatFlags.NoWrap))
            {
                foreach (string paragraph in (text ?? "").Replace("\r", "").Split('\n'))
                {
                    string line = "";
                    foreach (string word in paragraph.Split(' '))
                    {
                        string candidate = line.Length == 0 ? word : line + " " + word;
                        if (line.Length > 0 && g.MeasureString(candidate, font, PointF.Empty, format).Width > width)
                        {
                            lines.Add(line);
                            line = word;
                        }
                        else
                        {
                            line = candidate;
                        }
                    }
                    lines.Add(line);
                }
            }
            return lines;
        }

        /// <summary>Parágrafo quebrado só nos espaços (WrapLines), centralizado na altura do retângulo.</summary>
        public static void DrawWrapped(Graphics g, string text, Font font, Color color, RectangleF r)
        {
            List<string> lines = WrapLines(g, text, font, r.Width);
            float lineH = font.GetHeight(g);
            float y = r.Y + Math.Max(0, (r.Height - lineH * lines.Count) / 2f);
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
            {
                foreach (string line in lines)
                {
                    g.DrawString(line, font, brush, new RectangleF(r.X, y, r.Width, lineH + 2), format);
                    y += lineH;
                }
            }
        }

        /// <summary>
        /// Altura (px a 96 dpi) do texto quebrado na largura dada (px a 96 dpi). Fica com a maior entre a medida do GDI
        /// (Label) e a do GDI+ (controles desenhados à mão), para caber nas duas. Serve para montar as telas na
        /// construção, antes da escala da janela.
        /// </summary>
        public static int TextHeight(string text, Font font, int width)
        {
            text = text ?? "";
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float k = g.DpiY / 96f;
                    int w = Math.Max(1, (int)(width * k));
                    Size gdi = TextRenderer.MeasureText(g, text, font, new Size(w, int.MaxValue), WrapFlags);
                    SizeF plus = g.MeasureString(text, font, w);
                    float wrapped = WrapLines(g, text, font, w).Count * font.GetHeight(g);
                    return (int)Math.Ceiling(Math.Max(Math.Max(gdi.Height, plus.Height), wrapped) / k) + 2;
                }
            }
            catch
            {
                return (int)Math.Ceiling(font.GetHeight() * 2) + 2;
            }
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

        /// <summary>
        /// Rótulo em caixa alta com tracking proporcional (0,06 em): o único estilo em caixa alta, no máximo um por tela.
        /// Retorna a largura desenhada.
        /// </summary>
        public static float DrawOverline(Graphics g, string text, Color color, float x, float y, float size = TypeScale.Overline)
        {
            string upper = (text ?? "").ToUpperInvariant();
            Font font = UiSemibold(size);
            float tracking = size * 96f / 72f * 0.06f * g.DpiX / 96f;
            DrawSpaced(g, upper, font, color, x, y, tracking);
            return MeasureSpaced(g, upper, font, tracking).Width;
        }

        /// <summary>
        /// Anel de foco do teclado (2 px). Um controle não pinta fora dos próprios limites: passe o retângulo externo
        /// dele (o anel fica por cima da borda).
        /// </summary>
        public static void DrawFocusRing(Graphics g, RectangleF r, float radius, Color color)
        {
            StrokeRounded(g, RectangleF.Inflate(r, -1f, -1f), Math.Max(1f, radius - 1f), color, 2f);
        }

        // ---------- Chips de informação (cartão do modo e medidores) ----------

        /// <summary>
        /// Faixa única com divisórias verticais: em cada coluna, o valor na fonte de números (Text) e o rótulo em
        /// sentence case logo abaixo (TextSecondary). band = fundo da faixa (Surface por padrão; Surface2 sobre um cartão).
        /// k multiplica as medidas internas (respiros, raio, traços) para desenhar num bitmap de dpi maior (MatchShare);
        /// as fontes já seguem o dpi do Graphics.
        /// </summary>
        public static void DrawChips(Graphics g, RectangleF area, string[] labels, string[] values, Color accent, Color? band = null,
                                     float valueSize = TypeScale.NumM, float k = 1f)
        {
            int n = labels.Length;
            if (n == 0) return;
            FillRounded(g, area, Radius.Md * k, band ?? Surface);
            StrokeRounded(g, area, Radius.Md * k, Border, k);
            float w = area.Width / n;
            Font valueFont = Num(valueSize);
            Font labelFont = Ui(TypeScale.Caption);
            float valueH = (float)Math.Ceiling(valueFont.GetHeight(g));
            float labelH = (float)Math.Ceiling(labelFont.GetHeight(g));
            float top = area.Y + Math.Max(2f * k, (area.Height - valueH - labelH + 2 * k) / 2f);
            using (var divider = new Pen(Divider, k))
            {
                for (int i = 0; i < n; i++)
                {
                    float x = area.X + i * w;
                    if (i > 0) g.DrawLine(divider, x, area.Y + 9 * k, x, area.Bottom - 9 * k);
                    var col = new RectangleF(x + 12 * k, top, w - 16 * k, valueH);
                    string value = values != null && i < values.Length ? values[i] : "";
                    // Sem leitura ("—"): apagado, como o "—" do número grande.
                    bool empty = value.Trim().Length == 0 || value.Trim() == "—";
                    DrawText(g, value, valueFont, empty ? TextTertiary : Text, col, wrap: false);
                    DrawText(g, labels[i], labelFont, TextSecondary, new RectangleF(x + 12 * k, top + valueH - 2 * k, w - 16 * k, labelH), wrap: false);
                }
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

                    case GlyphKind.Target:
                        g.DrawEllipse(pen, x + w * 0.14f, y + h * 0.14f, w * 0.72f, h * 0.72f);
                        g.FillEllipse(brush, x + w * 0.42f, y + h * 0.42f, w * 0.16f, h * 0.16f);
                        g.DrawLine(pen, x + w * 0.50f, y, x + w * 0.50f, y + h * 0.24f);
                        g.DrawLine(pen, x + w * 0.50f, y + h * 0.76f, x + w * 0.50f, y + h);
                        g.DrawLine(pen, x, y + h * 0.50f, x + w * 0.24f, y + h * 0.50f);
                        g.DrawLine(pen, x + w * 0.76f, y + h * 0.50f, x + w, y + h * 0.50f);
                        break;

                    case GlyphKind.Shield:
                        using (var shield = new GraphicsPath())
                        {
                            shield.AddLine(x + w * 0.50f, y + h * 0.04f, x + w * 0.88f, y + h * 0.18f);
                            shield.AddBezier(x + w * 0.88f, y + h * 0.18f, x + w * 0.88f, y + h * 0.62f, x + w * 0.70f, y + h * 0.82f, x + w * 0.50f, y + h * 0.96f);
                            shield.AddBezier(x + w * 0.50f, y + h * 0.96f, x + w * 0.30f, y + h * 0.82f, x + w * 0.12f, y + h * 0.62f, x + w * 0.12f, y + h * 0.18f);
                            shield.CloseFigure();
                            g.DrawPath(pen, shield);
                        }
                        g.DrawLine(pen, x + w * 0.34f, y + h * 0.50f, x + w * 0.46f, y + h * 0.62f);
                        g.DrawLine(pen, x + w * 0.46f, y + h * 0.62f, x + w * 0.68f, y + h * 0.38f);
                        break;

                    case GlyphKind.Sliders:
                        // Duas linhas com um botão vazado em cada: a linha para na borda do botão.
                        for (int i = 0; i < 2; i++)
                        {
                            float ly = y + h * (i == 0 ? 0.28f : 0.72f);
                            float kx = x + w * (i == 0 ? 0.34f : 0.66f);
                            float kr = w * 0.14f;
                            g.DrawLine(pen, x + w * 0.06f, ly, kx - kr, ly);
                            g.DrawLine(pen, kx + kr, ly, x + w * 0.94f, ly);
                            g.DrawEllipse(pen, kx - kr, ly - kr, kr * 2, kr * 2);
                        }
                        break;

                    case GlyphKind.Ruler:
                        using (GraphicsPath ruler = Rounded(new RectangleF(x + w * 0.04f, y + h * 0.30f, w * 0.92f, h * 0.40f), w * 0.06f))
                            g.DrawPath(pen, ruler);
                        for (int i = 1; i <= 5; i++)
                        {
                            float tx = x + w * (0.04f + 0.92f * i / 6f);
                            g.DrawLine(pen, tx, y + h * 0.30f, tx, y + h * (i % 2 == 0 ? 0.52f : 0.44f));
                        }
                        break;

                    case GlyphKind.Chart:
                        g.DrawLine(pen, x + w * 0.10f, y + h * 0.90f, x + w * 0.90f, y + h * 0.90f);
                        g.DrawLine(pen, x + w * 0.26f, y + h * 0.74f, x + w * 0.26f, y + h * 0.50f);
                        g.DrawLine(pen, x + w * 0.50f, y + h * 0.74f, x + w * 0.50f, y + h * 0.14f);
                        g.DrawLine(pen, x + w * 0.74f, y + h * 0.74f, x + w * 0.74f, y + h * 0.36f);
                        break;

                    case GlyphKind.Clock:
                        g.DrawEllipse(pen, x + w * 0.08f, y + h * 0.08f, w * 0.84f, h * 0.84f);
                        g.DrawLine(pen, x + w * 0.50f, y + h * 0.26f, x + w * 0.50f, y + h * 0.52f);
                        g.DrawLine(pen, x + w * 0.50f, y + h * 0.52f, x + w * 0.68f, y + h * 0.62f);
                        break;

                    case GlyphKind.Alert:
                        g.DrawLine(pen, x + w * 0.50f, y + h * 0.10f, x + w * 0.50f, y + h * 0.60f);
                        g.FillEllipse(brush, x + w * 0.50f - stroke * 0.75f, y + h * 0.84f - stroke * 0.75f, stroke * 1.5f, stroke * 1.5f);
                        break;

                    case GlyphKind.Check:
                        g.DrawLines(pen, new[]
                        {
                            new PointF(x + w * 0.12f, y + h * 0.54f),
                            new PointF(x + w * 0.40f, y + h * 0.80f),
                            new PointF(x + w * 0.88f, y + h * 0.24f)
                        });
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
