using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>Base dos controles desenhados à mão: buffer duplo e animação de hover.</summary>
    public class GControl : Control
    {
        protected bool Hovered;
        protected bool Pressed;
        protected float HoverT;
        private readonly Timer anim;

        public GControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            anim = new Timer { Interval = 15 };
            anim.Tick += (s, e) =>
            {
                bool more = StepAnimations();
                Invalidate();
                if (!more) anim.Stop();
            };
        }

        protected virtual bool Interactive { get { return true; } }

        /// <summary>Atualiza animações; retorna true enquanto ainda houver movimento.</summary>
        protected virtual bool StepAnimations()
        {
            float target = Hovered ? 1f : 0f;
            HoverT = Theme.Approach(HoverT, target, 0.18f);
            return HoverT != target;
        }

        protected void Animate()
        {
            anim.Start();
        }

        protected Color ParentBack
        {
            get { return Parent != null ? Parent.BackColor : Theme.Bg; }
        }

        protected Graphics Begin(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            Theme.HighQuality(g);
            return g;
        }

        /// <summary>Foco vindo do teclado (Tab): só aí o anel de foco aparece.</summary>
        protected bool KeyboardFocus
        {
            get { return Focused && ShowFocusCues; }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (!Interactive) return;
            Hovered = true;
            Animate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!Interactive) return;
            Hovered = false;
            Pressed = false;
            Animate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Interactive || e.Button != MouseButtons.Left) return;
            Pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Pressed) return;
            Pressed = false;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) anim.Dispose();
            base.Dispose(disposing);
        }
    }

    // =====================================================================

    /// <summary>Barra de título própria: arrastar, minimizar, fechar.</summary>
    public class TitleBar : GControl
    {
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int ButtonWidth = 46;
        private int hoverButton = -1; // 0 = fechar, 1 = minimizar

        public string Title { get; set; }
        public bool ShowMinimize { get; set; }
        public Color AccentA { get; set; }
        public Color AccentB { get; set; }
        /// <summary>Opacidade do logo (0 a 1): a troca de modo some e volta, sem passar por outra cor.</summary>
        public float AccentOpacity { get; set; }

        public event Action CloseClicked;
        public event Action MinimizeClicked;

        public TitleBar()
        {
            Title = "MouseModes";
            AccentA = Theme.DailyA;
            AccentB = Theme.DailyB;
            AccentOpacity = 1f;
            Height = 44;
        }

        private Rectangle CloseRect { get { return new Rectangle(Width - ButtonWidth, 0, ButtonWidth, Height); } }
        private Rectangle MinRect { get { return new Rectangle(Width - ButtonWidth * 2, 0, ButtonWidth, Height); } }

        private int HitButton(Point p)
        {
            if (CloseRect.Contains(p)) return 0;
            if (ShowMinimize && MinRect.Contains(p)) return 1;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = HitButton(e.Location);
            if (hit != hoverButton)
            {
                hoverButton = hit;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverButton = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || HitButton(e.Location) >= 0) return;
            Form form = FindForm();
            if (form == null) return;
            try
            {
                ReleaseCapture();
                SendMessage(form.Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
            }
            catch { }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            int hit = HitButton(e.Location);
            Action handler = hit == 0 ? CloseClicked : hit == 1 ? MinimizeClicked : null;
            if (handler != null) handler();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);

            // Logo: mouse dentro de um quadrado com o gradiente de assinatura do modo
            ModePalette p = Theme.Faded(Theme.PaletteFor(AccentA), AccentOpacity);
            var logo = new RectangleF(16, (Height - 24) / 2f, 24, 24);
            using (LinearGradientBrush lb = Theme.Gradient(logo, p.SigA, p.SigB, 45f))
                Theme.FillRounded(g, logo, Theme.Radius.Sm + 1, lb);
            using (var white = new SolidBrush(Color.White))
                Theme.DrawGlyph(g, GlyphKind.Mouse, RectangleF.Inflate(logo, -6, -5), white, 1.6f);

            float buttons = ButtonWidth * (ShowMinimize ? 2 : 1);
            Theme.DrawText(g, Title, Theme.UiSemibold(Theme.TypeScale.Button), Theme.TextSecondary,
                new RectangleF(50, 0, Math.Max(1, Width - 50 - buttons - 8), Height), StringAlignment.Near, StringAlignment.Center, false);

            // Botões da janela
            if (hoverButton == 0)
                using (var b = new SolidBrush(Theme.DangerSolid)) g.FillRectangle(b, CloseRect);
            using (var b = new SolidBrush(hoverButton == 0 ? Color.White : Theme.TextSecondary))
            {
                Rectangle c = CloseRect;
                Theme.DrawGlyph(g, GlyphKind.Close, new RectangleF(c.X + c.Width / 2f - 5, c.Y + c.Height / 2f - 5, 10, 10), b, 1.4f);
            }

            if (ShowMinimize)
            {
                if (hoverButton == 1)
                    using (var b = new SolidBrush(Theme.SurfaceHover)) g.FillRectangle(b, MinRect);
                using (var b = new SolidBrush(hoverButton == 1 ? Theme.Text : Theme.TextSecondary))
                {
                    Rectangle m = MinRect;
                    Theme.DrawGlyph(g, GlyphKind.Minimize, new RectangleF(m.X + m.Width / 2f - 5, m.Y + m.Height / 2f - 5, 10, 10), b, 1.4f);
                }
            }

            // Divisória inferior neutra
            using (var line = new SolidBrush(Theme.Divider))
                g.FillRectangle(line, 0, Height - 1, Width, 1);
        }
    }

    // =====================================================================

    public enum ButtonStyle
    {
        /// <summary>Cor sólida do modo (Fill) com texto escuro (OnFill): a ação principal da tela.</summary>
        Primary,
        /// <summary>Neutro: Surface2 com borda. É o padrão (Secondary é o mesmo estilo).</summary>
        Ghost,
        /// <summary>Destrutivo em repouso já vermelho: fundo translúcido, borda e texto de perigo.</summary>
        Danger,
        /// <summary>Confirmação destrutiva: vermelho sólido com texto branco.</summary>
        DangerSolid,
        /// <summary>Só texto na cor do modo, sem caixa (ação leve, como um link).</summary>
        Tertiary,
        /// <summary>Ação destrutiva rara (Sair): texto discreto em repouso, vermelho só no hover.</summary>
        QuietDanger,
        Secondary = Ghost
    }

    /// <summary>Botão do tema (primário, secundário, terciário, perigo).</summary>
    public class NeonButton : GControl
    {
        public ButtonStyle Style { get; set; }
        public Color AccentA { get; set; }
        public Color AccentB { get; set; }
        public GlyphKind Glyph { get; set; }

        /// <summary>Paleta explícita (ex.: a de aviso); null = a do modo de AccentA.</summary>
        public ModePalette? PaletteOverride { get; set; }

        /// <summary>Opacidade das cores do modo (0 a 1), para a troca de modo sumir e voltar sem misturar as cores.</summary>
        public float AccentOpacity { get; set; }

        public NeonButton()
        {
            Style = ButtonStyle.Ghost;
            AccentA = Theme.DailyA;
            AccentB = Theme.DailyB;
            AccentOpacity = 1f;
            Cursor = Cursors.Hand;
            Size = new Size(140, 44);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        // Espaço e Enter acionam o botão com foco, como num botão do Windows.
        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space && !Pressed)
            {
                Pressed = true;
                Invalidate();
            }
            else if (e.KeyCode == Keys.Enter && Enabled)
            {
                e.Handled = true;
                OnClick(EventArgs.Empty);
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode != Keys.Space || !Pressed) return;
            Pressed = false;
            Invalidate();
            e.Handled = true;
            if (Enabled) OnClick(EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            // O botão ocupa o tamanho declarado (36/44 px de fato): as bordas batem com as dos cartões ao lado.
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            const float radius = Theme.Radius.Sm;
            ModePalette p = Theme.Faded(PaletteOverride ?? Theme.PaletteFor(AccentA), AccentOpacity);
            bool enabled = Enabled;
            float hover = enabled ? HoverT : 0f;
            bool pressed = enabled && Pressed;
            Color textColor, iconColor;

            if (!enabled)
            {
                if (Style != ButtonStyle.Tertiary && Style != ButtonStyle.QuietDanger)
                {
                    Theme.FillRounded(g, r, radius, Theme.Surface2);
                    Theme.StrokeRounded(g, r, radius, Theme.Border, 1f);
                }
                textColor = iconColor = Theme.Alpha(Theme.TextTertiary, 153);
            }
            else
            {
                switch (Style)
                {
                    case ButtonStyle.Primary:
                        Theme.FillRounded(g, r, radius, pressed ? p.Pressed : Theme.Lerp(p.Fill, p.Hover, hover));
                        textColor = iconColor = p.OnFill;
                        break;

                    case ButtonStyle.Danger:
                        Theme.FillRounded(g, r, radius, Theme.Alpha(Theme.Danger, pressed ? 52 : 22 + (int)(18 * hover)));
                        Theme.StrokeRounded(g, r, radius, Theme.Lerp(Theme.Alpha(Theme.Danger, 110), Theme.Danger, hover), 1.2f);
                        textColor = iconColor = Theme.DangerText;
                        break;

                    case ButtonStyle.DangerSolid:
                        Theme.FillRounded(g, r, radius, pressed ? Theme.Lerp(Theme.DangerSolid, Color.Black, 0.1f)
                                                                : Theme.Lerp(Theme.DangerSolid, Theme.Danger, hover * 0.5f));
                        textColor = iconColor = Color.White;
                        break;

                    case ButtonStyle.Tertiary:
                        if (hover > 0.01f) Theme.FillRounded(g, r, radius, Theme.Alpha(Color.White, (int)(10 * hover)));
                        textColor = iconColor = p.OnSurface;
                        break;

                    case ButtonStyle.QuietDanger:
                        if (hover > 0.01f || pressed) Theme.FillRounded(g, r, radius, Theme.Alpha(Theme.Danger, pressed ? 40 : (int)(22 * hover)));
                        textColor = Theme.Lerp(Theme.TextSecondary, Theme.DangerText, hover);
                        iconColor = Theme.Lerp(Theme.TextTertiary, Theme.DangerText, hover);
                        break;

                    default: // Ghost / Secondary
                        Color fill = pressed ? Theme.Lerp(Theme.Surface2, Color.Black, 0.1f) : Theme.Lerp(Theme.Surface2, Theme.SurfaceHover, hover);
                        Theme.FillRounded(g, r, radius, fill);
                        Theme.StrokeRounded(g, r, radius, Theme.Lerp(Theme.Border, Theme.BorderStrong, hover), 1f);
                        textColor = Theme.Text;
                        iconColor = Theme.Lerp(Theme.TextSecondary, Theme.Text, hover);
                        break;
                }
            }

            string text = Text ?? "";
            Font font = Style == ButtonStyle.QuietDanger ? Theme.Ui(9f) : Theme.UiSemibold(Theme.TypeScale.Button);
            SizeF ts;
            using (var format = new StringFormat(StringFormatFlags.NoWrap)) ts = g.MeasureString(text, font, PointF.Empty, format);
            float iconSize = Glyph == GlyphKind.None ? 0 : Style == ButtonStyle.QuietDanger ? 14 : 16;
            float gap = iconSize > 0 && text.Length > 0 ? (Style == ButtonStyle.QuietDanger ? 6 : 8) : 0;
            float room = r.Width - 16;
            float textW = Math.Min(ts.Width, Math.Max(1, room - iconSize - gap));
            float total = textW + iconSize + gap;
            float x = r.X + (r.Width - total) / 2f;
            float shift = pressed ? 1 : 0;

            if (iconSize > 0)
            {
                using (var ib = new SolidBrush(iconColor))
                    Theme.DrawGlyph(g, Glyph, new RectangleF(x, r.Y + (r.Height - iconSize) / 2f + shift, iconSize, iconSize), ib, 1.6f);
                x += iconSize + gap;
            }
            if (text.Length > 0)
                Theme.DrawText(g, text, font, textColor, new RectangleF(x, r.Y + shift, textW + 2, r.Height), StringAlignment.Near, StringAlignment.Center, false);

            // Anel de foco por dentro da borda, na cor do texto (contrasta com o preenchimento de qualquer estilo).
            if (KeyboardFocus) Theme.StrokeRounded(g, RectangleF.Inflate(r, -2.5f, -2.5f), radius - 2f, enabled ? textColor : p.OnSurface, 1.5f);
        }
    }

    // =====================================================================

    /// <summary>
    /// Cartão do modo atual (Carbon): superfície neutra, barra de assinatura à esquerda, uma luz ambiente da cor do
    /// modo, ícone num quadrado sólido, motivo numa pílula e os valores numa faixa de chips.
    /// </summary>
    public class HeroCard : GControl
    {
        public const float Inset = 8f;

        public Color AccentA { get; set; }
        public Color AccentB { get; set; }
        /// <summary>Opacidade das cores do modo (0 a 1): a troca de modo some e volta, sem misturar as cores.</summary>
        public float AccentOpacity { get; set; }
        public string ModeName { get; set; }
        public string Reason { get; set; }
        public GlyphKind Glyph { get; set; }
        public string[] StatLabels { get; set; }
        public string[] StatValues { get; set; }

        public HeroCard()
        {
            AccentA = Theme.DailyA;
            AccentB = Theme.DailyB;
            AccentOpacity = 1f;
            ModeName = L.T("Dia a dia", "Daily");
            Reason = "";
            Glyph = GlyphKind.Monitor;
            StatLabels = new string[0];
            StatValues = new string[0];
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            float k = g.DpiY / 96f; // espaços internos acompanham a escala do Windows, como as fontes
            var card = new RectangleF(Inset, Inset, Width - Inset * 2, Height - Inset * 2);
            const float radius = Theme.Radius.Lg;
            ModePalette p = Theme.Faded(Theme.PaletteFor(AccentA), AccentOpacity);

            Theme.FillRounded(g, card, radius, Theme.Surface);

            using (GraphicsPath clip = Theme.Rounded(card, radius))
            {
                // Save/Restore em vez de guardar o g.Clip: aquele Region nunca era liberado.
                GraphicsState state = g.Save();
                g.SetClip(clip, CombineMode.Intersect);

                // Uma única luz ambiente da cor do modo, no canto superior direito
                var light = new RectangleF(card.Right - card.Width * 0.62f, card.Y - card.Height * 0.75f, card.Width * 0.95f, card.Height * 1.5f);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(light);
                    using (var pb = new PathGradientBrush(path) { CenterColor = Theme.Alpha(p.Fill, (int)(48 * AccentOpacity)), SurroundColors = new[] { Theme.Alpha(p.Fill, 0) } })
                        g.FillEllipse(pb, light);
                }

                // Realce de 1 px no topo e a barra de assinatura de 3 px à esquerda
                using (var hl = new SolidBrush(Theme.Alpha(Color.White, 6))) g.FillRectangle(hl, card.X, card.Y + 1, card.Width, 1);
                var bar = new RectangleF(card.X, card.Y, 3 * k, card.Height);
                using (LinearGradientBrush lb = Theme.Gradient(bar, p.SigA, p.SigB, 90f)) g.FillRectangle(lb, bar);
                g.Restore(state);
            }
            // Contorno de 1 px na cor do modo, discreto: o cartão do modo atual é o primeiro a ser lido.
            Theme.StrokeRounded(g, card, radius, Theme.Lerp(Theme.Border, Theme.Alpha(p.Fill, 90), AccentOpacity), 1f);

            float pad = 20 * k;
            float left = card.X + pad;

            // Ícone 40 px num quadrado sólido do modo, glifo escuro (OnFill), sem halo
            float icon = 40 * k;
            var iconBox = new RectangleF(card.Right - pad - icon, card.Y + pad, icon, icon);
            Theme.FillRounded(g, iconBox, Theme.Radius.Md, p.Fill);
            using (var gb = new SolidBrush(p.OnFill))
                Theme.DrawGlyph(g, Glyph, RectangleF.Inflate(iconBox, -11 * k, -11 * k), gb, 2f * k);
            float textW = iconBox.X - 12 * k - left;

            // Overline, nome do modo e o motivo, de cima para baixo pelas alturas das fontes
            Font overFont = Theme.UiSemibold(Theme.TypeScale.Overline);
            float y = card.Y + 18 * k;
            Theme.DrawOverline(g, L.T("Modo atual", "Current mode"), Theme.TextSecondary, left, y);
            y += (float)Math.Ceiling(overFont.GetHeight(g)) + 1 * k;

            Font big = Theme.Title(Theme.TypeScale.Hero);
            float bigH = (float)Math.Ceiling(big.GetHeight(g));
            Theme.DrawText(g, ModeName, big, Theme.Text, new RectangleF(left - 2 * k, y, textW, bigH), wrap: false);
            y += bigH + 6 * k;

            if (!string.IsNullOrEmpty(Reason))
            {
                Font rf = Theme.Ui(9f);
                SizeF rs;
                using (var format = new StringFormat(StringFormatFlags.NoWrap)) rs = g.MeasureString(Reason, rf, PointF.Empty, format);
                float pillH = Math.Max(24 * k, (float)Math.Ceiling(rf.GetHeight(g)) + 8 * k);
                var pill = new RectangleF(left, y, Math.Min(card.Right - pad - left, rs.Width + 20 * k), pillH);
                Theme.FillRounded(g, pill, pill.Height / 2f, p.Tint);
                Theme.DrawText(g, Reason, rf, p.OnSurface, new RectangleF(pill.X + 10 * k, pill.Y, pill.Width - 14 * k, pill.Height), StringAlignment.Near, StringAlignment.Center, false);
            }

            // Chips: a faixa de valores no pé do cartão
            float chipsH = Math.Max(50 * k, (float)Math.Ceiling(Theme.Num(Theme.TypeScale.NumM).GetHeight(g) + Theme.Ui(Theme.TypeScale.Caption).GetHeight(g)) + 12 * k);
            var chips = new RectangleF(left, card.Bottom - 16 * k - chipsH, card.Right - pad - left, chipsH);
            Theme.DrawChips(g, chips, StatLabels, StatValues, p.Fill, Theme.Surface2);
        }
    }

    // =====================================================================

    /// <summary>Botão grande de modo: neutro quando inativo; ativo com a tinta do modo, contorno e o selo "Ativo".</summary>
    public class ModeTile : GControl
    {
        public const float Inset = 8f;

        private bool active;
        private float activeT;

        public string Title { get; set; }
        public string Subtitle { get; set; }
        public GlyphKind Glyph { get; set; }
        public Color AccentA { get; set; }
        public Color AccentB { get; set; }

        public ModeTile()
        {
            Title = "";
            Subtitle = "";
            Cursor = Cursors.Hand;
        }

        public bool Active
        {
            get { return active; }
            set { SetActive(value, true); }
        }

        /// <summary>Troca o estado; animate = false (ou janela ainda fechada) já mostra o estado final, sem transição.</summary>
        public void SetActive(bool value, bool animate)
        {
            if (active == value) return;
            active = value;
            if (!animate || !IsHandleCreated || !Visible)
            {
                activeT = active ? 1f : 0f;
                Invalidate();
            }
            else
            {
                Animate();
            }
        }

        protected override bool StepAnimations()
        {
            bool more = base.StepAnimations();
            float target = active ? 1f : 0f;
            activeT = Theme.Approach(activeT, target, 0.16f);
            return more || activeT != target;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            float k = g.DpiY / 96f;
            var r = new RectangleF(Inset, Inset, Width - Inset * 2, Height - Inset * 2);
            if (Pressed) r.Offset(0, 1);
            const float radius = Theme.Radius.Md;
            ModePalette p = Theme.PaletteFor(AccentA);

            // Inativo: superfície neutra. Ativo: tinta do modo e contorno de 1,5 px, sem brilho externo.
            Theme.FillRounded(g, r, radius, Theme.Surface);
            if (activeT > 0.01f) Theme.FillRounded(g, r, radius, Theme.Alpha(p.Fill, (int)(p.Tint.A * activeT)));
            Color idleBorder = Theme.Lerp(Theme.Border, Theme.BorderStrong, HoverT);
            Theme.StrokeRounded(g, r, radius, Theme.Lerp(idleBorder, Theme.Lerp(p.Stroke, p.Fill, HoverT * 0.5f), activeT), 1f + 0.5f * activeT);

            // Ícone: quadrado neutro; no ativo, o mesmo quadrado sólido do cartão do modo (Fill com o glifo OnFill)
            float icon = 40 * k;
            var iconBox = new RectangleF(r.X + 16 * k, r.Y + (r.Height - icon) / 2f, icon, icon);
            Theme.FillRounded(g, iconBox, Theme.Radius.Md, Theme.Lerp(Theme.Surface2, p.Fill, activeT));
            RectangleF glyph = RectangleF.Inflate(iconBox, -11 * k, -11 * k);
            using (var ib = new SolidBrush(Theme.Lerp(Theme.Lerp(Theme.TextSecondary, Theme.Text, HoverT), p.OnFill, activeT)))
                Theme.DrawGlyph(g, Glyph, glyph, ib, 2f * k);

            // Selo "Ativo" no canto de cima, à direita (o título fica no meio da altura, abaixo dele)
            if (activeT > 0.01f)
            {
                string badge = L.T("Ativo", "Active");
                Font bf = Theme.UiSemibold(Theme.TypeScale.Overline);
                SizeF bs;
                using (var format = new StringFormat(StringFormatFlags.NoWrap)) bs = g.MeasureString(badge, bf, PointF.Empty, format);
                float bh = Math.Max(18 * k, (float)Math.Ceiling(bf.GetHeight(g)) + 4 * k);
                var pill = new RectangleF(r.Right - 8 * k - (bs.Width + 12 * k), r.Y + 8 * k, bs.Width + 12 * k, bh);
                Theme.FillRounded(g, pill, bh / 2f, Theme.Alpha(p.Fill, (int)(36 * activeT)));
                Theme.DrawText(g, badge, bf, Theme.Alpha(p.OnSurface, (int)(255 * activeT)), pill, StringAlignment.Center, StringAlignment.Center, false);
            }

            float textX = iconBox.Right + 12 * k;
            Font titleFont = Theme.Title(11.5f);
            Font subFont = Theme.Ui(Theme.TypeScale.Caption);
            float titleH = (float)Math.Ceiling(titleFont.GetHeight(g));
            float subH = (float)Math.Ceiling(subFont.GetHeight(g));
            float top = r.Y + (r.Height - titleH - subH + 2 * k) / 2f;
            Theme.DrawText(g, Title, titleFont, Theme.Text, new RectangleF(textX, top, r.Right - textX - 8 * k, titleH), wrap: false);
            Theme.DrawText(g, Subtitle, subFont, Theme.TextSecondary,
                new RectangleF(textX, top + titleH - 2 * k, r.Right - textX - 8 * k, subH + 2), wrap: false);
        }
    }

    // =====================================================================

    /// <summary>Chave liga/desliga com animação.</summary>
    public class ToggleSwitch : GControl
    {
        private bool isOn;
        private float knobT;

        public Color AccentA { get; set; }
        public Color AccentB { get; set; }
        /// <summary>Opacidade da cor do modo (0 a 1), para a troca de modo.</summary>
        public float AccentOpacity { get; set; }

        public event Action CheckedChanged;

        public ToggleSwitch()
        {
            AccentA = Theme.DailyA;
            AccentB = Theme.DailyB;
            AccentOpacity = 1f;
            Size = new Size(40, 22);
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return isOn; }
            set { SetChecked(value, true); }
        }

        public void SetChecked(bool value, bool raise)
        {
            if (isOn == value) return;
            isOn = value;
            // Só o clique desliza a chave. Valor vindo de fora (config ao abrir, menu da bandeja) já aparece no lugar:
            // a janela não abre com as chaves no meio do caminho.
            if (!raise || !IsHandleCreated || !Visible)
            {
                knobT = isOn ? 1f : 0f;
                Invalidate();
            }
            else
            {
                Animate();
            }
            if (raise && CheckedChanged != null) CheckedChanged();
        }

        public void Toggle()
        {
            Checked = !Checked;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Toggle();
        }

        protected override bool StepAnimations()
        {
            bool more = base.StepAnimations();
            float target = isOn ? 1f : 0f;
            knobT = Theme.Approach(knobT, target, 0.22f);
            return more || knobT != target;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            ModePalette p = Theme.Faded(Theme.PaletteFor(AccentA), AccentOpacity);
            var track = new RectangleF(1, 1, Width - 2, Height - 2);
            float radius = track.Height / 2f;

            // Desligado: Surface2 com borda forte. Ligado: a cor sólida do modo.
            Theme.FillRounded(g, track, radius, Theme.Lerp(Theme.Surface2, Theme.Lerp(p.Fill, p.Hover, HoverT), knobT));
            if (knobT < 0.99f)
                Theme.StrokeRounded(g, track, radius, Theme.Alpha(Theme.Lerp(Theme.BorderStrong, Theme.TextTertiary, HoverT * 0.5f), (int)(255 * (1 - knobT))), 1f);

            // Botão: 12 px (desligado) a 14 px (ligado), mais 2 px no hover
            float d = 12 + 2 * knobT + 2 * HoverT;
            float pad = (track.Height - d) / 2f;
            float x = track.X + pad + (track.Width - pad * 2 - d) * knobT;
            using (var kb = new SolidBrush(Theme.Lerp(Theme.TextTertiary, p.OnFill, knobT)))
                g.FillEllipse(kb, x, track.Y + pad, d, d);
            if (KeyboardFocus) Theme.DrawFocusRing(g, new RectangleF(0, 0, Width, Height), radius + 1, p.OnSurface);
        }
    }

    // =====================================================================

    /// <summary>Linha com título, subtítulo (com ponto de status opcional) e uma chave à direita.</summary>
    public class ToggleRow : GControl
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public Color DotColor { get; set; }
        public float DotPulse { get; set; }
        public ToggleSwitch Switch { get; private set; }

        public ToggleRow()
        {
            Title = "";
            Subtitle = "";
            DotColor = Color.Empty;
            BackColor = Theme.Panel;
            Cursor = Cursors.Hand;
            Switch = new ToggleSwitch();
            Controls.Add(Switch);
        }

        public Color Accent
        {
            set
            {
                Switch.AccentA = value;
                Switch.AccentB = Theme.Lerp(value, Color.White, 0.25f);
                Switch.Invalidate();
            }
        }

        public void SetAccents(Color a, Color b, float opacity = 1f)
        {
            Switch.AccentA = a;
            Switch.AccentB = b;
            Switch.AccentOpacity = opacity;
            Switch.Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            Switch.Location = new Point(Width - Switch.Width - 16, (Height - Switch.Height) / 2);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Switch.Toggle();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.HighQuality(g);

            GraphicsPath shape;
            if (SettingsGroup.PaintBackdrop(g, this, out shape))
            {
                // Dentro de um grupo: sem cartão próprio, só o realce do hover (recortado nos cantos do grupo).
                using (shape)
                {
                    if (HoverT > 0.01f)
                    {
                        GraphicsState state = g.Save();
                        g.SetClip(shape, CombineMode.Intersect);
                        using (var hb = new SolidBrush(Theme.Alpha(Color.White, (int)(4 * HoverT + 0.5f))))
                            g.FillRectangle(hb, 0, 0, Width, Height);
                        g.Restore(state);
                    }
                }
            }
            else
            {
                g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
                var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
                Theme.FillRounded(g, r, Theme.Radius.Md, Theme.Surface);
                if (HoverT > 0.01f) Theme.FillRounded(g, r, Theme.Radius.Md, Theme.Alpha(Color.White, (int)(4 * HoverT + 0.5f)));
                Theme.StrokeRounded(g, r, Theme.Radius.Md, Theme.Lerp(Theme.Border, Theme.BorderStrong, HoverT), 1f);
            }

            float textW = Width - Switch.Width - 48;
            bool hasSub = !string.IsNullOrEmpty(Subtitle);
            Font titleFont = Theme.UiSemibold(10f), subFont = Theme.Ui(Theme.TypeScale.Caption);
            // Altura das linhas pela fonte: a 100% dá os 22 e 18 px de sempre; com a escala do Windows alta, cresce
            // junto com o texto e não corta as letras que descem (g, p, q).
            float titleH = Math.Max(22f, (float)Math.Ceiling(titleFont.GetHeight(g)));
            float subH = Math.Max(18f, (float)Math.Ceiling(subFont.GetHeight(g)));
            float titleY = hasSub
                ? Height / 2f - 19 - (titleH - 22 + subH - 18) / 2f
                : Height / 2f - titleH / 2f + 1;
            Theme.DrawText(g, Title, titleFont, Theme.Text, new RectangleF(16, titleY, textW, titleH), wrap: false);

            if (hasSub)
            {
                float subY = titleY + titleH - 1;
                float subX = 17;
                if (DotColor != Color.Empty)
                {
                    float dotY = subY + subH / 2f - 3.5f; // centro da linha do subtítulo
                    if (DotPulse > 0.01f)
                        using (var halo = new SolidBrush(Theme.Alpha(DotColor, (int)(50 * DotPulse))))
                            g.FillEllipse(halo, subX - 2.5f, dotY - 2.5f, 12, 12);
                    using (var dot = new SolidBrush(DotColor))
                        g.FillEllipse(dot, subX, dotY, 7, 7);
                    subX += 14;
                }
                Theme.DrawText(g, Subtitle, subFont, Theme.TextSecondary, new RectangleF(subX, subY, textW - subX + 16, subH), wrap: false);
            }
        }
    }

    // =====================================================================

    /// <summary>Tom da linha de status: neutro enquanto não há resultado (verificando, aguardando), ok ou aviso.</summary>
    public enum StatusTone
    {
        Neutral,
        Ok,
        Warning
    }

    /// <summary>Linha de status com ícone colorido e um botão de ação opcional (usada para os monitores).</summary>
    public class StatusRow : GControl
    {
        public static readonly Color Ok = Theme.Success;
        public static readonly Color Warn = Theme.Warning;

        private readonly NeonButton action;

        public string Title { get; set; }
        public string Subtitle { get; set; }
        public GlyphKind Glyph { get; set; }
        public StatusTone Tone { get; set; }

        /// <summary>Atalho para Tone: true = aviso, false = ok.</summary>
        public bool Warning
        {
            get { return Tone == StatusTone.Warning; }
            set { Tone = value ? StatusTone.Warning : StatusTone.Ok; }
        }

        public event Action ActionClicked;

        public StatusRow()
        {
            Title = "";
            Subtitle = "";
            Glyph = GlyphKind.Monitor;
            Tone = StatusTone.Ok;
            BackColor = Theme.Panel;
            action = new NeonButton { Size = new Size(104, 32), Visible = false, Style = ButtonStyle.Primary, PaletteOverride = Theme.WarningPalette };
            action.Click += (s, e) => { if (ActionClicked != null) ActionClicked(); };
            Controls.Add(action);
        }

        protected override bool Interactive { get { return false; } }

        public void SetAction(string text)
        {
            action.Text = text ?? "";
            action.Visible = !string.IsNullOrEmpty(text);
            PerformLayout();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            action.Location = new Point(Width - action.Width - 13, (Height - action.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.HighQuality(g);

            // Neutro (ainda sem resultado): quadrado Surface2 e ícone TextSecondary, sem antecipar ok ou aviso.
            bool neutral = Tone == StatusTone.Neutral;
            Color c = Tone == StatusTone.Warning ? Warn : neutral ? Theme.TextSecondary : Ok;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, r, Theme.Radius.Md, Theme.Surface);
            Theme.StrokeRounded(g, r, Theme.Radius.Md, Warning ? Theme.Alpha(Warn, 120) : Theme.Border, 1f);

            var iconBox = new RectangleF(16, (Height - 32) / 2f, 32, 32); // mesma margem do texto do ToggleRow
            Theme.FillRounded(g, iconBox, 8, neutral ? Theme.Surface2 : Theme.Alpha(c, 30));
            using (var ib = new SolidBrush(c))
                Theme.DrawGlyph(g, Glyph, RectangleF.Inflate(iconBox, -8, -8), ib, 1.7f);

            float textX = iconBox.Right + 12;
            float textW = Width - textX - (action.Visible ? action.Width + 24 : 14);
            Font titleFont = Theme.UiSemibold(10f), subFont = Theme.Ui(Theme.TypeScale.Caption);
            if (string.IsNullOrEmpty(Subtitle))
            {
                // Sem subtítulo: o título fica no centro, alinhado ao ícone.
                Theme.DrawText(g, Title, titleFont, Theme.Text, new RectangleF(textX, 0, textW, Height), StringAlignment.Near, StringAlignment.Center, false);
                return;
            }
            float titleH = Math.Max(20f, (float)Math.Ceiling(titleFont.GetHeight(g)));
            float subH = Math.Max(18f, (float)Math.Ceiling(subFont.GetHeight(g)));
            float top = (Height - titleH - subH + 1) / 2f;
            Theme.DrawText(g, Title, titleFont, Theme.Text, new RectangleF(textX, top, textW, titleH), wrap: false);
            Theme.DrawText(g, Subtitle, subFont, Theme.TextSecondary, new RectangleF(textX, top + titleH - 1, textW, subH), wrap: false);
        }
    }

    // =====================================================================

    /// <summary>
    /// Pé da janela principal: as teclas do atalho (keycaps) e, abaixo de uma divisória, o aviso de que o app segue na
    /// bandeja, com uma ação opcional à direita (Sair).
    /// </summary>
    public class HotkeyHint : GControl
    {
        private string[] keys = { "CTRL", "ALT", "M" };
        private Control footerAction;

        /// <summary>Teclas do atalho ativo; null = nenhum atalho disponível.</summary>
        public string[] Keys
        {
            get { return keys; }
            set { keys = value; Invalidate(); }
        }

        /// <summary>Controle no fim da linha do rodapé, à direita do aviso (ex.: o botão Sair).</summary>
        public Control FooterAction
        {
            get { return footerAction; }
            set
            {
                if (footerAction != null) Controls.Remove(footerAction);
                footerAction = value;
                if (value != null) Controls.Add(value);
                PerformLayout();
                Invalidate();
            }
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (footerAction != null)
                footerAction.Location = new Point(Width - footerAction.Width, Height - footerAction.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            float k = g.DpiY / 96f;
            Font footFont = Theme.Ui(Theme.TypeScale.Caption);
            Font textFont = Theme.Ui(Theme.TypeScale.Caption);
            float rowH = Keycaps.RowHeight(g, k);

            // Rodapé: divisória e o aviso da bandeja (quebra em duas linhas se não couber ao lado da ação)
            float footH = footerAction != null ? footerAction.Height : Math.Max(18 * k, (float)Math.Ceiling(footFont.GetHeight(g)));
            float footTop = Height - footH;
            using (var line = new SolidBrush(Theme.Divider))
                g.FillRectangle(line, 0, footTop - 8 * k, Width, 1);
            float footW = Width - (footerAction != null ? footerAction.Width + 8 * k : 0);
            string footText = L.T("Fechar a janela mantém o MouseModes rodando perto do relógio.", "Closing the window keeps MouseModes running next to the clock.");
            Theme.DrawText(g, footText, footFont, Theme.TextTertiary, new RectangleF(0, footTop, footW, footH), StringAlignment.Near, StringAlignment.Center);

            if (keys == null)
            {
                Theme.DrawText(g, L.T("Atalho global indisponível: outros programas usam todas as opções.", "Global hotkey unavailable: other programs use every option."), textFont, Theme.TextSecondary,
                    new RectangleF(0, 0, Width, rowH), StringAlignment.Near, StringAlignment.Center, false);
                return;
            }

            float capH = rowH - 2 * k; // 1,5 px de "profundidade" da tecla por baixo
            float x = Keycaps.Draw(g, keys, 0, 0, capH, k);
            Theme.DrawText(g, L.T("alterna os modos, até dentro do jogo", "switches modes, even inside a game"), textFont, Theme.TextSecondary,
                new RectangleF(x + 8 * k, 0, Math.Max(1, Width - x - 8 * k), capH + 1), StringAlignment.Near, StringAlignment.Center, false);
        }
    }

    /// <summary>Posição de cada tecla e de cada "+" de uma fileira de keycaps, e onde a fileira termina.</summary>
    public sealed class KeycapLayout
    {
        public RectangleF[] Caps;
        /// <summary>O "+" depois de cada tecla (o último só existe com trailingPlus).</summary>
        public RectangleF[] Plus;
        /// <summary>Primeiro x livre depois da fileira.</summary>
        public float End;
    }

    /// <summary>
    /// Teclas de atalho desenhadas como keycaps (Surface2, contorno BorderStrong, raio 4, UiSemibold 8,5, "+" em
    /// TextTertiary): o pé da janela principal (HotkeyHint) e o campo do atalho nos Ajustes (HotkeyBox).
    /// </summary>
    public static class Keycaps
    {
        public const float MinWidth = 26f;
        /// <summary>Folga horizontal somada à largura do texto (7 px de cada lado).</summary>
        public const float Padding = 14f;
        /// <summary>Espaço entre a tecla e o "+" (e depois da última tecla).</summary>
        public const float Gap = 4f;
        public const float PlusWidth = 10f;
        /// <summary>Quanto o "+" avança até a próxima tecla.</summary>
        public const float PlusAdvance = 14f;
        /// <summary>A "profundidade" da tecla, uma faixa BorderStrong por baixo.</summary>
        public const float Depth = 1.5f;

        public static Font Font { get { return Theme.UiSemibold(Theme.TypeScale.Caption); } }

        /// <summary>Altura da fileira: a tecla (RowHeight − 2 px) mais a profundidade, com folga.</summary>
        public static float RowHeight(Graphics g, float k)
        {
            return Math.Max(24 * k, (float)Math.Ceiling(Font.GetHeight(g)) + 8 * k);
        }

        /// <summary>
        /// Onde fica cada tecla a partir de (x, y), sem desenhar. textWidth mede o nome de uma tecla; k é a escala de DPI.
        /// trailingPlus põe um "+" depois da última tecla (a combinação ainda sem a tecla final).
        /// </summary>
        public static KeycapLayout Layout(string[] keys, Func<string, float> textWidth, float x, float y, float capH, float k, bool trailingPlus = false)
        {
            int n = keys != null ? keys.Length : 0;
            var layout = new KeycapLayout { Caps = new RectangleF[n], Plus = new RectangleF[trailingPlus ? n : Math.Max(0, n - 1)] };
            for (int i = 0; i < n; i++)
            {
                var cap = new RectangleF(x + 0.5f, y + 0.5f, Math.Max(MinWidth * k, textWidth(keys[i]) + Padding * k), capH);
                layout.Caps[i] = cap;
                x = cap.Right + Gap * k;
                if (i < layout.Plus.Length)
                {
                    layout.Plus[i] = new RectangleF(x, y, PlusWidth * k, capH);
                    x += PlusAdvance * k;
                }
            }
            layout.End = x;
            return layout;
        }

        private static KeycapLayout Measure(Graphics g, string[] keys, float x, float y, float capH, float k, bool trailingPlus)
        {
            Font keyFont = Font;
            using (var format = new StringFormat(StringFormatFlags.NoWrap))
                return Layout(keys, s => g.MeasureString(s, keyFont, PointF.Empty, format).Width, x, y, capH, k, trailingPlus);
        }

        /// <summary>Largura da fileira desenhada a partir de x = 0 (para saber se cabe antes de desenhar).</summary>
        public static float Width(Graphics g, string[] keys, float k, bool trailingPlus = false)
        {
            return Measure(g, keys, 0, 0, 0, k, trailingPlus).End;
        }

        /// <summary>
        /// Desenha a fileira a partir de (x, y) e devolve o primeiro x livre depois dela. fill troca o
        /// preenchimento da tecla (padrão Surface2) para quando o fundo já é Surface2, como num campo.
        /// </summary>
        public static float Draw(Graphics g, string[] keys, float x, float y, float capH, float k, bool trailingPlus = false, Color? fill = null)
        {
            Font keyFont = Font;
            KeycapLayout layout = Measure(g, keys, x, y, capH, k, trailingPlus);
            for (int i = 0; i < layout.Caps.Length; i++)
            {
                RectangleF cap = layout.Caps[i];
                Theme.FillRounded(g, new RectangleF(cap.X, cap.Y + Depth * k, cap.Width, cap.Height), Theme.Radius.Xs, Theme.BorderStrong);
                Theme.FillRounded(g, cap, Theme.Radius.Xs, fill ?? Theme.Surface2);
                Theme.StrokeRounded(g, cap, Theme.Radius.Xs, Theme.BorderStrong, 1f);
                Theme.DrawText(g, keys[i], keyFont, Theme.Text, cap, StringAlignment.Center, StringAlignment.Center, false);
            }
            foreach (RectangleF plus in layout.Plus)
                Theme.DrawText(g, "+", Theme.Ui(9f), Theme.TextTertiary, plus, StringAlignment.Center, StringAlignment.Center, false);
            return layout.End;
        }
    }

    // =====================================================================

    /// <summary>Cabeçalho de grupo (sentence case), com um valor opcional à direita.</summary>
    public class SectionLabel : GControl
    {
        private string rightText;
        private string[] rightTextOptions;

        /// <summary>Texto da direita. Gravar aqui descarta as alternativas de RightTextOptions.</summary>
        public string RightText
        {
            get { return rightText; }
            set { rightText = value; rightTextOptions = null; }
        }

        /// <summary>
        /// Versões do texto da direita, da mais longa para a mais curta: desenha a primeira que cabe ao lado do título;
        /// só a última termina em "…" quando nem ela cabe. Gravar aqui põe a primeira em RightText.
        /// </summary>
        public string[] RightTextOptions
        {
            get { return rightTextOptions; }
            set
            {
                rightTextOptions = value != null && value.Length > 0 ? value : null;
                rightText = rightTextOptions != null ? rightTextOptions[0] : "";
            }
        }

        public Color RightColor { get; set; }

        /// <summary>
        /// Título de uma linha dentro de um grupo (UiSemibold 10, como o título do ToggleRow), em vez do cabeçalho do
        /// grupo (UiSemibold 11).
        /// </summary>
        public bool RowTitle { get; set; }

        public SectionLabel()
        {
            RightText = "";
            RightColor = Theme.Text;
            Height = 20;
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            // Cabeçalho de grupo: sentence case, semibold, na cor do texto (não mais caixa alta espaçada em cinza).
            string title = Text ?? "";
            Font titleFont = Theme.UiSemibold(RowTitle ? 10f : Theme.TypeScale.Group);
            Theme.DrawText(g, title, titleFont, Theme.Text, new RectangleF(-1, 0, Width + 1, Height), StringAlignment.Near, StringAlignment.Center, false);
            if (string.IsNullOrEmpty(RightText)) return;
            // Só no espaço que sobra à direita do título: texto longo termina em "…" em vez de passar por cima dele.
            float left = 0;
            if (title.Length > 0)
                using (var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap })
                    left = g.MeasureString(title, titleFont, PointF.Empty, format).Width + 8;
            if (left >= Width) return;
            Font font = Theme.Num(10.5f);
            string text = RightText;
            if (rightTextOptions != null && rightTextOptions.Length > 1)
            {
                // A versão mais completa que cabe inteira: o "…" nunca come o fim (o executável e o "?" do palpite).
                var widths = new float[rightTextOptions.Length];
                using (var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap })
                    for (int i = 0; i < widths.Length; i++)
                        widths[i] = g.MeasureString(rightTextOptions[i] ?? "", font, PointF.Empty, format).Width;
                text = rightTextOptions[FirstThatFits(widths, Width - left)];
            }
            Theme.DrawText(g, text, font, RightColor, new RectangleF(left, 0, Width - left, Height), StringAlignment.Far, StringAlignment.Center, false);
        }

        /// <summary>Índice da primeira largura (da mais longa para a mais curta) que cabe no espaço; nenhuma cabe: a última.</summary>
        internal static int FirstThatFits(float[] widths, float space)
        {
            if (widths == null || widths.Length == 0) return 0;
            for (int i = 0; i < widths.Length - 1; i++)
                if (widths[i] <= space) return i;
            return widths.Length - 1;
        }
    }

    // =====================================================================

    /// <summary>Slider desenhado com trilha em gradiente.</summary>
    public class NeonSlider : GControl
    {
        private int minimum = 1;
        private int maximum = 20;
        private int value = 10;
        private bool dragging;

        public Color AccentA { get; set; }
        public Color AccentB { get; set; }
        public int Marker { get; set; } // valor destacado (ex.: 10 = 1:1); 0 = nenhum

        public event Action ValueChanged;

        public NeonSlider()
        {
            AccentA = Theme.DailyA;
            AccentB = Theme.DailyB;
            Height = 36;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int Minimum { get { return minimum; } set { minimum = value; Invalidate(); } }
        public int Maximum { get { return maximum; } set { maximum = Math.Max(minimum + 1, value); Invalidate(); } }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(minimum, Math.Min(maximum, value));
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged();
            }
        }

        private const float Pad = 12f;

        private float XFor(int v)
        {
            float t = (float)(v - minimum) / (maximum - minimum);
            return Pad + t * (Width - Pad * 2);
        }

        private int ValueAt(int x)
        {
            float t = (x - Pad) / (Width - Pad * 2);
            return (int)Math.Round(minimum + Math.Max(0, Math.Min(1, t)) * (maximum - minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            dragging = true;
            Value = ValueAt(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Value = ValueAt(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) Value = value - 1;
            if (e.KeyCode == Keys.Right) Value = value + 1;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            ModePalette p = Theme.PaletteFor(AccentA);
            float cy = Height / 2f;
            var track = new RectangleF(Pad, cy - 2, Width - Pad * 2, 4);
            Theme.FillRounded(g, track, 2, Theme.Surface2);

            // Marcas (ornamento: Decor)
            using (var tick = new SolidBrush(Theme.Decor))
            {
                for (int v = minimum; v <= maximum; v++)
                {
                    if (maximum - minimum > 30 && v % 5 != 0) continue;
                    g.FillRectangle(tick, XFor(v) - 0.5f, cy + 8, 1, 4);
                }
            }
            if (Marker >= minimum && Marker <= maximum)
            {
                using (var mb = new SolidBrush(Theme.TextTertiary))
                    g.FillRectangle(mb, XFor(Marker) - 1, cy + 7, 2, 6);
            }

            float kx = XFor(value);
            var filled = new RectangleF(track.X, track.Y, Math.Max(4, kx - track.X), track.Height);
            Theme.FillRounded(g, filled, 2, p.Fill);

            // Botão branco com anel da cor do modo, sem halo
            float knob = 16 + HoverT * 2;
            var knobRect = new RectangleF(kx - knob / 2, cy - knob / 2, knob, knob);
            g.FillEllipse(Brushes.White, knobRect);
            using (var ring = new Pen(p.Fill, 2f)) g.DrawEllipse(ring, RectangleF.Inflate(knobRect, -1f, -1f));
            if (KeyboardFocus)
                using (var fp = new Pen(p.OnSurface, 2f)) g.DrawEllipse(fp, RectangleF.Inflate(knobRect, 3, 3));
        }
    }

    // =====================================================================

    /// <summary>Seletor de opções lado a lado.</summary>
    public class Segmented : GControl
    {
        private string[] options = new string[0];
        private int selected;
        private int hoverIndex = -1;

        public Color AccentA { get; set; }
        public Color AccentB { get; set; }

        public event Action SelectedIndexChanged;

        public Segmented()
        {
            AccentA = Theme.DailyA;
            AccentB = Theme.DailyB;
            Height = 40;
            Cursor = Cursors.Hand;
        }

        public string[] Options { get { return options; } set { options = value ?? new string[0]; Invalidate(); } }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value == selected) return;
                selected = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged();
            }
        }

        private RectangleF Segment(int i)
        {
            float w = (Width - 6f) / Math.Max(1, options.Length);
            return new RectangleF(3 + i * w, 3, w, Height - 6);
        }

        private int IndexAt(Point p)
        {
            for (int i = 0; i < options.Length; i++)
                if (Segment(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Location);
            if (i != hoverIndex) { hoverIndex = i; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverIndex = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = IndexAt(e.Location);
            if (i >= 0) SelectedIndex = i;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            ModePalette p = Theme.PaletteFor(AccentA);
            var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, outer, 8, Theme.Surface2);
            Theme.StrokeRounded(g, outer, 8, Theme.Border, 1f);

            Font font = Theme.UiSemibold(9f);
            for (int i = 0; i < options.Length; i++)
            {
                RectangleF r = Segment(i);
                bool isSel = i == selected;
                if (isSel)
                    Theme.FillRounded(g, RectangleF.Inflate(r, -0.5f, -0.5f), Theme.Radius.Sm, p.Fill);
                else if (i == hoverIndex)
                    Theme.FillRounded(g, RectangleF.Inflate(r, -0.5f, -0.5f), Theme.Radius.Sm, Theme.SurfaceHover);
                Color fg = isSel ? p.OnFill : i == hoverIndex ? Theme.Text : Theme.TextSecondary;
                Theme.DrawText(g, options[i], font, fg, RectangleF.Inflate(r, -2, 0), StringAlignment.Center, StringAlignment.Center, false);
            }
        }
    }

    // =====================================================================

    /// <summary>Moldura arredondada para TextBox/ListBox no tema escuro.</summary>
    public class FieldHost : GControl
    {
        private readonly Control inner;
        /// <summary>Lista ou registro com rolagem: a barra nativa fica fora da região visível dele; esta, fina, na margem.</summary>
        private readonly ThinScrollBar bar;
        private Size clippedTo;

        public FieldHost(Control inner)
        {
            this.inner = inner;
            Accent = Theme.DailyB; // só escolhe a paleta do modo (os campos com foco usam um contorno neutro)
            BackColor = Theme.Panel2;
            inner.BackColor = Theme.Panel2;
            inner.ForeColor = Theme.Text;
            inner.GotFocus += (s, e) => Invalidate();
            inner.LostFocus += (s, e) => Invalidate();

            var list = inner as ListBox;
            var box = inner as TextBox;
            bool scrolls = list != null || (box != null && box.Multiline && (box.ScrollBars == ScrollBars.Vertical || box.ScrollBars == ScrollBars.Both));
            if (scrolls)
            {
                // A barra nativa sempre presente (a área dos itens não muda de largura), cortada pela região do controle.
                if (list != null) list.ScrollAlwaysVisible = true;
                Controls.Add(inner);
                bar = new ThinScrollBar { Target = inner, BackColor = Theme.Panel2 };
                Controls.Add(bar);
                inner.HandleCreated += (s, e) => PerformLayout();
            }
            else
            {
                Controls.Add(inner);
            }
            Theme.DarkScroll(inner); // se uma barra nativa aparecer, escura
        }

        public Color Accent { get; set; }

        protected override bool Interactive { get { return false; } }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            var tb = inner as TextBox;
            if (tb != null && !tb.Multiline)
            {
                // Campo de uma linha: centraliza na vertical (a altura dele é fixa).
                int h = tb.PreferredHeight;
                inner.Bounds = new Rectangle(10, Math.Max(2, (Height - h) / 2 + 1), Math.Max(10, Width - 20), h);
                // O texto do Label começa 1 px depois do da caixa de texto (margem do TextBox sem borda).
                if (placeholder != null) placeholder.Bounds = new Rectangle(inner.Left - 1, inner.Top, inner.Width, inner.Height);
            }
            else if (bar != null)
            {
                // A largura da barra nativa vem do próprio controle (a escala de DPI muda o tamanho dela). A lista vai
                // até 2 px da borda do campo; a barra nativa (no fim dela) fica fora da região visível e, por cima, a fina.
                int native = inner.IsHandleCreated && inner.Width > inner.ClientSize.Width
                    ? inner.Width - inner.ClientSize.Width
                    : SystemInformation.VerticalScrollBarWidth;
                var area = new Size(Math.Max(10, Width - 14 - native), Math.Max(10, Height - 20));
                inner.Bounds = new Rectangle(12, 10, area.Width + native, area.Height);
                if (clippedTo != area)
                {
                    clippedTo = area;
                    Region old = inner.Region;
                    inner.Region = new Region(new Rectangle(Point.Empty, area));
                    if (old != null) old.Dispose();
                }
                // A barra fina ocupa a margem direita do campo, logo depois da região visível da lista ou do texto.
                bar.Bounds = new Rectangle(12 + area.Width, 8, native, Math.Max(10, Height - 16));
            }
            else if (inner is GControl)
            {
                // Controle desenhado à mão (o campo do atalho, com keycaps): ocupa o campo quase todo, longe dos cantos
                // arredondados e do contorno de foco, e centraliza o próprio conteúdo na vertical.
                inner.Bounds = new Rectangle(10, 3, Math.Max(10, Width - 20), Math.Max(10, Height - 6));
            }
            else
            {
                inner.Bounds = new Rectangle(12, 10, Math.Max(10, Width - 24), Math.Max(10, Height - 20));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.HighQuality(g);
            bool focus = inner.ContainsFocus;
            var r = focus ? new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f) : new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, r, Theme.Radius.Sm, Theme.Surface2);
            // Foco: contorno neutro claro. A cor sólida do modo (o laranja fica perto do vermelho de erro) é do estado ativo.
            Theme.StrokeRounded(g, r, Theme.Radius.Sm, focus ? Theme.Alpha(Theme.Text, 140) : Theme.Border, focus ? 1.5f : 1f);
        }

        /// <summary>Texto de exemplo do campo de uma linha vazio e sem foco, em TextTertiary.</summary>
        public string Placeholder
        {
            get { return placeholder != null ? placeholder.Text : ""; }
            set
            {
                if (!(inner is TextBox) || ((TextBox)inner).Multiline) return;
                if (placeholder == null)
                {
                    placeholder = new Label
                    {
                        ForeColor = Theme.TextTertiary,
                        BackColor = Theme.Surface2,
                        Font = PlaceholderFont ?? inner.Font,
                        TextAlign = PlaceholderAlign,
                        AutoSize = false,
                        UseMnemonic = false,
                        Cursor = Cursors.IBeam
                    };
                    placeholder.Click += (s, e) => inner.Focus();
                    inner.TextChanged += (s, e) => UpdatePlaceholder();
                    inner.GotFocus += (s, e) => UpdatePlaceholder();
                    inner.LostFocus += (s, e) => UpdatePlaceholder();
                    // Embaixo da caixa: enquanto o exemplo aparece, a caixa fica sem região visível (UpdatePlaceholder). Assim
                    // a janela impressa (DrawToBitmap, que imprime de cima para baixo) também mostra o exemplo.
                    Controls.Add(placeholder);
                    placeholder.SendToBack();
                }
                placeholder.Text = value ?? "";
                PerformLayout();
                UpdatePlaceholder();
            }
        }

        private Label placeholder;

        /// <summary>Fonte do exemplo (null = a do campo).</summary>
        public Font PlaceholderFont { get; set; }

        /// <summary>Alinhamento do exemplo (o de um campo centralizado é MiddleCenter).</summary>
        public ContentAlignment PlaceholderAlign { get; set; } = ContentAlignment.TopLeft;

        private void UpdatePlaceholder()
        {
            if (placeholder == null) return;
            // Com o foco o Label sai (cobriria o cursor); o exemplo nativo da caixa (Theme.CueBanner) fica no lugar dele.
            bool show = placeholder.Text.Length > 0 && inner.Text.Length == 0 && !inner.ContainsFocus;
            placeholder.Visible = show;
            // A caixa continua no Tab e recebe o foco pelo clique no exemplo; só não cobre o exemplo enquanto ele aparece.
            if (show == (inner.Region != null)) return;
            Region old = inner.Region;
            inner.Region = show ? new Region(Rectangle.Empty) : null;
            if (old != null) old.Dispose();
        }
    }

    // =====================================================================

    /// <summary>
    /// Barra de rolagem fina pintada na margem (polegar de 4 a 6 px, raio 3, sem trilho), para uma página ou lista cuja
    /// barra nativa fica escondida fora do recorte. A rolagem continua sendo a do controle (roda, teclado, foco); a barra
    /// só lê a posição dele e, no arraste ou no clique, manda rolar.
    /// </summary>
    public class ThinScrollBar : GControl
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SCROLLINFO
        {
            public int cbSize, fMask, nMin, nMax, nPage, nPos, nTrackPos;
        }

        [DllImport("user32.dll")] private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref SCROLLINFO info);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int SB_VERT = 1, SIF_ALL = 0x17, GWL_STYLE = -16, WS_VSCROLL = 0x00200000;
        private const int WM_MOUSEWHEEL = 0x020A, EM_LINESCROLL = 0x00B6;

        /// <summary>Repassa à barra as mensagens do controle que podem mudar a rolagem.</summary>
        private sealed class Listener : NativeWindow
        {
            private readonly ThinScrollBar owner;

            public Listener(ThinScrollBar owner)
            {
                this.owner = owner;
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                switch (m.Msg)
                {
                    case 0x000F: // WM_PAINT
                    case 0x0115: // WM_VSCROLL
                    case 0x020A: // WM_MOUSEWHEEL
                    case 0x0005: // WM_SIZE
                    case 0x0047: // WM_WINDOWPOSCHANGED
                    case 0x0100: // WM_KEYDOWN
                        owner.Sync();
                        break;
                }
            }
        }

        private Control target;
        private Listener listener;
        private int min, max, page, pos;
        private bool dragging;
        private float grabOffset;

        public ThinScrollBar()
        {
            Size = new Size(10, 100);
        }

        /// <summary>Cor da última coluna à direita (a borda da janela no Windows 10); Color.Empty = nenhuma.</summary>
        public Color EdgeColor { get; set; }

        /// <summary>Espaço (px a 96 dpi) entre o polegar e a borda direita da barra.</summary>
        public float RightInset { get; set; } = 2f;

        /// <summary>O controle que rola (página com AutoScroll, ListBox ou TextBox de várias linhas).</summary>
        public Control Target
        {
            get { return target; }
            set
            {
                if (target == value) return;
                if (target != null)
                {
                    target.HandleCreated -= OnTargetHandle;
                    target.HandleDestroyed -= OnTargetHandleGone;
                    Detach();
                }
                target = value;
                if (target != null)
                {
                    target.HandleCreated += OnTargetHandle;
                    target.HandleDestroyed += OnTargetHandleGone;
                    if (target.IsHandleCreated) Attach();
                }
                Sync();
            }
        }

        private void OnTargetHandle(object sender, EventArgs e)
        {
            Attach();
            Sync();
        }

        private void OnTargetHandleGone(object sender, EventArgs e)
        {
            Detach();
        }

        private void Attach()
        {
            Detach();
            listener = new Listener(this);
            listener.AssignHandle(target.Handle);
        }

        private void Detach()
        {
            if (listener == null) return;
            listener.ReleaseHandle();
            listener = null;
        }

        /// <summary>Posição de rolagem lida na última sincronização.</summary>
        internal int Position
        {
            get { return pos; }
        }

        /// <summary>Há o que rolar: o conteúdo passa da área visível.</summary>
        public bool Scrollable
        {
            get { return page > 0 && max - min + 1 > page; }
        }

        /// <summary>Lê a posição atual do controle e redesenha se ela mudou.</summary>
        public void Sync()
        {
            if (Read()) Invalidate();
        }

        /// <summary>Lê a rolagem do controle; true se mudou desde a última leitura.</summary>
        private bool Read()
        {
            int nMin = 0, nMax = 0, nPage = 0, nPos = 0;
            if (target != null && target.IsHandleCreated && (GetWindowLong(target.Handle, GWL_STYLE) & WS_VSCROLL) != 0)
            {
                var info = new SCROLLINFO { cbSize = Marshal.SizeOf(typeof(SCROLLINFO)), fMask = SIF_ALL };
                if (GetScrollInfo(target.Handle, SB_VERT, ref info))
                {
                    nMin = info.nMin;
                    nMax = info.nMax;
                    nPage = info.nPage;
                    nPos = info.nPos;
                }
            }
            if (nMin == min && nMax == max && nPage == page && nPos == pos) return false;
            min = nMin;
            max = nMax;
            page = nPage;
            pos = nPos;
            return true;
        }

        private RectangleF Track
        {
            get { return new RectangleF(0, 4, Width, Math.Max(1, Height - 8)); }
        }

        /// <summary>Polegar: altura proporcional à parte visível (mínimo 24 px), posição pela rolagem.</summary>
        private RectangleF Thumb(float width)
        {
            RectangleF track = Track;
            int range = Math.Max(1, max - min + 1);
            float len = Math.Max(Math.Min(track.Height, 24f * DeviceDpi / 96f), track.Height * page / range);
            int span = Math.Max(1, range - page);
            float t = Math.Max(0f, Math.Min(1f, (pos - min) / (float)span));
            float right = RightInset * DeviceDpi / 96f;
            return new RectangleF(Width - right - width, track.Y + (track.Height - len) * t, width, len);
        }

        private void ScrollTo(int value)
        {
            if (target == null || !target.IsHandleCreated) return;
            value = Math.Max(min, Math.Min(Math.Max(min, max - page + 1), value));
            var scrollable = target as ScrollableControl;
            var list = target as ListBox;
            if (scrollable != null)
                scrollable.AutoScrollPosition = new Point(-scrollable.AutoScrollPosition.X, value);
            else if (list != null)
                list.TopIndex = value;
            else
                SendMessage(target.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(value - pos)); // TextBox: em linhas
            Sync();
        }

        protected override void WndProc(ref Message m)
        {
            // A roda em cima da barra rola o controle, como em cima dele.
            if (m.Msg == WM_MOUSEWHEEL && target != null && target.IsHandleCreated)
            {
                SendMessage(target.Handle, WM_MOUSEWHEEL, m.WParam, m.LParam);
                Sync();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Scrollable) return;
            RectangleF thumb = Thumb(Width);
            if (e.Y >= thumb.Top && e.Y <= thumb.Bottom)
            {
                dragging = true;
                grabOffset = e.Y - thumb.Top;
                Capture = true;
            }
            else
            {
                // Clique fora do polegar: uma página para cima ou para baixo.
                ScrollTo(e.Y < thumb.Top ? pos - page : pos + page);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            RectangleF track = Track;
            RectangleF thumb = Thumb(Width);
            float room = Math.Max(1f, track.Height - thumb.Height);
            float t = Math.Max(0f, Math.Min(1f, (e.Y - grabOffset - track.Y) / room));
            int span = Math.Max(0, max - min + 1 - page);
            ScrollTo(min + (int)Math.Round(t * span));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            Capture = false;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Target = null;
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            Read(); // também ao imprimir a janela (WM_PRINT), que não passa pelo WM_PAINT do controle
            if (EdgeColor != Color.Empty)
                using (var edge = new SolidBrush(EdgeColor)) g.FillRectangle(edge, Width - 1, 0, 1, Height);
            if (!Scrollable) return;
            float k = DeviceDpi / 96f;
            float w = (4f + 2f * (dragging ? 1f : HoverT)) * k;
            RectangleF thumb = Thumb(w);
            int alpha = dragging ? 90 : 48 + (int)(30 * HoverT);
            Theme.FillRounded(g, thumb, w / 2f, Theme.Alpha(Color.White, alpha));
        }
    }

    // =====================================================================

    /// <summary>Item da navegação lateral das configurações.</summary>
    public class NavItem : GControl
    {
        private bool selected;

        public GlyphKind Glyph { get; set; }
        public Color AccentA { get; set; }
        public Color AccentB { get; set; }

        public NavItem()
        {
            AccentA = Theme.DailyA;
            AccentB = Theme.DailyB;
            Size = new Size(164, 44);
            Cursor = Cursors.Hand;
        }

        public bool Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            ModePalette p = Theme.PaletteFor(AccentA);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            if (selected)
            {
                Theme.FillRounded(g, r, Theme.Radius.Sm, Theme.Alpha(Color.White, 8));
                Theme.FillRounded(g, new RectangleF(r.X + 1, r.Y + (r.Height - 16) / 2f, 3, 16), 1.5f, p.Fill);
            }
            else if (HoverT > 0.01f)
            {
                Theme.FillRounded(g, r, Theme.Radius.Sm, Theme.Alpha(Color.White, (int)(5 * HoverT + 0.5f)));
            }

            Color fg = selected ? Theme.Text : Theme.Lerp(Theme.TextSecondary, Theme.Text, HoverT);
            var icon = new RectangleF(16, (Height - 16) / 2f, 16, 16);
            using (var ib = new SolidBrush(selected ? p.OnSurface : fg))
                Theme.DrawGlyph(g, Glyph, icon, ib, 1.6f);
            Theme.DrawText(g, Text, selected ? Theme.UiSemibold(10f) : Theme.Ui(10f), fg,
                new RectangleF(42, 0, Math.Max(1, Width - 46), Height), StringAlignment.Near, StringAlignment.Center, false);
            if (KeyboardFocus) Theme.DrawFocusRing(g, r, Theme.Radius.Sm + 1, p.OnSurface);
        }
    }

    // =====================================================================

    /// <summary>Número grande do medidor de polling rate.</summary>
    public class BigReadout : GControl
    {
        public string Value { get; set; }
        public string Unit { get; set; }
        public string Caption { get; set; }
        public Color AccentA { get; set; }
        public Color AccentB { get; set; }
        public float Pulse { get; set; }

        public BigReadout()
        {
            Value = "—";
            Unit = "Hz";
            Caption = "";
            AccentA = Theme.PerfA;
            AccentB = Theme.PerfB;
        }

        protected override bool Interactive { get { return false; } }

        /// <summary>Sem leitura ainda: o valor é "—" ou vazio.</summary>
        public bool Idle
        {
            get { return string.IsNullOrEmpty(Value) || Value.Trim() == "—" || Value.Trim() == "-"; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            ModePalette p = Theme.PaletteFor(AccentA);
            var card = new RectangleF(8.5f, 8.5f, Width - 17, Height - 17);
            // Em repouso ("—" ou vazio) é neutro: borda comum, traço apagado. Com leitura, só o contorno do modo.
            bool idle = Idle;
            Theme.FillRounded(g, card, Theme.Radius.Lg, Theme.Surface);
            using (var hl = new SolidBrush(Theme.Alpha(Color.White, 6))) g.FillRectangle(hl, card.X + Theme.Radius.Lg, card.Y + 1, card.Width - Theme.Radius.Lg * 2, 1);
            Theme.StrokeRounded(g, card, Theme.Radius.Lg, idle ? Theme.Border : p.Stroke, 1f);

            string value = string.IsNullOrEmpty(Value) ? "—" : Value;
            Font big = Theme.Num(Theme.TypeScale.NumXL);
            Font unit = Theme.Num(13f);
            Font caption = Theme.Ui(9f);
            SizeF vs, us;
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                vs = g.MeasureString(value, big, PointF.Empty, format);
                us = string.IsNullOrEmpty(Unit) ? SizeF.Empty : g.MeasureString(Unit, unit, PointF.Empty, format);
                float gap = us.Width > 0 ? 8 : 0;
                float total = vs.Width + gap + us.Width;
                float captionH = (float)Math.Ceiling(caption.GetHeight(g));
                // Valor e legenda centralizados juntos na altura do cartão.
                float blockH = vs.Height + 6 + captionH;
                float y = card.Y + Math.Max(8, (card.Height - blockH) / 2f);
                float x = card.X + (card.Width - total) / 2f;
                using (var vb = new SolidBrush(idle ? Theme.TextTertiary : Theme.Text))
                    g.DrawString(value, big, vb, x, y, format);
                if (us.Width > 0)
                    using (var ub = new SolidBrush(Theme.TextSecondary))
                        g.DrawString(Unit, unit, ub, x + vs.Width + gap, y + vs.Height * 0.78f - us.Height * 0.78f, format);
                Theme.DrawText(g, Caption, caption, Theme.TextSecondary,
                    new RectangleF(card.X + 12, y + vs.Height + 6, card.Width - 24, captionH + 2), StringAlignment.Center, StringAlignment.Near, false);
            }
        }
    }

    /// <summary>Linha de chips (rótulo + valor).</summary>
    public class ChipRow : GControl
    {
        public string[] Labels { get; set; }
        public string[] Values { get; set; }
        public Color Accent { get; set; }
        /// <summary>Tamanho (pt) dos valores: NumM por padrão; maior nos números-chave do Raio-X.</summary>
        public float ValueSize { get; set; }

        public ChipRow()
        {
            Labels = new string[0];
            Values = new string[0];
            Accent = Theme.PerfB;
            ValueSize = Theme.TypeScale.NumM;
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            Theme.DrawChips(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Labels, Values, Accent, null, ValueSize);
        }
    }

    /// <summary>Gráfico de barras com os intervalos entre leituras do mouse.</summary>
    public class IntervalGraph : GControl
    {
        private double[] samples = new double[0];

        public Color AccentA { get; set; }
        public Color AccentB { get; set; }

        public IntervalGraph()
        {
            AccentA = Theme.PerfA;
            AccentB = Theme.PerfB;
        }

        protected override bool Interactive { get { return false; } }

        public void SetSamples(IList<double> values, int max)
        {
            int n = Math.Min(max, values.Count);
            samples = new double[n];
            for (int i = 0; i < n; i++) samples[i] = values[values.Count - n + i];
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            ModePalette p = Theme.PaletteFor(AccentA);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, r, Theme.Radius.Md, Theme.Surface);
            Theme.StrokeRounded(g, r, Theme.Radius.Md, Theme.Border, 1f);

            var plot = new RectangleF(r.X + 12, r.Y + 12, r.Width - 24, r.Height - 24);
            bool empty = samples.Length < 2;
            using (var grid = new Pen(Theme.Decor))
            {
                for (int i = 1; i < 4; i++)
                {
                    if (empty && i == 2) continue; // a linha do meio passaria por cima do texto de espera
                    float y = plot.Y + plot.Height * i / 4f;
                    g.DrawLine(grid, plot.X, y, plot.Right, y);
                }
            }

            if (empty)
            {
                Theme.DrawText(g, L.T("Mexa o mouse para ver as leituras", "Move the mouse to see the readings"), Theme.Ui(9f), Theme.TextTertiary, plot, StringAlignment.Center, StringAlignment.Center);
                return;
            }

            var sorted = (double[])samples.Clone();
            Array.Sort(sorted);
            double median = sorted[sorted.Length / 2];
            double top = Math.Max(median * 2.5, 0.0001);

            float barW = plot.Width / samples.Length;
            using (var bars = new SolidBrush(p.Fill))
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    float h = (float)Math.Min(1.0, samples[i] / top) * plot.Height;
                    g.FillRectangle(bars, plot.X + i * barW, plot.Bottom - h, Math.Max(1f, barW - 1f), h);
                }
            }

            // Linha da mediana
            float my = plot.Bottom - (float)(median / top) * plot.Height;
            using (var mp = new Pen(Theme.Alpha(Color.White, 120), 1f) { DashStyle = DashStyle.Dash })
                g.DrawLine(mp, plot.X, my, plot.Right, my);
            Theme.DrawText(g, median.ToString("0.00") + " ms", Theme.Num(Theme.TypeScale.Caption), Theme.Text,
                new RectangleF(plot.Right - 90, my - 18, 90, 16), StringAlignment.Far);
        }
    }

    // =====================================================================

    /// <summary>
    /// Grupo de linhas das configurações (Carbon): superfície com raio 10 e borda; as linhas de dentro não têm cartão
    /// próprio e ficam separadas por divisórias recuadas 16 px. As posições são dadas na construção (px a 96 dpi), antes
    /// de a janela aplicar a escala, que depois leva o grupo e as linhas juntos.
    /// </summary>
    public class SettingsGroup : GControl
    {
        /// <summary>Recuo das divisórias e do conteúdo das linhas.</summary>
        public const int Inset = 16;

        private readonly List<Control> rows = new List<Control>();

        public SettingsGroup(int width)
        {
            BackColor = Theme.Surface;
            Size = new Size(width, 0);
        }

        protected override bool Interactive { get { return false; } }

        /// <summary>Acrescenta uma linha de largura inteira embaixo da última, com 1 px de divisória entre elas.</summary>
        public void AddRow(Control row)
        {
            int y = rows.Count == 0 ? 0 : rows[rows.Count - 1].Bottom + 1;
            row.Location = new Point(0, y);
            row.Width = Width;
            rows.Add(row);
            Controls.Add(row);
            Height = row.Bottom;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, r, Theme.Radius.Md, Theme.Surface);
            Theme.StrokeRounded(g, r, Theme.Radius.Md, Theme.Border, 1f);
            float inset = Inset * g.DpiX / 96f;
            using (var line = new SolidBrush(Theme.Border))
            {
                for (int i = 1; i < rows.Count; i++)
                {
                    if (!rows[i].Visible) continue;
                    g.FillRectangle(line, inset, rows[i].Top - 1, Width - inset * 2, 1);
                }
            }
        }

        /// <summary>
        /// Pinta, nas coordenadas de uma linha do grupo, o fundo e a borda do grupo, para os cantos arredondados saírem
        /// certos na linha de cima e na de baixo. Retorna false (sem pintar nada) se o controle não está num grupo; shape
        /// é o contorno do grupo nas coordenadas da linha (quem chama libera).
        /// </summary>
        public static bool PaintBackdrop(Graphics g, Control row, out GraphicsPath shape)
        {
            shape = null;
            var group = row.Parent as SettingsGroup;
            if (group == null) return false;
            g.Clear(group.Parent != null ? group.Parent.BackColor : Theme.Bg);
            var r = new RectangleF(0.5f - row.Left, 0.5f - row.Top, group.Width - 1, group.Height - 1);
            shape = Theme.Rounded(r, Theme.Radius.Md);
            using (var fill = new SolidBrush(Theme.Surface)) g.FillPath(fill, shape);
            using (var pen = new Pen(Theme.Border, 1f)) g.DrawPath(pen, shape);
            return true;
        }
    }

    /// <summary>Linha de um SettingsGroup feita de outros controles (título, slider, campo, legenda...).</summary>
    public class GroupCell : GControl
    {
        public GroupCell()
        {
            BackColor = Theme.Surface;
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.HighQuality(g);
            GraphicsPath shape;
            if (SettingsGroup.PaintBackdrop(g, this, out shape)) shape.Dispose();
            else g.Clear(BackColor);
        }
    }

    /// <summary>Linha de um SettingsGroup com um ícone num quadrado à esquerda e um texto de várias linhas.</summary>
    public class IconRow : GControl
    {
        private const int Box = 32, TextLeft = SettingsGroup.Inset + Box + 12, PadY = 12;

        public GlyphKind Glyph { get; set; }

        public IconRow()
        {
            BackColor = Theme.Surface;
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        private static Font TextFont { get { return Theme.Ui(9f); } }

        /// <summary>Altura (px a 96 dpi) da linha com o texto quebrado na largura dela.</summary>
        public static int HeightFor(string text, int width)
        {
            return Math.Max(56, Theme.TextHeight(text, TextFont, width - TextLeft - SettingsGroup.Inset) + PadY * 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.HighQuality(g);
            GraphicsPath shape;
            if (SettingsGroup.PaintBackdrop(g, this, out shape)) shape.Dispose();
            else g.Clear(BackColor);

            float k = g.DpiX / 96f;
            var box = new RectangleF(SettingsGroup.Inset * k, (Height - Box * k) / 2f, Box * k, Box * k);
            Theme.FillRounded(g, box, 8 * k, Theme.Surface2);
            using (var ib = new SolidBrush(Theme.TextSecondary))
                Theme.DrawGlyph(g, Glyph, RectangleF.Inflate(box, -8 * k, -8 * k), ib, 1.6f * k);
            float x = TextLeft * k;
            Theme.DrawWrapped(g, Text, TextFont, Theme.TextSecondary,
                new RectangleF(x, 0, Math.Max(1, Width - x - SettingsGroup.Inset * k), Height));
        }
    }

    /// <summary>Nota de rodapé de página: tinta leve da cor do modo, barra de 3 px à esquerda e texto de apoio.</summary>
    public class InfoNote : GControl
    {
        private const int PadX = 16, PadY = 10;

        public Color Accent { get; set; }

        public InfoNote()
        {
            Accent = Theme.DailyA;
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        private static Font TextFont { get { return Theme.Ui(Theme.TypeScale.Caption); } }

        /// <summary>Altura (px a 96 dpi) da nota com o texto quebrado na largura dela.</summary>
        public static int HeightFor(string text, int width)
        {
            return Theme.TextHeight(text, TextFont, width - PadX * 2) + PadY * 2;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            float k = g.DpiX / 96f;
            ModePalette p = Theme.PaletteFor(Accent);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.Rounded(r, Theme.Radius.Md))
            {
                using (var fill = new SolidBrush(Theme.Alpha(p.Fill, 12))) g.FillPath(fill, path);
                GraphicsState state = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                using (var bar = new SolidBrush(p.Fill)) g.FillRectangle(bar, 0, 0, 3 * k, Height);
                g.Restore(state);
            }
            float x = PadX * k;
            Theme.DrawWrapped(g, Text, TextFont, Theme.TextSecondary, new RectangleF(x, 0, Math.Max(1, Width - x * 2), Height));
        }
    }

    /// <summary>
    /// Estado vazio: caixa Surface com borda (raio 10) e um ícone apagado; o texto é um Label posto dentro dela. O
    /// tracejado fica só para a área de arrastar do DPI, que é interativa.
    /// </summary>
    public class EmptyState : GControl
    {
        public GlyphKind Glyph { get; set; }

        /// <summary>Topo do ícone de 20 px (px a 96 dpi); o texto vai logo abaixo.</summary>
        public int GlyphTop { get; set; }

        public EmptyState()
        {
            Glyph = GlyphKind.Mouse;
            GlyphTop = 16;
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            float k = g.DpiX / 96f;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, r, Theme.Radius.Md, Theme.Surface);
            Theme.StrokeRounded(g, r, Theme.Radius.Md, Theme.Border, 1f);
            float icon = 20 * k;
            using (var ib = new SolidBrush(Theme.TextTertiary))
                Theme.DrawGlyph(g, Glyph, new RectangleF((Width - icon) / 2f, GlyphTop * k, icon, icon), ib, 1.6f * k);
        }
    }

    // =====================================================================
    // Peças das telas de relatório e das ferramentas

    /// <summary>
    /// Cartão de seção (Surface, raio 10, borda e um realce de 1 px no topo) que contém outros controles. Os textos de
    /// dentro usam fundo Theme.Surface (um Label transparente pintaria o fundo da janela).
    /// </summary>
    public class Card : GControl
    {
        public Card()
        {
            BackColor = Theme.Surface;
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, r, Theme.Radius.Md, Theme.Surface);
            using (var hl = new SolidBrush(Theme.Alpha(Color.White, 6)))
                g.FillRectangle(hl, r.X + Theme.Radius.Md, r.Y + 1, Math.Max(1, r.Width - Theme.Radius.Md * 2), 1);
            Theme.StrokeRounded(g, r, Theme.Radius.Md, Theme.Border, 1f);
        }
    }

    /// <summary>Parágrafo pintado pelo GDI+ (para as fontes de título, que o GDI de um Label não desenha bem), no topo.</summary>
    public class Paragraph : GControl
    {
        public Font TextFont { get; set; }
        public Color TextColor { get; set; }

        public Paragraph()
        {
            TextFont = Theme.Ui(Theme.TypeScale.Body);
            TextColor = Theme.Text;
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        /// <summary>Altura (px a 96 dpi) do texto quebrado na largura dada.</summary>
        public static int HeightFor(string text, Font font, int width)
        {
            return Theme.TextHeight(text, font, width);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            float lineH = TextFont.GetHeight(g);
            float y = 0;
            // O GDI+ põe 1/6 de em antes da letra: começa antes disso, para o texto ficar na borda (a dos cartões).
            float pad = TextFont.SizeInPoints * g.DpiX / 72f / 6f;
            using (var brush = new SolidBrush(TextColor))
            using (var format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
            {
                foreach (string line in Theme.WrapLines(g, Text, TextFont, Width))
                {
                    g.DrawString(line, TextFont, brush, new RectangleF(-pad, y, Width + pad, lineH + 2), format);
                    y += lineH;
                }
            }
        }
    }

    /// <summary>Rótulo de campo (sentence case, semibold pequeno, TextSecondary), logo acima da caixa.</summary>
    public class FieldLabel : GControl
    {
        public FieldLabel()
        {
            Height = 16;
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            Theme.DrawText(g, Text, Theme.UiSemibold(Theme.TypeScale.Caption), Theme.TextSecondary,
                new RectangleF(-1, 0, Width + 1, Height), StringAlignment.Near, StringAlignment.Center, false);
        }
    }

    /// <summary>
    /// Uma linha de texto em trechos de cores diferentes (ex.: só o pico em âmbar) e um valor opcional na coluna da
    /// direita (ex.: a variação "+21%"). Text guarda o texto inteiro dos trechos.
    /// </summary>
    public class TextRuns : GControl
    {
        private readonly List<KeyValuePair<string, Color>> runs = new List<KeyValuePair<string, Color>>();

        public float FontSize { get; set; }
        public string RightText { get; set; }
        public Color RightColor { get; set; }

        public TextRuns()
        {
            FontSize = 9f;
            RightText = "";
            RightColor = Theme.TextSecondary;
        }

        protected override bool Interactive { get { return false; } }

        public void Add(string text, Color color)
        {
            runs.Add(new KeyValuePair<string, Color>(text ?? "", color));
            var all = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, Color> run in runs) all.Append(run.Key);
            Text = all.ToString();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            Font font = Theme.Ui(FontSize);
            float limit = Width;
            if (!string.IsNullOrEmpty(RightText))
            {
                Font rightFont = Theme.UiSemibold(FontSize);
                float rw;
                using (var format = new StringFormat(StringFormatFlags.NoWrap)) rw = g.MeasureString(RightText, rightFont, PointF.Empty, format).Width;
                Theme.DrawText(g, RightText, rightFont, RightColor, new RectangleF(0, 0, Width, Height), StringAlignment.Far, StringAlignment.Center, false);
                limit = Math.Max(1, Width - rw - 12);
            }
            float lineH = font.GetHeight(g);
            float y = (Height - lineH) / 2f;
            float x = 0;
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                format.Trimming = StringTrimming.EllipsisCharacter;
                foreach (KeyValuePair<string, Color> run in runs)
                {
                    if (run.Key.Length == 0) continue;
                    float w = g.MeasureString(run.Key, font, PointF.Empty, format).Width;
                    using (var brush = new SolidBrush(run.Value))
                        g.DrawString(run.Key, font, brush, new RectangleF(x, y, Math.Max(1, limit - x), lineH + 2), format);
                    x += w;
                    if (x >= limit) break;
                }
            }
        }
    }

    /// <summary>Ícone pequeno de veredito: visto (bom) ou exclamação (vale olhar) num círculo da cor semântica.</summary>
    public class VerdictIcon : GControl
    {
        private bool good = true;

        public bool Good
        {
            get { return good; }
            set { good = value; Invalidate(); }
        }

        public VerdictIcon()
        {
            Size = new Size(16, 16);
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            Color tone = good ? Theme.Success : Theme.Warning;
            float d = Math.Min(Width, Height) - 1;
            var r = new RectangleF((Width - d) / 2f, (Height - d) / 2f, d, d);
            using (var b = new SolidBrush(Theme.Alpha(tone, 40))) g.FillEllipse(b, r);
            float inset = d * 0.27f;
            using (var b = new SolidBrush(tone))
                Theme.DrawGlyph(g, good ? GlyphKind.Check : GlyphKind.Alert, RectangleF.Inflate(r, -inset, -inset), b, Math.Max(1.4f, d / 10f));
        }
    }

    /// <summary>Ícone num círculo tingido (cabeçalho de diálogo: perigo, aviso, informação).</summary>
    public class IconBadge : GControl
    {
        public GlyphKind Glyph { get; set; }
        public Color Tone { get; set; }

        public IconBadge()
        {
            Glyph = GlyphKind.Alert;
            Tone = Theme.Danger;
            Size = new Size(40, 40);
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            float d = Math.Min(Width, Height) - 1;
            var r = new RectangleF((Width - d) / 2f, (Height - d) / 2f, d, d);
            using (var b = new SolidBrush(Theme.Alpha(Tone, 28))) g.FillEllipse(b, r);
            float inset = d * 0.3f;
            using (var b = new SolidBrush(Tone))
                Theme.DrawGlyph(g, Glyph, RectangleF.Inflate(r, -inset, -inset), b, Math.Max(1.6f, d / 18f));
        }
    }

    /// <summary>Estado em pílula: ponto de 6 px e o texto na cor semântica (Success, Warning) ou neutro (Tone vazio).</summary>
    public class Pill : GControl
    {
        public Color Tone { get; set; }

        public Pill()
        {
            Tone = Color.Empty;
            Height = 24;
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        private static Font PillFont { get { return Theme.UiSemibold(9f); } }

        /// <summary>Largura (px a 96 dpi) da pílula com o texto.</summary>
        public static int WidthFor(string text)
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                using (var format = new StringFormat(StringFormatFlags.NoWrap))
                {
                    float k = g.DpiX / 96f;
                    return (int)Math.Ceiling(g.MeasureString(text ?? "", PillFont, PointF.Empty, format).Width / k) + 30;
                }
            }
            catch
            {
                return (text ?? "").Length * 7 + 30;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            float k = g.DpiX / 96f;
            bool neutral = Tone.IsEmpty;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, r, r.Height / 2f, neutral ? Theme.Alpha(Color.White, 8) : Theme.Alpha(Tone, 30));
            float dot = 6 * k;
            using (var b = new SolidBrush(neutral ? Theme.TextTertiary : Tone))
                g.FillEllipse(b, 10 * k, (Height - dot) / 2f, dot, dot);
            Theme.DrawText(g, Text, PillFont, neutral ? Theme.TextSecondary : Tone,
                new RectangleF(22 * k, 0, Math.Max(1, Width - 28 * k), Height), StringAlignment.Near, StringAlignment.Center, false);
        }
    }

    /// <summary>
    /// Estado vazio padrão: ícone de 24 px apagado e o texto centralizado logo abaixo. Framed = caixa Surface com borda
    /// (no lugar de uma lista); sem moldura, fica por cima de uma caixa vazia (fundo do pai).
    /// </summary>
    public class EmptyHint : GControl
    {
        public GlyphKind Glyph { get; set; }
        public bool Framed { get; set; }

        public EmptyHint()
        {
            Glyph = GlyphKind.Mouse;
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            float k = g.DpiX / 96f;
            if (Framed)
            {
                var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
                Theme.FillRounded(g, r, Theme.Radius.Md, Theme.Surface);
                Theme.StrokeRounded(g, r, Theme.Radius.Md, Theme.Border, 1f);
            }
            Font font = Theme.Ui(9f);
            float textW = Math.Max(1, Width - 48 * k);
            List<string> lines = Theme.WrapLines(g, Text, font, textW);
            float lineH = font.GetHeight(g);
            float icon = 24 * k, gap = 10 * k;
            float total = icon + gap + lineH * lines.Count;
            float y = Math.Max(4 * k, (Height - total) / 2f);
            using (var ib = new SolidBrush(Theme.TextTertiary))
                Theme.DrawGlyph(g, Glyph, new RectangleF((Width - icon) / 2f, y, icon, icon), ib, 1.6f * k);
            y += icon + gap;
            using (var brush = new SolidBrush(Theme.TextSecondary))
            using (var format = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
            {
                foreach (string line in lines)
                {
                    g.DrawString(line, font, brush, new RectangleF(0, y, Width, lineH + 2), format);
                    y += lineH;
                }
            }
        }
    }
}
