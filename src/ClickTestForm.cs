using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>
    /// Teste de clique duplo: detecta "cliques fantasmas" (o switch gasto registra dois cliques
    /// quando você deu um). Lê os botões direto do mouse via Raw Input.
    /// </summary>
    public class ClickTestForm : GamerWindow
    {
        /// <summary>Dois cliques do mesmo botão mais rápidos que isso não são humanos.</summary>
        private const double GhostMs = 60;

        private static readonly string[] Names = { "Esquerdo", "Direito", "Meio", "Lateral 1", "Lateral 2" };
        private static readonly string[] Short = { "ESQ", "DIR", "MEIO", "LAT 1", "LAT 2" };
        private static readonly ushort[] DownFlags = { RawMouse.LeftDown, RawMouse.RightDown, RawMouse.MiddleDown, RawMouse.Button4Down, RawMouse.Button5Down };
        private static readonly ushort[] UpFlags = { RawMouse.LeftUp, RawMouse.RightUp, RawMouse.MiddleUp, RawMouse.Button4Up, RawMouse.Button5Up };

        private readonly RawMouse raw = new RawMouse();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly ButtonTile[] tiles = new ButtonTile[5];
        private readonly double[] lastDown = { -1, -1, -1, -1, -1 };
        private readonly double[] minGhost = { double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue };
        private readonly StatusRow summary;
        private readonly TextBox events;
        private readonly List<string> eventLines = new List<string>();

        public ClickTestForm() : base("Teste de clique", true)
        {
            CloseOnEscape = true;
            ClientSize = new Size(520, 560);
            TitleBar.AccentA = Theme.DailyA;
            TitleBar.AccentB = Theme.DailyB;

            Controls.Add(Note("Clique normalmente com cada botão, sem pressa, uns 30 cliques em cada. " +
                              "Se aparecer clique fantasma, o switch daquele botão está gasto.", 20, 56, 480, 36, Theme.Muted, 9f));

            for (int i = 0; i < 5; i++)
            {
                tiles[i] = new ButtonTile { Caption = Short[i], Location = new Point(20 + i * 98, 100), Size = new Size(88, 112) };
                Controls.Add(tiles[i]);
            }

            summary = new StatusRow { Location = new Point(20, 226), Size = new Size(480, 60), Glyph = GlyphKind.Mouse };
            Controls.Add(summary);

            Controls.Add(new SectionLabel { Text = "Últimos cliques", Location = new Point(20, 300), Size = new Size(480, 20) });
            events = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.None,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9f),
                ContextMenuStrip = new ContextMenuStrip(), // sem menu no botão direito: o clique é do teste
                TabStop = false
            };
            Controls.Add(new FieldHost(events) { Location = new Point(20, 324), Size = new Size(480, 160), Accent = Theme.DailyB });

            var reset = new NeonButton { Text = "Zerar", Location = new Point(20, 498), Size = new Size(480, 44) };
            reset.Click += (s, e) => Reset();
            Controls.Add(reset);

            UpdateSummary();
            FinishLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            string error;
            if (!raw.Register(Handle, false, out error))
            {
                summary.Warning = true;
                summary.Title = "Raw Input indisponível";
                summary.Subtitle = error;
                Log.Warn("Teste de clique: Raw Input indisponível (" + error + ")");
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            raw.Unregister();
            raw.Dispose();
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == RawMouse.WM_INPUT)
            {
                int dx, dy;
                ushort flags;
                if (raw.Read(m.LParam, out dx, out dy, out flags) && flags != 0) HandleButtons(flags);
            }
            base.WndProc(ref m);
        }

        private void HandleButtons(ushort flags)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            for (int i = 0; i < 5; i++)
            {
                if ((flags & DownFlags[i]) != 0)
                {
                    double gap = lastDown[i] >= 0 ? now - lastDown[i] : -1;
                    bool ghost = gap >= 0 && gap < GhostMs;
                    lastDown[i] = now;

                    tiles[i].Clicks++;
                    tiles[i].IsDown = true;
                    if (ghost)
                    {
                        tiles[i].Ghosts++;
                        if (gap < minGhost[i]) minGhost[i] = gap;
                        Log.Warn("Teste de clique: clique fantasma no botão " + Names[i] + " (" + gap.ToString("0") + " ms)");
                    }
                    AddEvent(Short[i].PadRight(5) + (ghost ? "  FANTASMA  " : "  clique    ") +
                             (gap >= 0 ? gap.ToString("0").PadLeft(6) + " ms desde o anterior" : "      primeiro clique"));
                    tiles[i].Invalidate();
                    UpdateSummary();
                }
                if ((flags & UpFlags[i]) != 0)
                {
                    tiles[i].IsDown = false;
                    tiles[i].Invalidate();
                }
            }
        }

        private void AddEvent(string line)
        {
            eventLines.Insert(0, line);
            if (eventLines.Count > 40) eventLines.RemoveAt(eventLines.Count - 1);
            events.Text = string.Join(Environment.NewLine, eventLines.ToArray());
        }

        private void UpdateSummary()
        {
            int total = 0, ghosts = 0;
            var bad = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                total += tiles[i].Clicks;
                ghosts += tiles[i].Ghosts;
                if (tiles[i].Ghosts > 0)
                    bad.Add(Names[i] + ": " + tiles[i].Ghosts + "x (" + minGhost[i].ToString("0") + " ms)");
            }

            if (total == 0)
            {
                summary.Warning = false;
                summary.Title = "Aguardando cliques";
                summary.Subtitle = "Clique em qualquer lugar desta janela";
            }
            else if (ghosts == 0)
            {
                summary.Warning = false;
                summary.Title = "Nenhum clique fantasma";
                summary.Subtitle = total + (total == 1 ? " clique testado" : " cliques testados") + (total < 30 ? " · continue clicando" : " · switches ok");
            }
            else
            {
                summary.Warning = true;
                summary.Title = ghosts + (ghosts == 1 ? " clique fantasma" : " cliques fantasmas");
                summary.Subtitle = string.Join("  ·  ", bad.ToArray());
            }
            summary.Invalidate();
        }

        private void Reset()
        {
            for (int i = 0; i < 5; i++)
            {
                lastDown[i] = -1;
                minGhost[i] = double.MaxValue;
                tiles[i].Clicks = 0;
                tiles[i].Ghosts = 0;
                tiles[i].IsDown = false;
                tiles[i].Invalidate();
            }
            eventLines.Clear();
            events.Text = "";
            UpdateSummary();
        }

        /// <summary>Quadrado de um botão: acende quando pressionado, fica vermelho com fantasmas.</summary>
        private class ButtonTile : GControl
        {
            public string Caption { get; set; }
            public int Clicks { get; set; }
            public int Ghosts { get; set; }
            public bool IsDown { get; set; }

            protected override bool Interactive { get { return false; } }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = Begin(e);
                var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
                bool bad = Ghosts > 0;
                Color a = bad ? Theme.Danger : Theme.DailyA;
                Color b = bad ? Color.FromArgb(255, 110, 80) : Theme.DailyB;

                if (IsDown)
                {
                    using (LinearGradientBrush lb = Theme.Gradient(r, a, b, 90f))
                        Theme.FillRounded(g, r, 12, lb);
                }
                else
                {
                    Theme.FillRounded(g, r, 12, Theme.Panel);
                }
                Theme.StrokeRounded(g, r, 12, bad ? Theme.Alpha(Theme.Danger, 200) : Theme.Border, bad ? 1.5f : 1f);

                Color fg = IsDown ? Color.White : Theme.Muted;
                using (var tb = new SolidBrush(fg))
                    Theme.DrawSpacedCentered(g, Caption, Theme.Ui(7.5f, FontStyle.Bold), tb, new RectangleF(r.X, r.Y + 8, r.Width, 18), 1.4f);
                Theme.DrawText(g, Clicks.ToString(), Theme.Display(22f), IsDown ? Color.White : Theme.Text,
                    new RectangleF(r.X, r.Y + 28, r.Width, 40), StringAlignment.Center, StringAlignment.Center, false);
                Theme.DrawText(g, Ghosts == 0 ? "sem fantasma" : Ghosts + (Ghosts == 1 ? " fantasma" : " fantasmas"),
                    Theme.Ui(7.5f, FontStyle.Bold), IsDown ? Color.White : bad ? Theme.Danger : Theme.Faint,
                    new RectangleF(r.X, r.Bottom - 30, r.Width, 20), StringAlignment.Center, StringAlignment.Center, false);
            }
        }
    }
}
