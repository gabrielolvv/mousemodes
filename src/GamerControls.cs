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

        public event Action CloseClicked;
        public event Action MinimizeClicked;

        public TitleBar()
        {
            Title = "MOUSEMODES";
            AccentA = Theme.DailyA;
            AccentB = Theme.DailyB;
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

            // Logo: mouse dentro de um quadrado com gradiente
            var logo = new RectangleF(16, (Height - 24) / 2f, 24, 24);
            using (LinearGradientBrush lb = Theme.Gradient(logo, AccentA, AccentB, 45f))
                Theme.FillRounded(g, logo, 7, lb);
            using (var white = new SolidBrush(Color.White))
                Theme.DrawGlyph(g, GlyphKind.Mouse, RectangleF.Inflate(logo, -6, -5), white, 1.6f);

            SizeF ts = Theme.MeasureSpaced(g, Title, Theme.Display(10.5f), 2.2f);
            Theme.DrawSpaced(g, Title, Theme.Display(10.5f), Theme.Text, 50, (Height - ts.Height) / 2f, 2.2f);

            // Botões da janela
            if (hoverButton == 0)
                using (var b = new SolidBrush(Theme.Danger)) g.FillRectangle(b, CloseRect);
            using (var b = new SolidBrush(hoverButton == 0 ? Color.White : Theme.Muted))
            {
                Rectangle c = CloseRect;
                Theme.DrawGlyph(g, GlyphKind.Close, new RectangleF(c.X + c.Width / 2f - 5, c.Y + c.Height / 2f - 5, 10, 10), b, 1.4f);
            }

            if (ShowMinimize)
            {
                if (hoverButton == 1)
                    using (var b = new SolidBrush(Theme.Panel2)) g.FillRectangle(b, MinRect);
                using (var b = new SolidBrush(hoverButton == 1 ? Theme.Text : Theme.Muted))
                {
                    Rectangle m = MinRect;
                    Theme.DrawGlyph(g, GlyphKind.Minimize, new RectangleF(m.X + m.Width / 2f - 5, m.Y + m.Height / 2f - 5, 10, 10), b, 1.4f);
                }
            }

            // Linha inferior com o brilho da cor do modo
            var line = new RectangleF(0, Height - 1, Width, 1);
            using (LinearGradientBrush lb = Theme.Gradient(line, Theme.Alpha(AccentA, 140), Theme.Alpha(AccentB, 0)))
                g.FillRectangle(lb, line);
        }
    }

    // =====================================================================

    public enum ButtonStyle
    {
        Primary,
        Ghost,
        Danger
    }

    /// <summary>Botão neon (preenchido, contorno ou perigo).</summary>
    public class NeonButton : GControl
    {
        public ButtonStyle Style { get; set; }
        public Color AccentA { get; set; }
        public Color AccentB { get; set; }
        public GlyphKind Glyph { get; set; }

        public NeonButton()
        {
            Style = ButtonStyle.Ghost;
            AccentA = Theme.DailyA;
            AccentB = Theme.DailyB;
            Cursor = Cursors.Hand;
            Size = new Size(140, 44);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            var r = new RectangleF(1.5f, 1.5f, Width - 3, Height - 3);
            if (Pressed) r.Offset(0, 1);
            const float radius = 10f;

            Color a = Style == ButtonStyle.Danger ? Theme.Danger : AccentA;
            Color b = Style == ButtonStyle.Danger ? Color.FromArgb(255, 110, 80) : AccentB;
            Color textColor;

            if (Style == ButtonStyle.Primary)
            {
                using (LinearGradientBrush lb = Theme.Gradient(r, Theme.Lerp(a, Color.White, HoverT * 0.12f), Theme.Lerp(b, Color.White, HoverT * 0.12f)))
                    Theme.FillRounded(g, r, radius, lb);
                textColor = Color.White;
            }
            else
            {
                Theme.FillRounded(g, r, radius, Theme.Lerp(Theme.Panel2, a, HoverT * 0.14f));
                Theme.StrokeRounded(g, r, radius, Theme.Lerp(Theme.Border, a, HoverT), 1.2f);
                textColor = Theme.Lerp(Theme.Text, Color.White, HoverT);
            }

            string text = (Text ?? "").ToUpperInvariant();
            Font font = Theme.Display(9.5f);
            SizeF ts = Theme.MeasureSpaced(g, text, font, 1.5f);
            float iconSize = Glyph == GlyphKind.None ? 0 : 15;
            float total = ts.Width + (iconSize > 0 ? iconSize + 9 : 0);
            float x = r.X + (r.Width - total) / 2f;

            if (iconSize > 0)
            {
                Color iconColor = Style == ButtonStyle.Primary ? Color.White : Theme.Lerp(Theme.Muted, a, 0.4f + HoverT * 0.6f);
                using (var ib = new SolidBrush(iconColor))
                    Theme.DrawGlyph(g, Glyph, new RectangleF(x, r.Y + (r.Height - iconSize) / 2f, iconSize, iconSize), ib, 1.6f);
                x += iconSize + 9;
            }
            Theme.DrawSpaced(g, text, font, textColor, x, r.Y + (r.Height - ts.Height) / 2f, 1.5f);
        }
    }

    // =====================================================================

    /// <summary>Cartão grande com o modo atual, brilho pulsando e chips de informação.</summary>
    public class HeroCard : GControl
    {
        public const float Inset = 8f;

        public Color AccentA { get; set; }
        public Color AccentB { get; set; }
        public string ModeName { get; set; }
        public string Reason { get; set; }
        public GlyphKind Glyph { get; set; }
        public float Pulse { get; set; }
        public string[] StatLabels { get; set; }
        public string[] StatValues { get; set; }

        public HeroCard()
        {
            AccentA = Theme.DailyA;
            AccentB = Theme.DailyB;
            ModeName = "DIA A DIA";
            Reason = "";
            Glyph = GlyphKind.Monitor;
            StatLabels = new string[0];
            StatValues = new string[0];
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            var card = new RectangleF(Inset, Inset, Width - Inset * 2, Height - Inset * 2);
            const float radius = 16f;

            Theme.Glow(g, card, radius, AccentA, 0.55f + Pulse * 0.35f);
            Theme.FillRounded(g, card, radius, Theme.Panel);

            // Luz diagonal da cor do modo no canto superior esquerdo
            using (GraphicsPath clip = Theme.Rounded(card, radius))
            {
                Region old = g.Clip;
                g.SetClip(clip, CombineMode.Intersect);
                var lightRect = new RectangleF(card.X, card.Y, card.Width * 0.8f, card.Height);
                using (LinearGradientBrush lb = Theme.Gradient(lightRect, Theme.Alpha(AccentA, 46), Theme.Alpha(AccentA, 0), 20f))
                    g.FillRectangle(lb, lightRect);

                // Grade sutil no fundo
                using (var gridPen = new Pen(Theme.Alpha(Color.White, 6)))
                {
                    for (float gx = card.X; gx < card.Right; gx += 22) g.DrawLine(gridPen, gx, card.Y, gx, card.Bottom);
                    for (float gy = card.Y; gy < card.Bottom; gy += 22) g.DrawLine(gridPen, card.X, gy, card.Right, gy);
                }
                g.Clip = old;
            }

            using (LinearGradientBrush border = Theme.Gradient(card, Theme.Alpha(AccentA, 220), Theme.Alpha(AccentB, 90), 30f))
                Theme.StrokeRounded(g, card, radius, border, 1.5f);

            float left = card.X + 22;

            // "MODO ATUAL" com ponto pulsando
            using (var dot = new SolidBrush(Theme.Alpha(AccentB, 140 + (int)(Pulse * 115))))
                g.FillEllipse(dot, left, card.Y + 23, 7, 7);
            Theme.DrawSpaced(g, "MODO ATUAL", Theme.Ui(7.5f, FontStyle.Bold), Theme.Muted, left + 14, card.Y + 18, 2f);

            // Nome do modo em gradiente
            Font big = Theme.Display(27f);
            SizeF nameSize = Theme.MeasureSpaced(g, ModeName, big, 1.5f);
            var nameRect = new RectangleF(left, card.Y + 38, Math.Max(1, nameSize.Width), Math.Max(1, nameSize.Height));
            using (LinearGradientBrush nb = Theme.Gradient(nameRect, Theme.Lerp(AccentA, Color.White, 0.15f), AccentB))
                Theme.DrawSpaced(g, ModeName, big, nb, left, card.Y + 38, 1.5f);

            Theme.DrawText(g, Reason, Theme.Ui(9f), Theme.Muted,
                new RectangleF(left, card.Y + 86, card.Width - 150, 20), wrap: false);

            // Ícone grande com halo
            var iconBox = new RectangleF(card.Right - 22 - 84, card.Y + 18, 84, 84);
            for (int i = 4; i >= 1; i--)
            {
                using (var halo = new SolidBrush(Theme.Alpha(AccentA, (int)((10 + Pulse * 8) * (5 - i) / 2f))))
                    g.FillEllipse(halo, RectangleF.Inflate(iconBox, i * 4 - 10, i * 4 - 10));
            }
            Theme.FillRounded(g, RectangleF.Inflate(iconBox, -12, -12), 18, Theme.Alpha(Theme.Panel2, 230));
            Theme.StrokeRounded(g, RectangleF.Inflate(iconBox, -12, -12), 18, Theme.Alpha(AccentA, 120), 1.2f);
            RectangleF glyphRect = RectangleF.Inflate(iconBox, -26, -26);
            using (LinearGradientBrush gb = Theme.Gradient(glyphRect, AccentA, AccentB, 60f))
                Theme.DrawGlyph(g, Glyph, glyphRect, gb, 2.6f);

            // Chips
            var chips = new RectangleF(left, card.Bottom - 18 - 46, card.Width - 44, 46);
            Theme.DrawChips(g, chips, StatLabels, StatValues, AccentB);
        }
    }

    // =====================================================================

    /// <summary>Botão grande de modo.</summary>
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
            set
            {
                if (active == value) return;
                active = value;
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
            var r = new RectangleF(Inset, Inset, Width - Inset * 2, Height - Inset * 2);
            if (Pressed) r.Offset(0, 1);
            const float radius = 14f;

            Theme.Glow(g, r, radius, AccentA, activeT * 0.9f + HoverT * 0.25f, 6, 1.3f);
            Theme.FillRounded(g, r, radius, Theme.Panel);
            if (activeT > 0.01f)
            {
                using (LinearGradientBrush lb = Theme.Gradient(r, Theme.Alpha(AccentA, (int)(255 * activeT)), Theme.Alpha(AccentB, (int)(255 * activeT)), 25f))
                    Theme.FillRounded(g, r, radius, lb);
            }
            Theme.StrokeRounded(g, r, radius,
                Theme.Lerp(Theme.Lerp(Theme.Border, AccentA, HoverT * 0.8f), Theme.Alpha(Color.White, 90), activeT), 1.3f);

            // Ícone
            var iconBox = new RectangleF(r.X + 16, r.Y + (r.Height - 42) / 2f, 42, 42);
            Theme.FillRounded(g, iconBox, 11, Theme.Lerp(Theme.Panel2, Theme.Alpha(Color.White, 45), activeT));
            RectangleF glyph = RectangleF.Inflate(iconBox, -11, -11);
            using (var ib = new SolidBrush(Theme.Lerp(AccentA, Color.White, activeT)))
                Theme.DrawGlyph(g, Glyph, glyph, ib, 2f);

            float textX = iconBox.Right + 13;
            Theme.DrawSpaced(g, Title, Theme.Display(11.5f), Theme.Lerp(Theme.Text, Color.White, activeT), textX, r.Y + r.Height / 2f - 19, 1f);
            Theme.DrawText(g, Subtitle, Theme.Ui(8.5f), Theme.Lerp(Theme.Muted, Theme.Alpha(Color.White, 215), activeT),
                new RectangleF(textX, r.Y + r.Height / 2f + 3, r.Right - textX - 8, 18), wrap: false);
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

        public event Action CheckedChanged;

        public ToggleSwitch()
        {
            AccentA = Theme.DailyA;
            AccentB = Theme.DailyB;
            Size = new Size(48, 26);
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
            Animate();
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
            var track = new RectangleF(1, 1, Width - 2, Height - 2);
            float radius = track.Height / 2f;

            Theme.FillRounded(g, track, radius, Theme.Panel2);
            if (knobT > 0.01f)
            {
                using (LinearGradientBrush lb = Theme.Gradient(track, Theme.Alpha(AccentA, (int)(255 * knobT)), Theme.Alpha(AccentB, (int)(255 * knobT))))
                    Theme.FillRounded(g, track, radius, lb);
            }
            Theme.StrokeRounded(g, track, radius, Theme.Lerp(Theme.Lerp(Theme.Border, AccentA, HoverT * 0.6f), Theme.Alpha(Color.White, 60), knobT), 1f);

            float d = track.Height - 6;
            float x = track.X + 3 + (track.Width - 6 - d) * knobT;
            using (var kb = new SolidBrush(Theme.Lerp(Theme.Muted, Color.White, knobT)))
                g.FillEllipse(kb, x, track.Y + 3, d, d);
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

        public void SetAccents(Color a, Color b)
        {
            Switch.AccentA = a;
            Switch.AccentB = b;
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
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.HighQuality(g);

            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, r, 12, Theme.Panel);
            Theme.StrokeRounded(g, r, 12, Theme.Lerp(Theme.Border, Theme.Faint, HoverT), 1f);

            float textW = Width - Switch.Width - 48;
            bool hasSub = !string.IsNullOrEmpty(Subtitle);
            float titleY = hasSub ? Height / 2f - 19 : Height / 2f - 10;
            Theme.DrawText(g, Title, Theme.UiSemibold(10f), Theme.Text, new RectangleF(16, titleY, textW, 22), wrap: false);

            if (hasSub)
            {
                float subX = 17;
                if (DotColor != Color.Empty)
                {
                    using (var halo = new SolidBrush(Theme.Alpha(DotColor, (int)(40 + 60 * DotPulse))))
                        g.FillEllipse(halo, subX - 3, Height / 2f + 4, 13, 13);
                    using (var dot = new SolidBrush(DotColor))
                        g.FillEllipse(dot, subX, Height / 2f + 7, 7, 7);
                    subX += 14;
                }
                Theme.DrawText(g, Subtitle, Theme.Ui(8.5f), Theme.Muted, new RectangleF(subX, Height / 2f + 2, textW - subX + 16, 18), wrap: false);
            }
        }
    }

    // =====================================================================

    /// <summary>Linha de status com ícone colorido e um botão de ação opcional (usada para os monitores).</summary>
    public class StatusRow : GControl
    {
        public static readonly Color Ok = Color.FromArgb(57, 255, 136);
        public static readonly Color Warn = Color.FromArgb(255, 190, 46);

        private readonly NeonButton action;

        public string Title { get; set; }
        public string Subtitle { get; set; }
        public GlyphKind Glyph { get; set; }
        public bool Warning { get; set; }

        public event Action ActionClicked;

        public StatusRow()
        {
            Title = "";
            Subtitle = "";
            Glyph = GlyphKind.Monitor;
            BackColor = Theme.Panel;
            action = new NeonButton { Size = new Size(104, 34), Visible = false, Style = ButtonStyle.Primary };
            action.AccentA = Color.FromArgb(255, 150, 30);
            action.AccentB = Warn;
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

            Color c = Warning ? Warn : Ok;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, r, 12, Theme.Panel);
            Theme.StrokeRounded(g, r, 12, Warning ? Theme.Alpha(Warn, 150) : Theme.Border, 1f);

            var iconBox = new RectangleF(14, (Height - 34) / 2f, 34, 34);
            Theme.FillRounded(g, iconBox, 9, Theme.Alpha(c, 28));
            using (var ib = new SolidBrush(c))
                Theme.DrawGlyph(g, Glyph, RectangleF.Inflate(iconBox, -9, -9), ib, 1.7f);

            float textX = iconBox.Right + 12;
            float textW = Width - textX - (action.Visible ? action.Width + 24 : 14);
            Theme.DrawText(g, Title, Theme.UiSemibold(10f), Theme.Text, new RectangleF(textX, Height / 2f - 19, textW, 20), wrap: false);
            Theme.DrawText(g, Subtitle, Theme.Ui(8.5f), Theme.Muted, new RectangleF(textX, Height / 2f + 1, textW, 18), wrap: false);
        }
    }

    // =====================================================================

    /// <summary>Teclas do atalho desenhadas como keycaps.</summary>
    public class HotkeyHint : GControl
    {
        private string[] keys = { "CTRL", "ALT", "M" };

        /// <summary>Teclas do atalho ativo; null = nenhum atalho disponível.</summary>
        public string[] Keys
        {
            get { return keys; }
            set { keys = value; Invalidate(); }
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            float x = 0;
            float y = 2;
            if (keys == null)
            {
                Theme.DrawText(g, "Atalho global indisponível: outros programas usam todas as opções.", Theme.Ui(8.5f), Theme.Muted,
                    new RectangleF(0, y + 3, Width, 20), wrap: false);
                Theme.DrawText(g, "Fechar a janela mantém o MouseModes rodando perto do relógio.", Theme.Ui(8f), Theme.Faint,
                    new RectangleF(0, y + 32, Width, 18), wrap: false);
                return;
            }
            Font keyFont = Theme.Display(8.5f);
            for (int i = 0; i < keys.Length; i++)
            {
                SizeF s = Theme.MeasureSpaced(g, keys[i], keyFont, 1f);
                var cap = new RectangleF(x, y, Math.Max(26, s.Width + 16), 24);
                Theme.FillRounded(g, new RectangleF(cap.X, cap.Y + 2, cap.Width, cap.Height), 6, Theme.Border);
                Theme.FillRounded(g, cap, 6, Theme.Panel2);
                Theme.StrokeRounded(g, cap, 6, Theme.Border, 1f);
                using (var b = new SolidBrush(Theme.Text))
                    Theme.DrawSpacedCentered(g, keys[i], keyFont, b, cap, 1f);
                x = cap.Right + 6;
                if (i < keys.Length - 1)
                {
                    Theme.DrawText(g, "+", Theme.Ui(9f), Theme.Faint, new RectangleF(x - 2, y + 2, 12, 20));
                    x += 12;
                }
            }
            Theme.DrawText(g, "alterna os modos, até dentro do jogo", Theme.Ui(8.5f), Theme.Muted,
                new RectangleF(x + 6, y + 3, Width - x - 6, 20), wrap: false);
            Theme.DrawText(g, "Fechar a janela mantém o MouseModes rodando perto do relógio.", Theme.Ui(8f), Theme.Faint,
                new RectangleF(0, y + 32, Width, 18), wrap: false);
        }
    }

    // =====================================================================

    /// <summary>Título de seção em caixa alta, com texto opcional à direita.</summary>
    public class SectionLabel : GControl
    {
        public string RightText { get; set; }
        public Color RightColor { get; set; }

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
            Theme.DrawSpaced(g, (Text ?? "").ToUpperInvariant(), Theme.Ui(7.5f, FontStyle.Bold), Theme.Muted, 0, 3, 1.8f);
            if (!string.IsNullOrEmpty(RightText))
                Theme.DrawText(g, RightText, Theme.Display(10f), RightColor, new RectangleF(0, 0, Width, Height), StringAlignment.Far, StringAlignment.Center, false);
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
            float cy = Height / 2f;
            var track = new RectangleF(Pad, cy - 3, Width - Pad * 2, 6);
            Theme.FillRounded(g, track, 3, Theme.Panel2);

            // Marcas
            using (var tick = new SolidBrush(Theme.Faint))
            {
                for (int v = minimum; v <= maximum; v++)
                {
                    if (maximum - minimum > 30 && v % 5 != 0) continue;
                    g.FillRectangle(tick, XFor(v) - 0.5f, cy + 8, 1, 3);
                }
            }
            if (Marker >= minimum && Marker <= maximum)
            {
                using (var mb = new SolidBrush(Theme.Alpha(AccentB, 200)))
                    g.FillRectangle(mb, XFor(Marker) - 1, cy + 7, 2, 6);
            }

            float kx = XFor(value);
            var filled = new RectangleF(track.X, track.Y, Math.Max(6, kx - track.X), track.Height);
            using (LinearGradientBrush lb = Theme.Gradient(filled, AccentA, AccentB))
                Theme.FillRounded(g, filled, 3, lb);

            float knob = 18 + HoverT * 2;
            var knobRect = new RectangleF(kx - knob / 2, cy - knob / 2, knob, knob);
            for (int i = 3; i >= 1; i--)
                using (var halo = new SolidBrush(Theme.Alpha(AccentA, (int)((22 + HoverT * 20) * (4 - i) / 3f))))
                    g.FillEllipse(halo, RectangleF.Inflate(knobRect, i * 3, i * 3));
            g.FillEllipse(Brushes.White, knobRect);
            if (Focused)
                using (var fp = new Pen(AccentB, 1.5f)) g.DrawEllipse(fp, RectangleF.Inflate(knobRect, 3, 3));
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
            float w = (Width - 8f) / Math.Max(1, options.Length);
            return new RectangleF(4 + i * w, 4, w, Height - 8);
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
            var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, outer, 11, Theme.Panel);
            Theme.StrokeRounded(g, outer, 11, Theme.Border, 1f);

            for (int i = 0; i < options.Length; i++)
            {
                RectangleF r = Segment(i);
                bool isSel = i == selected;
                if (isSel)
                {
                    using (LinearGradientBrush lb = Theme.Gradient(r, AccentA, AccentB))
                        Theme.FillRounded(g, r, 8, lb);
                }
                else if (i == hoverIndex)
                {
                    Theme.FillRounded(g, r, 8, Theme.Panel2);
                }
                using (var tb = new SolidBrush(isSel ? Color.White : i == hoverIndex ? Theme.Text : Theme.Muted))
                    Theme.DrawSpacedCentered(g, options[i], Theme.Display(8.5f), tb, r, 1.2f);
            }
        }
    }

    // =====================================================================

    /// <summary>Moldura arredondada para TextBox/ListBox no tema escuro.</summary>
    public class FieldHost : GControl
    {
        private readonly Control inner;

        public FieldHost(Control inner)
        {
            this.inner = inner;
            Accent = Theme.DailyB;
            BackColor = Theme.Panel2;
            inner.BackColor = Theme.Panel2;
            inner.ForeColor = Theme.Text;
            Controls.Add(inner);
            inner.GotFocus += (s, e) => Invalidate();
            inner.LostFocus += (s, e) => Invalidate();
            // Barra de rolagem escura (tema nativo do Windows 10 1809+; em versões antigas fica a clara).
            inner.HandleCreated += (s, e) =>
            {
                try { SetWindowTheme(inner.Handle, "DarkMode_Explorer", null); } catch { }
            };
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        public Color Accent { get; set; }

        protected override bool Interactive { get { return false; } }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            inner.Bounds = new Rectangle(12, 10, Math.Max(10, Width - 24), Math.Max(10, Height - 20));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.HighQuality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, r, 10, Theme.Panel2);
            Theme.StrokeRounded(g, r, 10, inner.ContainsFocus ? Accent : Theme.Border, inner.ContainsFocus ? 1.4f : 1f);
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
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            if (selected)
            {
                Theme.FillRounded(g, r, 10, Theme.Panel2);
                using (LinearGradientBrush lb = Theme.Gradient(new RectangleF(r.X, r.Y + 10, 3, r.Height - 20), AccentA, AccentB, 90f))
                    Theme.FillRounded(g, new RectangleF(r.X, r.Y + 10, 3, r.Height - 20), 1.5f, lb);
            }
            else if (HoverT > 0.01f)
            {
                Theme.FillRounded(g, r, 10, Theme.Alpha(Theme.Panel2, (int)(200 * HoverT)));
            }

            Color fg = selected ? Color.White : Theme.Lerp(Theme.Muted, Theme.Text, HoverT);
            var icon = new RectangleF(16, (Height - 16) / 2f, 16, 16);
            using (var ib = new SolidBrush(selected ? AccentB : fg))
                Theme.DrawGlyph(g, Glyph, icon, ib, 1.6f);
            SizeF ts = Theme.MeasureSpaced(g, Text.ToUpperInvariant(), Theme.Display(9f), 1.4f);
            Theme.DrawSpaced(g, Text.ToUpperInvariant(), Theme.Display(9f), fg, 42, (Height - ts.Height) / 2f, 1.4f);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            var card = new RectangleF(8, 8, Width - 16, Height - 16);
            Theme.Glow(g, card, 16, AccentA, 0.4f + Pulse * 0.4f);
            Theme.FillRounded(g, card, 16, Theme.Panel);
            using (LinearGradientBrush border = Theme.Gradient(card, Theme.Alpha(AccentA, 200), Theme.Alpha(AccentB, 70), 30f))
                Theme.StrokeRounded(g, card, 16, border, 1.5f);

            Font big = Theme.Display(46f);
            Font unit = Theme.Display(16f);
            SizeF vs = Theme.MeasureSpaced(g, Value, big, 1f);
            SizeF us = Theme.MeasureSpaced(g, Unit, unit, 1f);
            float total = vs.Width + 10 + us.Width;
            float x = card.X + (card.Width - total) / 2f;
            float y = card.Y + 14;
            var valueRect = new RectangleF(x, y, Math.Max(1, vs.Width), Math.Max(1, vs.Height));
            using (LinearGradientBrush vb = Theme.Gradient(valueRect, Theme.Lerp(AccentA, Color.White, 0.1f), AccentB))
                Theme.DrawSpaced(g, Value, big, vb, x, y, 1f);
            Theme.DrawSpaced(g, Unit, unit, Theme.Muted, x + vs.Width + 10, y + vs.Height - us.Height - 8, 1f);

            Theme.DrawText(g, Caption, Theme.Ui(9f), Theme.Muted,
                new RectangleF(card.X, card.Bottom - 34, card.Width, 22), StringAlignment.Center, StringAlignment.Center, false);
        }
    }

    /// <summary>Linha de chips (rótulo + valor).</summary>
    public class ChipRow : GControl
    {
        public string[] Labels { get; set; }
        public string[] Values { get; set; }
        public Color Accent { get; set; }

        public ChipRow()
        {
            Labels = new string[0];
            Values = new string[0];
            Accent = Theme.PerfB;
        }

        protected override bool Interactive { get { return false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            Theme.DrawChips(g, new RectangleF(0, 0, Width, Height), Labels, Values, Accent);
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
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, r, 12, Theme.Panel);
            Theme.StrokeRounded(g, r, 12, Theme.Border, 1f);

            var plot = new RectangleF(r.X + 12, r.Y + 12, r.Width - 24, r.Height - 24);
            using (var grid = new Pen(Theme.Alpha(Color.White, 10)))
            {
                for (int i = 1; i < 4; i++)
                {
                    float y = plot.Y + plot.Height * i / 4f;
                    g.DrawLine(grid, plot.X, y, plot.Right, y);
                }
            }

            if (samples.Length < 2)
            {
                Theme.DrawText(g, "mexa o mouse para ver as leituras", Theme.Ui(8.5f), Theme.Faint, plot, StringAlignment.Center, StringAlignment.Center);
                return;
            }

            var sorted = (double[])samples.Clone();
            Array.Sort(sorted);
            double median = sorted[sorted.Length / 2];
            double top = Math.Max(median * 2.5, 0.0001);

            float barW = plot.Width / samples.Length;
            using (LinearGradientBrush lb = Theme.Gradient(new RectangleF(plot.X, plot.Y, plot.Width, plot.Height), AccentB, AccentA, 90f))
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    float h = (float)Math.Min(1.0, samples[i] / top) * plot.Height;
                    g.FillRectangle(lb, plot.X + i * barW, plot.Bottom - h, Math.Max(1f, barW - 1f), h);
                }
            }

            // Linha da mediana
            float my = plot.Bottom - (float)(median / top) * plot.Height;
            using (var mp = new Pen(Theme.Alpha(Color.White, 120), 1f) { DashStyle = DashStyle.Dash })
                g.DrawLine(mp, plot.X, my, plot.Right, my);
            Theme.DrawText(g, median.ToString("0.00") + " ms", Theme.Ui(7.5f, FontStyle.Bold), Theme.Text,
                new RectangleF(plot.Right - 80, my - 16, 80, 14), StringAlignment.Far);
        }
    }
}
