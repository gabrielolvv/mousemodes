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

            Controls.Add(Note(L.T("Lido a cada 3 s com o jogo aberto no Performance. Mostra o que pesou em segundo plano; o FPS não é medido.",
                                  "Read every 3 s with the game open in Performance. Shows what weighed in the background; FPS is not measured."),
                              20, y, 440, 32, Theme.Faint, 8f));
            y += 38;

            var close = new NeonButton { Text = L.T("Fechar", "Close"), Location = new Point(20, y), Size = new Size(440, 42), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            close.Click += (s, e) => Close();
            Controls.Add(close);
            ClientSize = new Size(480, y + 42 + 18);

            FinishLayout();
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
