using System;
using System.Drawing;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>Raio-X da partida: quem pesou em segundo plano e o que fazer com cada um, num clique.</summary>
    public class MatchReportForm : GamerWindow
    {
        private const int RowHeight = 62;

        /// <summary>Launchers (começo do nome): o jogo depende deles, e fechar um pode derrubar a partida.</summary>
        internal static readonly string[] Launchers =
        {
            "steam", "EpicGamesLauncher", "EpicWebHelper", "Battle.net", "Agent", "RiotClient", "EADesktop",
            "EABackgroundService", "UbisoftConnect", "upc", "GalaxyClient", "FiveM"
        };

        private readonly AppConfig cfg;
        private readonly Action changed;

        /// <param name="changed">Chamado quando a pessoa muda algo (para gravar o config).</param>
        public MatchReportForm(MatchReport report, AppConfig cfg, Action changed) : base(L.T("Raio-X da partida", "Match X-ray"), true)
        {
            this.cfg = cfg;
            this.changed = changed;
            CloseOnEscape = true;
            TitleBar.AccentA = Theme.PerfA;
            TitleBar.AccentB = Theme.PerfB;

            var title = Note(report.Game + "  ·  " + MatchReport.FormatDuration(report.Duration), 20, 56, 440, 28, Theme.Text, 14f);
            title.Font = Theme.Ui(14f, FontStyle.Bold);
            Controls.Add(title);
            int count = report.Culprits.Count;
            Controls.Add(Note(count == 0
                ? L.T("Nada pesou em segundo plano durante a partida.", "Nothing weighed on the PC in the background during the match.")
                : count == 1
                    ? L.T("1 programa pesou em segundo plano durante a partida:", "1 program weighed on the PC in the background during the match:")
                    : L.T(count + " programas pesaram em segundo plano durante a partida:", count + " programs weighed on the PC in the background during the match:"),
                20, 86, 440, 20, Theme.Muted, 9f));

            int y = 116;
            foreach (Culprit c in report.Culprits)
            {
                AddRow(c, y);
                y += RowHeight;
            }

            if (count > 0)
            {
                y += 4;
                if (cfg.CalmBackground)
                {
                    Controls.Add(Note(L.T("O segundo plano calmo já está ligado: no Performance, estes programas ficam mais leves sem fechar.",
                                          "Calm background is already on: in Performance, these programs get lighter without closing."),
                                      20, y, 440, 36, Theme.DailyB, 8.5f));
                    y += 42;
                }
                else
                {
                    var calm = new NeonButton { Text = L.T("Ligar o segundo plano calmo", "Turn on calm background"), Style = ButtonStyle.Primary, Location = new Point(20, y), Size = new Size(440, 42), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
                    var calmNote = Note(L.T("Deixa os programas em segundo plano mais leves durante o jogo, sem fechar nenhum (Ajustes → Turbo).",
                                            "Makes background programs lighter during the game without closing any (Settings → Turbo)."),
                                        20, y + 46, 440, 32, Theme.Faint, 8f);
                    calm.Click += (s, e) =>
                    {
                        cfg.CalmBackground = true;
                        Log.Info(L.T("Raio-X: segundo plano calmo ligado", "X-ray: calm background turned on"));
                        if (this.changed != null) this.changed();
                        calm.Enabled = false;
                        calm.Text = L.T("Segundo plano calmo ligado", "Calm background on");
                        calm.Invalidate();
                    };
                    Controls.Add(calm);
                    Controls.Add(calmNote);
                    y += 82;
                }
            }

            if (report.Network != null) y = AddNetwork(report.Network, y);

            Controls.Add(Note(L.T("Lido a cada 3 s com o jogo aberto no Performance; a rede, com um ping por segundo ao roteador e ao 1.1.1.1. " +
                                  "Mostra o que pesou; o FPS não é medido.",
                                  "Read every 3 s with the game open in Performance; the network, with one ping per second to the router and to 1.1.1.1. " +
                                  "Shows what weighed; FPS is not measured."),
                              20, y, 440, 32, Theme.Faint, 8f));
            y += 38;

            var close = new NeonButton { Text = L.T("Fechar", "Close"), Location = new Point(20, y), Size = new Size(440, 42), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            close.Click += (s, e) => Close();
            Controls.Add(close);
            ClientSize = new Size(480, y + 42 + 18);

            FinishLayout();
        }

        /// <summary>Relatório maior que a tela (muitos programas e a rede, com a escala do Windows alta): rola.</summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            if (Height <= area.Height - 20) return;
            AutoScroll = true;
            Height = area.Height - 20;
            Top = area.Top + 10;
        }

        /// <summary>A rede: resumo, de onde vieram os picos e os piores deles. Devolve o y seguinte.</summary>
        private int AddNetwork(NetworkReport net, int y)
        {
            Controls.Add(new SectionLabel { Text = L.T("Rede", "Network"), Location = new Point(20, y), Size = new Size(440, 20) });
            y += 24;
            Controls.Add(Note(net.Summary(), 20, y, 440, 20, Theme.Text, 9.5f));
            y += 22;
            Controls.Add(Note(net.VerdictText(), 20, y, 440, 36, net.Verdict == NetVerdict.Stable ? Theme.DailyB : Theme.PerfB, 9f));
            y += 40;
            foreach (NetSpike spike in net.Spikes)
            {
                // Uma ou duas linhas, conforme o texto: cada item ocupa só a altura que precisa.
                Label line = Note("• " + NetworkReport.Describe(spike), 20, y, 440, 32, Theme.Muted, 8.5f);
                line.Height = TextRenderer.MeasureText(line.Text, line.Font, new Size(440, int.MaxValue), TextFormatFlags.WordBreak).Height + 2;
                Controls.Add(line);
                y += line.Height + 4;
            }
            if (net.SpikeCount > net.Spikes.Count)
            {
                Controls.Add(Note(L.T("e mais " + (net.SpikeCount - net.Spikes.Count) + " pico(s) menores",
                                      "and " + (net.SpikeCount - net.Spikes.Count) + " smaller spike(s)"), 20, y, 440, 18, Theme.Faint, 8f));
                y += 22;
            }
            return y + 6;
        }

        private void AddRow(Culprit c, int y)
        {
            var name = Note(c.Name, 20, y, 270, 22, Theme.Text, 11f);
            name.Font = Theme.Ui(11f, FontStyle.Bold);
            var detail = Note(MatchReport.Describe(c), 20, y + 24, 270, 20, Theme.PerfB, 8.5f);
            Controls.Add(name);
            Controls.Add(detail);

            if (GameBooster.Matches(c.Name, Launchers))
            {
                Controls.Add(Note(L.T("launcher: melhor manter", "launcher: keep it open"), 296, y + 10, 164, 20, Theme.Faint, 8.5f));
                return;
            }
            if (GameBooster.Matches(c.Name, BackgroundCalmer.NeverCalm))
            {
                // Voz, música, overlay, software do mouse: fechar atrapalharia mais do que ajuda.
                Controls.Add(Note(L.T("melhor manter aberto", "better kept open"), 296, y + 10, 164, 20, Theme.Faint, 8.5f));
                return;
            }
            if (InCloseList(c.Name))
            {
                Controls.Add(Note(L.T("já fecha ao jogar", "already auto-closed"), 296, y + 10, 164, 20, Theme.DailyB, 8.5f));
                return;
            }

            var closeIt = new NeonButton { Text = L.T("Fechar ao jogar", "Auto-close"), Location = new Point(296, y + 4), Size = new Size(164, 36), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            closeIt.Click += (s, e) =>
            {
                if (!InCloseList(c.Name)) cfg.AppsToClose.Add(c.Name);
                cfg.CloseAppsOnPerformance = true;
                Log.Info(L.T("Raio-X: ", "X-ray: ") + c.Name + L.T(" vai ser fechado ao entrar no Performance", " will be closed when entering Performance"));
                if (changed != null) changed();
                closeIt.Enabled = false;
                closeIt.Text = L.T("Na lista", "Listed");
                closeIt.Invalidate();
            };
            Controls.Add(closeIt);
        }

        private bool InCloseList(string name)
        {
            return cfg.AppsToClose.Exists(a => string.Equals(GameDetector.Normalize(a), name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
