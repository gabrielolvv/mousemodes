using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>Raio-X da partida: quem pesou em segundo plano e o que fazer com cada um, num clique.</summary>
    public class MatchReportForm : GamerWindow
    {
        // Esqueleto (px a 96 dpi): margem da janela, cartões de largura inteira com 16 de respiro dentro e 12 entre eles.
        private const int Edge = 20, CardWidth = 440, Pad = 16, Inner = CardWidth - Pad * 2, CardGap = 12;
        private const int RowHeight = 48, ActionWidth = 132;

        /// <summary>A variação de uma linha do "Antes e depois" (" (+21%)"), que vai para a coluna da direita.</summary>
        private static readonly Regex Delta = new Regex(@"\s\(([+-]?\d+%)\)");

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

            // Título na fonte de títulos; nomes longos (VALORANT-Win64-Shipping) quebram em vez de cortar a duração.
            string titleText = report.Game + "  ·  " + MatchReport.FormatDuration(report.Duration);
            Font titleFont = Theme.Title(16f);
            var title = new Paragraph
            {
                Text = titleText,
                TextFont = titleFont,
                Location = new Point(Edge, 54),
                Size = new Size(CardWidth, Paragraph.HeightFor(titleText, titleFont, CardWidth))
            };
            Controls.Add(title);
            int y = title.Bottom;
            if (record != null)
            {
                Label when = MeasuredNote(record.When(), Edge, y, CardWidth, Theme.TextSecondary, 9f);
                Controls.Add(when);
                y = when.Bottom;
            }
            y += 12;

            // Os números-chave numa faixa logo no alto.
            FrameReport frames = report.Frames;
            if (frames != null)
            {
                var keys = new ChipRow
                {
                    Location = new Point(Edge, y),
                    Size = new Size(CardWidth, 60),
                    ValueSize = 16f,
                    Labels = new[] { L.T("FPS médio", "Average FPS"), "1% low", L.T("Travadinhas", "Stutters") },
                    Values = new[] { Whole(frames.AverageFps), Whole(frames.Low1Fps), frames.StutterCount.ToString(CultureInfo.InvariantCulture) }
                };
                Controls.Add(keys);
                y = keys.Bottom + CardGap;
            }

            y = AddPrograms(report, y);
            y = AddTimeline(report, y);
            y = AddFrames(report, y);
            if (report.CpuSpeed != null) y = AddCpuSpeed(report.CpuSpeed, y);
            if (report.Network != null) y = AddNetwork(report.Network, y);
            if (record != null) y = AddComparison(record, previous, y);

            // Notas de rodapé: legíveis (TextSecondary) e em linhas curtas.
            y += 4;
            Label footer = MeasuredNote(L.T("Lido a cada 3 s com o jogo aberto no Performance; a rede, com um ping por segundo ao roteador e ao 1.1.1.1 " +
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
                                        Edge, y, 400, Theme.TextSecondary, 8.5f);
            Controls.Add(footer);
            y = footer.Bottom + 16;

            var close = new NeonButton { Text = L.T("Fechar", "Close"), Location = new Point(Edge + CardWidth - 120, y), Size = new Size(120, 36), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            close.Click += (s, e) => Close();
            Controls.Add(close);
            ClientSize = new Size(480, close.Bottom + 20);

            FinishLayout();
        }

        private static string Whole(double value)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- cartões

        /// <summary>Começa um cartão com o cabeçalho (e um texto à direita, opcional). cy = onde o conteúdo começa.</summary>
        private static Card StartCard(string header, string right, out int cy)
        {
            var card = new Card { Size = new Size(CardWidth, 60) };
            var label = new SectionLabel { Text = header, Location = new Point(Pad, 14), Size = new Size(Inner, 20) };
            if (right != null)
            {
                label.RightText = right;
                label.RightColor = Theme.TextSecondary;
            }
            card.Controls.Add(label);
            cy = label.Bottom + 8;
            return card;
        }

        /// <summary>Fecha o cartão (cy = depois do último item, já com o espaço dele) e devolve o y do próximo.</summary>
        private int EndCard(Card card, int y, int cy)
        {
            card.Location = new Point(Edge, y);
            card.Height = cy + 8;
            Controls.Add(card);
            return card.Bottom + CardGap;
        }

        /// <summary>Texto dentro de um cartão, com a altura medida (fundo Surface: transparente pintaria o da janela).</summary>
        private static Label CardText(Control card, string text, int x, int y, int w, Color color, float size, bool bold = false)
        {
            var label = new Label
            {
                Text = text,
                Location = new Point(x, y),
                ForeColor = color,
                BackColor = Theme.Surface,
                Font = bold ? Theme.Ui(size, FontStyle.Bold) : Theme.Ui(size),
                UseMnemonic = false
            };
            label.Size = new Size(w, Theme.TextHeight(text, label.Font, w));
            card.Controls.Add(label);
            return label;
        }

        /// <summary>Veredito: visto verde (bom) ou exclamação âmbar, com o texto em Text. Devolve o cy seguinte.</summary>
        private static int AddVerdict(Control card, string text, bool good, int cy)
        {
            card.Controls.Add(new VerdictIcon { Good = good, BackColor = Theme.Surface, Location = new Point(Pad, cy + 1), Size = new Size(16, 16) });
            Label label = CardText(card, text, Pad + 24, cy, Inner - 24, Theme.Text, 9f);
            return cy + Math.Max(18, label.Height) + 6;
        }

        /// <summary>Lista de itens ("• aos 10 min · ..."), cada um com a altura que precisa. Devolve o cy seguinte.</summary>
        private static int AddBullet(Control card, string text, int cy)
        {
            // Recuo pendurado: a segunda linha começa sob o texto, não sob o marcador.
            CardText(card, "•", Pad, cy, 12, Theme.TextTertiary, 9f);
            Label line = CardText(card, text, Pad + 14, cy, Inner - 14, Theme.TextSecondary, 9f);
            return cy + line.Height + 4;
        }

        /// <summary>"e mais 2 travadinha(s) menores": meta curta, em TextTertiary.</summary>
        private static int AddMore(Control card, string text, int cy)
        {
            Label more = CardText(card, text, Pad, cy, Inner, Theme.TextTertiary, 8.5f);
            return cy + more.Height + 4;
        }

        /// <summary>Os programas que pesaram, com "Fechar ao jogar" em cada um e o segundo plano calmo. Devolve o y seguinte.</summary>
        private int AddPrograms(MatchReport report, int y)
        {
            int cy;
            Card card = StartCard(L.T("Programas em segundo plano", "Background programs"), null, out cy);
            int count = report.Culprits.Count;
            if (count == 0)
            {
                cy = AddVerdict(card, L.T("Nada pesou em segundo plano durante a partida.", "Nothing weighed on the PC in the background during the match."), true, cy);
                return EndCard(card, y, cy);
            }

            Label lead = CardText(card, count == 1
                    ? L.T("1 programa pesou em segundo plano durante a partida:", "1 program weighed on the PC in the background during the match:")
                    : L.T(count + " programas pesaram em segundo plano durante a partida:", count + " programs weighed on the PC in the background during the match:"),
                Pad, cy, Inner, Theme.TextSecondary, 9f);
            cy = lead.Bottom + 8;

            foreach (Culprit c in report.Culprits)
            {
                AddRow(card, c, cy);
                cy += RowHeight;
            }
            cy += 4;

            if (cfg.CalmBackground)
            {
                cy = AddVerdict(card, L.T("O segundo plano calmo já está ligado: no Performance, estes programas ficam mais leves sem fechar.",
                                          "Calm background is already on: in Performance, these programs get lighter without closing."), true, cy);
                return EndCard(card, y, cy);
            }

            // A ação recomendada do relatório (o único primário da tela), depois dos dados que a justificam.
            string on = L.T("Ligar o segundo plano calmo", "Turn on calm background"), done = L.T("Segundo plano calmo ligado", "Calm background on");
            var calm = new NeonButton
            {
                Text = on,
                Style = ButtonStyle.Primary,
                Location = new Point(Pad, cy),
                Size = new Size(Math.Min(Inner, ButtonWidth(180, on, done)), 36),
                AccentA = Theme.PerfA,
                AccentB = Theme.PerfB
            };
            calm.Click += (s, e) =>
            {
                cfg.CalmBackground = true;
                Log.Info(L.T("Raio-X: segundo plano calmo ligado", "X-ray: calm background turned on"));
                if (this.changed != null) this.changed();
                calm.Enabled = false;
                calm.Text = done;
                calm.Invalidate();
            };
            card.Controls.Add(calm);
            Label calmNote = CardText(card, L.T("Deixa os programas em segundo plano mais leves durante o jogo, sem fechar nenhum (Ajustes → Turbo).",
                                                "Makes background programs lighter during the game without closing any (Settings → Turbo)."),
                                      Pad, calm.Bottom + 8, Inner, Theme.TextSecondary, 8.5f);
            return EndCard(card, y, calmNote.Bottom + 2);
        }

        /// <summary>
        /// A partida no tempo: FPS, 1% low, travadinhas, freio e ping num gráfico, com a legenda só do que existe. Partida
        /// antiga (sem a série) ou sem nada medido não ganha o cartão. Devolve o y seguinte.
        /// </summary>
        private int AddTimeline(MatchReport report, int y)
        {
            MatchTimeline timeline = report.Timeline;
            if (timeline == null || !timeline.HasData) return y;
            int cy;
            Card card = StartCard(L.T("A partida no tempo", "The match over time"),
                                  L.T("um ponto a cada ", "one point every ") + TimelineChart.Seconds(timeline.BucketSeconds), out cy);
            var chart = new TimelineChart { Location = new Point(Pad, cy), Size = new Size(Inner, TimelineChart.PreferredHeight) };
            chart.SetData(timeline, report.Duration.TotalSeconds);
            card.Controls.Add(chart);
            cy = chart.Bottom + 6;

            List<LegendItem> items = LegendFor(timeline);
            var legend = new ChartLegend { Location = new Point(Pad, cy), Size = new Size(Inner, ChartLegend.HeightFor(items, Inner)) };
            legend.SetItems(items);
            card.Controls.Add(legend);
            return EndCard(card, y, legend.Bottom + 2);
        }

        /// <summary>Os itens da legenda, só das séries que a partida tem (as cores são as do TimelineChart).</summary>
        internal static List<LegendItem> LegendFor(MatchTimeline timeline)
        {
            var items = new List<LegendItem>();
            if (timeline.Points.Exists(p => p.HasFps))
            {
                items.Add(new LegendItem(L.T("FPS e 1% low", "FPS and 1% low"), Theme.PaletteFor(Theme.PerfA).Fill));
                if (timeline.Stutters != null && timeline.Stutters.Count > 0) items.Add(new LegendItem(L.T("Travadinha", "Stutter"), Theme.Warning));
            }
            if (timeline.Slowdowns != null && timeline.Slowdowns.Count > 0)
                items.Add(new LegendItem(L.T("Processador freado", "CPU throttled"), Theme.Alpha(Theme.Warning, TimelineChart.SlowdownAlpha), true)
                          { Outline = Theme.Alpha(Theme.Warning, 90) });
            bool ping = timeline.Points.Exists(p => p.HasPing);
            if (ping) items.Add(new LegendItem("Ping", Theme.DailyPalette.OnSurface));
            // Sem nenhum ping medido o gráfico não tem faixa de ping (nem as marcas de perda): a legenda também não.
            if (ping && timeline.Points.Exists(p => p.Lost > 0)) items.Add(new LegendItem(L.T("Perda", "Lost ping"), Theme.Danger)); // ao lado do Ping: "Ping perdido" não cabe na linha
            return items;
        }

        /// <summary>Caça-travadinhas: as piores travadinhas com o que havia na hora (os números ficam na faixa do alto).</summary>
        private int AddFrames(MatchReport report, int y)
        {
            FrameReport frames = report.Frames;
            if (frames == null && report.FramesMissing == null) return y;
            int cy;
            Card card = StartCard(L.T("Travadinhas", "Stutters"), null, out cy);
            if (frames == null)
            {
                // Altura medida: o motivo pode ser longo (a mensagem de erro do Windows) e não pode sair cortado.
                Label missing = CardText(card, L.T("Quadros não medidos: ", "Frames not measured: ") + report.FramesMissing + ".", Pad, cy, Inner, Theme.TextSecondary, 9f);
                return EndCard(card, y, missing.Bottom + 2);
            }
            if (frames.Worst.Count == 0)
                cy = AddVerdict(card, frames.Summary(), frames.StutterCount == 0, cy);
            foreach (Stutter stutter in frames.Worst)
                cy = AddBullet(card, FrameReport.Describe(stutter), cy);
            if (frames.StutterCount > frames.Worst.Count)
                cy = AddMore(card, L.T("e mais " + (frames.StutterCount - frames.Worst.Count) + " travadinha(s) menores",
                                       "and " + (frames.StutterCount - frames.Worst.Count) + " smaller stutter(s)"), cy);
            return EndCard(card, y, cy);
        }

        /// <summary>O processador: velocidade média, se foi freado e os trechos mais longos de freio. Devolve o y seguinte.</summary>
        private int AddCpuSpeed(CpuSpeedReport cpu, int y)
        {
            int cy;
            Card card = StartCard(L.T("Processador", "CPU"), null, out cy);
            Label summary = CardText(card, cpu.Summary(), Pad, cy, Inner, Theme.Text, 9.5f);
            cy = summary.Bottom + 6;
            cy = AddVerdict(card, cpu.VerdictText(), !cpu.Limited, cy);
            foreach (CpuSlowdown s in cpu.Worst)
                cy = AddBullet(card, CpuSpeedReport.Describe(s), cy);
            return EndCard(card, y, cy);
        }

        /// <summary>A rede: resumo, de onde vieram os picos e os piores deles. Devolve o y seguinte.</summary>
        private int AddNetwork(NetworkReport net, int y)
        {
            int cy;
            Card card = StartCard(L.T("Rede", "Network"), null, out cy);
            Label summary = CardText(card, net.Summary(), Pad, cy, Inner, Theme.Text, 9.5f);
            cy = summary.Bottom + 6;
            cy = AddVerdict(card, net.VerdictText(), net.Verdict == NetVerdict.Stable, cy);
            // Uma ou duas linhas, conforme o texto: cada item ocupa só a altura que precisa.
            foreach (NetSpike spike in net.Spikes)
                cy = AddBullet(card, NetworkReport.Describe(spike), cy);
            if (net.SpikeCount > net.Spikes.Count)
                cy = AddMore(card, L.T("e mais " + (net.SpikeCount - net.Spikes.Count) + " pico(s) menores",
                                       "and " + (net.SpikeCount - net.Spikes.Count) + " smaller spike(s)"), cy);
            return EndCard(card, y, cy);
        }

        /// <summary>
        /// Antes e depois: os números da partida ao lado dos da anterior do mesmo jogo e os ajustes que mudaram entre as
        /// duas, para ver se uma mudança ajudou. A linha fica em Text; só a variação, à direita, ganha cor. Devolve o y seguinte.
        /// </summary>
        private int AddComparison(MatchRecord record, MatchRecord previous, int y)
        {
            if (previous == null)
            {
                // Só diz "primeira no histórico" se esta partida está mesmo gravada (não apagada, gravação que deu certo).
                if (!cfg.KeepMatchHistory || record.FilePath == null) return y;
                Label first = MeasuredNote(L.T("Primeira partida deste jogo no histórico: a próxima vai ser comparada com esta.",
                                               "First match of this game in the history: the next one will be compared with this one."),
                                           Edge, y, CardWidth, Theme.TextSecondary, 9f);
                Controls.Add(first);
                return first.Bottom + CardGap;
            }
            int cy;
            Card card = StartCard(L.T("Antes e depois", "Before and after"),
                                  L.T("anterior: ", "previous: ") + previous.When() + " · " + MatchReport.FormatDuration(TimeSpan.FromSeconds(previous.DurationSeconds)),
                                  out cy);
            foreach (ComparisonLine line in MatchComparison.Results(record, previous))
            {
                Match m = Delta.Match(line.Text);
                var row = new TextRuns
                {
                    BackColor = Theme.Surface,
                    Location = new Point(Pad, cy),
                    Size = new Size(Inner, 20),
                    FontSize = 9f,
                    // Sem porcentagem (a anterior era zero, como perda 0% → 3%): a palavra leva a cor de melhor ou pior.
                    RightText = m.Success ? m.Groups[1].Value
                              : line.Better > 0 ? L.T("melhor", "better")
                              : line.Better < 0 ? L.T("pior", "worse") : "",
                    RightColor = line.Better > 0 ? Theme.Success : line.Better < 0 ? Theme.Danger : Theme.TextSecondary
                };
                row.Add(m.Success ? line.Text.Remove(m.Index, m.Length) : line.Text, Theme.Text);
                card.Controls.Add(row);
                cy += 22;
            }
            List<string> changes = MatchComparison.SettingChanges(record.Settings, previous.Settings);
            string settings = record.Settings == null || previous.Settings == null
                ? L.T("Os ajustes de uma das partidas não foram guardados.", "The settings of one of the matches were not saved.")
                : changes.Count == 0
                    ? L.T("Nenhum ajuste do app mudou desde a anterior.", "No app setting changed since the previous one.")
                    : L.T("Mudou desde a anterior: ", "Changed since the previous one: ") + string.Join("; ", changes.ToArray()) + ".";
            Label changedNote = CardText(card, settings, Pad, cy + 6, Inner, Theme.Text, 9f);
            cy = changedNote.Bottom + 6;
            Label caveat = CardText(card, L.T("Mapa, servidor e cena também mudam esses números: compare algumas partidas antes de concluir.",
                                              "Map, server and scene also change these numbers: compare a few matches before concluding."),
                                    Pad, cy, Inner, Theme.TextSecondary, 8.5f);
            return EndCard(card, y, caveat.Bottom + 2);
        }

        /// <summary>Um programa: nome, uso (o pico em âmbar só quando alto) e a ação à direita.</summary>
        private void AddRow(Card card, Culprit c, int y)
        {
            CardText(card, c.Name, Pad, y, Inner - ActionWidth - 12, Theme.Text, 10f, true).Height = 20;
            var detail = new TextRuns { BackColor = Theme.Surface, Location = new Point(Pad, y + 21), Size = new Size(Inner - ActionWidth - 12, 18), FontSize = 8.5f };
            string peak = L.T("pico ", "peak ");
            string[] parts = MatchReport.Describe(c).Split(new[] { " · " }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) detail.Add(" · ", Theme.TextTertiary);
                detail.Add(parts[i], parts[i].StartsWith(peak, StringComparison.Ordinal) && c.PeakPercent > 25 ? Theme.Warning : Theme.TextSecondary);
            }
            card.Controls.Add(detail);

            int actionX = Pad + Inner - ActionWidth;
            if (GameBooster.Matches(c.Name, Launchers))
            {
                AddRowNote(card, L.T("launcher: melhor manter", "launcher: keep it open"), Theme.TextSecondary, y);
                return;
            }
            if (GameBooster.Matches(c.Name, BackgroundCalmer.NeverCalm))
            {
                // Voz, música, overlay, software do mouse: fechar atrapalharia mais do que ajuda.
                AddRowNote(card, L.T("melhor manter aberto", "better kept open"), Theme.TextSecondary, y);
                return;
            }
            if (InCloseList(c.Name))
            {
                AddRowNote(card, L.T("já fecha ao jogar", "already auto-closed"), Theme.Success, y);
                return;
            }

            var closeIt = new NeonButton { Text = L.T("Fechar ao jogar", "Auto-close"), Location = new Point(actionX, y + 4), Size = new Size(ActionWidth, 32), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
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
            card.Controls.Add(closeIt);
        }

        /// <summary>O motivo de não haver ação (launcher, voz...), alinhado à direita da linha.</summary>
        private static void AddRowNote(Card card, string text, Color color, int y)
        {
            Label note = CardText(card, text, Pad + Inner - 170, y + 10, 170, color, 8.5f);
            note.TextAlign = ContentAlignment.TopRight;
        }

        private bool InCloseList(string name)
        {
            return cfg.AppsToClose.Exists(a => string.Equals(GameDetector.Normalize(a), name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
