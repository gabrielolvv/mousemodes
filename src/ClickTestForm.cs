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

        // Propriedades, não campos: o texto segue o idioma escolhido ao abrir o app.
        private static string[] Names
        {
            get { return new[] { L.T("Esquerdo", "Left"), L.T("Direito", "Right"), L.T("Meio", "Middle"), L.T("Lateral 1", "Side 1"), L.T("Lateral 2", "Side 2") }; }
        }
        private static string[] Short
        {
            get { return new[] { L.T("ESQ", "LEFT"), L.T("DIR", "RIGHT"), L.T("MEIO", "MID"), L.T("LAT 1", "SIDE1"), L.T("LAT 2", "SIDE2") }; }
        }
        private static readonly ushort[] DownFlags = { RawMouse.LeftDown, RawMouse.RightDown, RawMouse.MiddleDown, RawMouse.Button4Down, RawMouse.Button5Down };
        private static readonly ushort[] UpFlags = { RawMouse.LeftUp, RawMouse.RightUp, RawMouse.MiddleUp, RawMouse.Button4Up, RawMouse.Button5Up };

        private readonly RawMouse raw = new RawMouse();
        private readonly long clockStart = Stopwatch.GetTimestamp(); // a hora das leituras vem nesta escala
        private readonly ButtonTile[] tiles = new ButtonTile[5];
        private readonly double[] lastDown = { -1, -1, -1, -1, -1 };
        private readonly double[] minGhost = { double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue };
        private readonly StatusRow summary;
        private readonly TextBox events;
        private readonly List<string> eventLines = new List<string>();
        private readonly EmptyHint noEvents;

        public ClickTestForm() : base(L.T("Teste de clique", "Click test"), true)
        {
            CloseOnEscape = true;
            TitleBar.AccentA = Theme.PerfA;
            TitleBar.AccentB = Theme.PerfB;

            // Esqueleto das ferramentas, só um pouco mais largo: os cinco quadrados (84 + 10 entre eles) pedem 460 de coluna
            // para "sem fantasma" caber inteiro.
            const int width = 460, tileW = 84;
            Label intro = MeasuredNote(L.T("Clique normalmente com cada botão, sem pressa, uns 30 cliques em cada. " +
                                           "Se aparecer clique fantasma, o switch daquele botão está gasto.",
                                           "Click normally with each button, without rushing, about 30 clicks each. " +
                                           "If a ghost click shows up, that button's switch is worn."), 20, 56, width, Theme.TextSecondary, 9f);
            Controls.Add(intro);

            int tilesY = intro.Bottom + 12;
            for (int i = 0; i < 5; i++)
            {
                tiles[i] = new ButtonTile { Caption = Names[i], Location = new Point(20 + i * (tileW + 10), tilesY), Size = new Size(tileW, 104) };
                Controls.Add(tiles[i]);
            }

            summary = new StatusRow { Location = new Point(20, tilesY + 104 + 12), Size = new Size(width, 56), Glyph = GlyphKind.Mouse };
            Controls.Add(summary);

            var lastLabel = new SectionLabel { Text = L.T("Últimos cliques", "Last clicks"), Location = new Point(20, summary.Bottom + 20), Size = new Size(width, 20) };
            Controls.Add(lastLabel);
            events = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.None,
                BorderStyle = BorderStyle.None,
                Font = Theme.Mono(9f),
                ContextMenuStrip = new ContextMenuStrip(), // sem menu no botão direito: o clique é do teste
                TabStop = false
            };
            var eventsHost = new FieldHost(events) { Location = new Point(20, lastLabel.Bottom + 8), Size = new Size(width, 150), Accent = Theme.PerfB };
            // Sem cliques ainda: estado vazio por cima da caixa.
            noEvents = new EmptyHint
            {
                Glyph = GlyphKind.Mouse,
                Text = L.T("Clique com qualquer botão para começar", "Click with any button to start"),
                BackColor = Theme.Surface2,
                Bounds = new Rectangle(2, 2, width - 4, 146)
            };
            events.Visible = false; // a caixa aparece com o primeiro clique
            eventsHost.Controls.Add(noEvents);
            noEvents.BringToFront();
            Controls.Add(eventsHost);

            var reset = new NeonButton { Text = L.T("Zerar", "Reset"), Location = new Point(20, eventsHost.Bottom + 16), Size = new Size(width, 36), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            reset.Click += (s, e) => Reset();
            Controls.Add(reset);
            ClientSize = new Size(width + 40, reset.Bottom + 20);

            UpdateSummary();
            FinishLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            string error;
            // Sem "sink": só conta clique com esta tela em primeiro plano.
            if (!raw.Register(this, false, (dx, dy, flags, timestamp) => { if (flags != 0) HandleButtons(flags, timestamp); }, out error))
            {
                summary.Warning = true;
                summary.Title = L.T("Raw Input indisponível", "Raw Input unavailable");
                summary.Subtitle = error;
                Log.Warn(L.T("Teste de clique: Raw Input indisponível (", "Click test: Raw Input unavailable (") + error + ")");
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            raw.Dispose();
            base.OnFormClosed(e);
        }

        private void HandleButtons(ushort flags, long timestamp)
        {
            // A hora em que o Windows entregou o clique (thread do mouse): uma pausa da tela não junta dois cliques.
            double now = (timestamp - clockStart) * 1000.0 / Stopwatch.Frequency;
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
                        Log.Warn(L.T("Teste de clique: clique fantasma no botão ", "Click test: ghost click on the ") + Names[i] + L.T(" (", " button (") + gap.ToString("0") + " ms)");
                    }
                    AddEvent(Short[i].PadRight(5) + (ghost ? L.T("  FANTASMA  ", "  GHOST     ") : L.T("  clique    ", "  click     ")) +
                             (gap >= 0 ? gap.ToString("0").PadLeft(6) + L.T(" ms desde o anterior", " ms since the previous") : L.T("      primeiro clique", "      first click")));
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
            noEvents.Visible = false;
            events.Visible = true;
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
                summary.Tone = StatusTone.Neutral; // ainda sem resultado
                summary.Title = L.T("Aguardando cliques", "Waiting for clicks");
                summary.Subtitle = L.T("Clique em qualquer lugar desta janela", "Click anywhere in this window");
            }
            else if (ghosts == 0)
            {
                summary.Warning = false;
                summary.Title = L.T("Nenhum clique fantasma", "No ghost clicks");
                summary.Subtitle = total + (total == 1 ? L.T(" clique testado", " click tested") : L.T(" cliques testados", " clicks tested")) +
                    (total < 30 ? L.T(" · continue clicando", " · keep clicking") : " · switches ok");
            }
            else
            {
                summary.Warning = true;
                summary.Title = ghosts + (ghosts == 1 ? L.T(" clique fantasma", " ghost click") : L.T(" cliques fantasmas", " ghost clicks"));
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
            noEvents.Visible = true;
            events.Visible = false;
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
                ModePalette p = Theme.PerformancePalette;

                // Pressionado: cor sólida (do modo, ou vermelho com fantasma) e texto escuro/branco por cima.
                Color fg, sub;
                if (IsDown)
                {
                    Theme.FillRounded(g, r, Theme.Radius.Md, bad ? Theme.DangerSolid : p.Fill);
                    fg = sub = bad ? Color.White : p.OnFill;
                }
                else
                {
                    Theme.FillRounded(g, r, Theme.Radius.Md, bad ? Theme.Alpha(Theme.Danger, 22) : Theme.Surface);
                    Theme.StrokeRounded(g, r, Theme.Radius.Md, bad ? Theme.Alpha(Theme.Danger, 160) : Theme.Border, 1f);
                    fg = Theme.Text;
                    sub = bad ? Theme.DangerText : Theme.TextTertiary;
                }

                Theme.DrawText(g, Caption, Theme.UiSemibold(Theme.TypeScale.Caption), IsDown ? fg : Theme.TextSecondary,
                    new RectangleF(r.X + 4, r.Y + 10, r.Width - 8, 18), StringAlignment.Center, StringAlignment.Center, false);
                Theme.DrawText(g, Clicks.ToString(), Theme.Num(22f), fg,
                    new RectangleF(r.X, r.Y + 30, r.Width, 40), StringAlignment.Center, StringAlignment.Center, false);
                Theme.DrawText(g, Ghosts == 0 ? L.T("sem fantasma", "no ghosts") : Ghosts + (Ghosts == 1 ? L.T(" fantasma", " ghost") : L.T(" fantasmas", " ghosts")),
                    Theme.Ui(Theme.TypeScale.Caption), sub,
                    new RectangleF(r.X - 4, r.Bottom - 28, r.Width + 8, 20), StringAlignment.Center, StringAlignment.Center, false);
            }
        }
    }
}
