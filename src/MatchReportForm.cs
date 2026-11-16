using System;
using System.Collections.Generic;
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
        /// <param name="record">A mesma partida no histórico, com os ajustes que valiam nela (null = sem comparação).</param>
        /// <param name="previous">A partida anterior do mesmo jogo no histórico (null se não há).</param>
        public MatchReportForm(MatchReport report, AppConfig cfg, Action changed, MatchRecord record = null, MatchRecord previous = null)
            : base(L.T("Raio-X da partida", "Match X-ray"), true)
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
            // A data da partida vai nesta linha (no título não cabe com nomes como VALORANT-Win64-Shipping).
            Label subtitle = Note((record != null ? record.When() + "  ·  " : "") + (count == 0
                ? L.T("Nada pesou em segundo plano durante a partida.", "Nothing weighed on the PC in the background during the match.")
                : count == 1
                    ? L.T("1 programa pesou em segundo plano durante a partida:", "1 program weighed on the PC in the background during the match:")
                    : L.T(count + " programas pesaram em segundo plano durante a partida:", count + " programs weighed on the PC in the background during the match:")),
                20, 86, 440, 20, Theme.Muted, 9f);
            subtitle.Height = Math.Max(20, Measure(subtitle));
            Controls.Add(subtitle);

            int y = 86 + subtitle.Height + 10;
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

            y = AddFrames(report, y);
            if (report.CpuSpeed != null) y = AddCpuSpeed(report.CpuSpeed, y);
            if (report.Network != null) y = AddNetwork(report.Network, y);
            if (record != null) y = AddComparison(record, previous, y);

            var footer = Note(L.T("Lido a cada 3 s com o jogo aberto no Performance; a rede, com um ping por segundo ao roteador e ao 1.1.1.1 " +
                                  "(dá para desligar em Ajustes → Privacidade). ",
                                  "Read every 3 s with the game open in Performance; the network, with one ping per second to the router and to 1.1.1.1 " +
                                  "(you can turn it off in Settings → Privacy). ") +
                              (report.Frames != null
                                  ? L.T("Os quadros vêm do rastreamento do Windows, sem entrar no jogo.", "Frames come from Windows tracing, without touching the game.") +
                                    (report.Frames.FromDriver
                                        ? L.T(" Jogo em Vulkan ou OpenGL: quadros lidos no driver de vídeo, com a hora de cada um um pouco menos precisa.",
                                              " Vulkan or OpenGL game: frames read at the graphics driver, with each one's time slightly less precise.")
                                        : "")
                                  : L.T("O FPS só é medido com a permissão do Caça-travadinhas (Ajustes → Geral).", "FPS is only measured with the Stutter finder permission (Settings → General).")),
                              20, y, 440, 32, Theme.Faint, 8f);
            footer.Height = TextRenderer.MeasureText(footer.Text, footer.Font, new Size(440, int.MaxValue), TextFormatFlags.WordBreak).Height + 2;
            Controls.Add(footer);
            y += footer.Height + 6;

            var close = new NeonButton { Text = L.T("Fechar", "Close"), Location = new Point(20, y), Size = new Size(440, 42), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            close.Click += (s, e) => Close();
            Controls.Add(close);
            ClientSize = new Size(480, y + 42 + 18);

            FinishLayout();
        }

        /// <summary>Caça-travadinhas: FPS, 1% low e as piores travadinhas com o que havia na hora. Devolve o y seguinte.</summary>
        private int AddFrames(MatchReport report, int y)
        {
            FrameReport frames = report.Frames;
            if (frames == null && report.FramesMissing == null) return y;
            Controls.Add(new SectionLabel { Text = L.T("Travadinhas", "Stutters"), Location = new Point(20, y), Size = new Size(440, 20) });
            y += 24;
            if (frames == null)
            {
                // Altura medida: o motivo pode ser longo (a mensagem de erro do Windows) e não pode sair cortado.
                Label missing = Note(L.T("Quadros não medidos: ", "Frames not measured: ") + report.FramesMissing + ".", 20, y, 440, 34, Theme.Faint, 8.5f);
                missing.Height = TextRenderer.MeasureText(missing.Text, missing.Font, new Size(440, int.MaxValue), TextFormatFlags.WordBreak).Height + 2;
                Controls.Add(missing);
                return y + missing.Height + 6;
            }
            Controls.Add(Note(frames.Summary(), 20, y, 440, 20, Theme.Text, 9.5f));
            y += 24;
            foreach (Stutter stutter in frames.Worst)
            {
                Label line = Note("• " + FrameReport.Describe(stutter), 20, y, 440, 32, Theme.Muted, 8.5f);
                line.Height = TextRenderer.MeasureText(line.Text, line.Font, new Size(440, int.MaxValue), TextFormatFlags.WordBreak).Height + 2;
                Controls.Add(line);
                y += line.Height + 4;
            }
            if (frames.StutterCount > frames.Worst.Count)
            {
                Controls.Add(Note(L.T("e mais " + (frames.StutterCount - frames.Worst.Count) + " travadinha(s) menores",
                                      "and " + (frames.StutterCount - frames.Worst.Count) + " smaller stutter(s)"), 20, y, 440, 18, Theme.Faint, 8f));
                y += 22;
            }
            return y + 6;
        }

        /// <summary>O processador: velocidade média, se foi freado e os trechos mais longos de freio. Devolve o y seguinte.</summary>
        private int AddCpuSpeed(CpuSpeedReport cpu, int y)
        {
            Controls.Add(new SectionLabel { Text = L.T("Processador", "CPU"), Location = new Point(20, y), Size = new Size(440, 20) });
            y += 24;
            Label summary = Note(cpu.Summary(), 20, y, 440, 20, Theme.Text, 9.5f);
            summary.Height = TextRenderer.MeasureText(summary.Text, summary.Font, new Size(440, int.MaxValue), TextFormatFlags.WordBreak).Height + 2;
            Controls.Add(summary);
            y += summary.Height + 2;
            Label verdict = Note(cpu.VerdictText(), 20, y, 440, 36, cpu.Limited ? Theme.PerfB : Theme.DailyB, 9f);
            verdict.Height = TextRenderer.MeasureText(verdict.Text, verdict.Font, new Size(440, int.MaxValue), TextFormatFlags.WordBreak).Height + 2;
            Controls.Add(verdict);
            y += verdict.Height + 8;
            foreach (CpuSlowdown s in cpu.Worst)
            {
                Controls.Add(Note("• " + CpuSpeedReport.Describe(s), 20, y, 440, 18, Theme.Muted, 8.5f));
                y += 22;
            }
            return y + 6;
        }

        /// <summary>
        /// Antes e depois: os números da partida ao lado dos da anterior do mesmo jogo e os ajustes que mudaram entre as
        /// duas, para ver se uma mudança ajudou. Devolve o y seguinte.
        /// </summary>
        private int AddComparison(MatchRecord record, MatchRecord previous, int y)
        {
            if (previous == null)
            {
                // Só diz "primeira no histórico" se esta partida está mesmo gravada (não apagada, gravação que deu certo).
                if (!cfg.KeepMatchHistory || record.FilePath == null) return y;
                Label first = Note(L.T("Primeira partida deste jogo no histórico: a próxima vai ser comparada com esta.",
                                       "First match of this game in the history: the next one will be compared with this one."),
                                   20, y, 440, 18, Theme.Faint, 8.5f);
                first.Height = Measure(first);
                Controls.Add(first);
                return y + first.Height + 10;
            }
            Controls.Add(new SectionLabel
            {
                Text = L.T("Antes e depois", "Before and after"),
                RightText = L.T("anterior: ", "previous: ") + previous.When() + " · " + MatchReport.FormatDuration(TimeSpan.FromSeconds(previous.DurationSeconds)),
                RightColor = Theme.Faint,
                Location = new Point(20, y),
                Size = new Size(440, 20)
            });
            y += 24;
            foreach (ComparisonLine line in MatchComparison.Results(record, previous))
            {
                Color color = line.Better > 0 ? Theme.DailyB : line.Better < 0 ? Theme.PerfB : Theme.Muted;
                Controls.Add(Note(line.Text, 20, y, 440, 18, color, 9f));
                y += 20;
            }
            List<string> changes = MatchComparison.SettingChanges(record.Settings, previous.Settings);
            string settings = record.Settings == null || previous.Settings == null
                ? L.T("Os ajustes de uma das partidas não foram guardados.", "The settings of one of the matches were not saved.")
                : changes.Count == 0
                    ? L.T("Nenhum ajuste do app mudou desde a anterior.", "No app setting changed since the previous one.")
                    : L.T("Mudou desde a anterior: ", "Changed since the previous one: ") + string.Join("; ", changes.ToArray()) + ".";
            Label changed = Note(settings, 20, y + 4, 440, 18, Theme.Text, 8.5f);
            changed.Height = Measure(changed);
            Controls.Add(changed);
            y += changed.Height + 8;
            Label caveat = Note(L.T("Mapa, servidor e cena também mudam esses números: compare algumas partidas antes de concluir.",
                                    "Map, server and scene also change these numbers: compare a few matches before concluding."),
                                20, y, 440, 18, Theme.Faint, 8f);
            caveat.Height = Measure(caveat);
            Controls.Add(caveat);
            return y + caveat.Height + 10;
        }

        private static int Measure(Label label)
        {
            return TextRenderer.MeasureText(label.Text, label.Font, new Size(label.Width, int.MaxValue), TextFormatFlags.WordBreak).Height + 2;
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
