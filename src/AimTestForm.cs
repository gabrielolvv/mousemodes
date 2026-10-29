using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>
    /// O que o teste de mira precisa do resto do app. O teste sugere o DPI e a pessoa troca no programa do
    /// próprio mouse (o controle direto de DPI do Darmoshark fica no build pessoal da branch dpi-darmoshark).
    /// </summary>
    public class AimTestHost
    {
        /// <summary>DPI informado da última vez (0 = não sabe), para preencher o campo "Seu DPI".</summary>
        public Func<int> CurrentDpi;
        /// <summary>Guarda o DPI digitado para a próxima vez.</summary>
        public Action<int> SaveDpi;
        public Func<MouseProfile> CurrentProfile;
    }

    /// <summary>Resultado de um alvo.</summary>
    internal class TargetResult
    {
        public double Distance;      // px até o centro, de onde o cursor estava
        public double TimeMs;        // do surgimento ao acerto
        public double EndRatio;      // onde o 1º movimento parou: 1 = no alvo, >1 passou, <1 parou antes
        public bool Overshot;        // passou do alvo em algum momento
        public int Corrections;      // movimentos de correção depois do 1º
        public double ClickOffset;   // distância do clique ao centro / raio (0 = no centro)
        public int Misses;           // cliques fora antes de acertar
    }

    /// <summary>Resumo de uma rodada.</summary>
    internal class RoundResult
    {
        public string Label;         // "1700 DPI"
        public int Dpi;
        public double Seconds;
        public List<TargetResult> Targets = new List<TargetResult>();

        public int Hits { get { return Targets.Count; } }
        public int Misses { get { int m = 0; foreach (var t in Targets) m += t.Misses; return m + PendingMisses; } }
        public int PendingMisses;

        public double Median(Func<TargetResult, double> f)
        {
            if (Targets.Count == 0) return 0;
            var v = Targets.ConvertAll(t => f(t));
            v.Sort();
            return v[v.Count / 2];
        }

        public double Average(Func<TargetResult, double> f)
        {
            if (Targets.Count == 0) return 0;
            double s = 0;
            foreach (var t in Targets) s += f(t);
            return s / Targets.Count;
        }

        public double OvershootRate { get { return Targets.Count == 0 ? 0 : Targets.FindAll(t => t.Overshot).Count / (double)Targets.Count; } }
        public double MissRate { get { int total = Hits + Misses; return total == 0 ? 0 : Misses / (double)total; } }

        /// <summary>Pontos por minuto: acertos rápidos e no centro valem mais; erros descontam.</summary>
        public double Score
        {
            get
            {
                if (Seconds <= 0) return 0;
                double pts = 0;
                foreach (var t in Targets) pts += 10 * (1.15 - 0.3 * Math.Min(1, t.ClickOffset));
                pts -= 4 * Misses;
                return Math.Max(0, pts) * 60 / Seconds;
            }
        }
    }

    public class AimTestForm : GamerWindow
    {
        private enum Phase { Intro, Countdown, Playing, Results }

        private const double RoundSeconds = 20, CountdownSeconds = 3;

        private readonly AimTestHost host;
        private readonly AimArena arena;
        private readonly NeonButton startButton;
        private readonly NeonButton againButton;
        private readonly Label footer;
        private readonly Timer frame;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random rnd = new Random();

        private Phase phase = Phase.Intro;
        /// <summary>Rodadas enquanto o app estiver aberto: dá para trocar o DPI no mouse e comparar.</summary>
        private static readonly List<RoundResult> History = new List<RoundResult>();
        private const int HistoryMax = 6;
        private readonly TextBox dpiBox;
        private readonly FieldHost dpiHost;
        private RoundResult round;
        private double phaseStart;

        // alvo atual
        private PointF target;
        private float radius;
        private double spawnTime;
        private PointF spawnCursor;
        private readonly List<double[]> path = new List<double[]>(); // t, x, y
        private int targetMisses;

        public AimTestForm(AimTestHost host) : base(L.T("Teste de mira", "Aim test"), true)
        {
            this.host = host;
            CloseOnEscape = true;
            ClientSize = new Size(1040, 730);
            TitleBar.AccentA = Theme.PerfA;
            TitleBar.AccentB = Theme.PerfB;

            arena = new AimArena { Location = new Point(20, 56), Size = new Size(1000, 580) };
            arena.MouseMove += (s, e) => OnArenaMove(e.Location);
            arena.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) OnArenaClick(e.Location); };
            Controls.Add(arena);

            dpiBox = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Ui(12f), TextAlign = HorizontalAlignment.Center, MaxLength = 5 };
            dpiBox.KeyPress += (s, e) =>
            {
                if (e.KeyChar == '\r') { e.Handled = true; StartRound(); }
                else if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
            };
            dpiHost = new FieldHost(dpiBox) { Size = new Size(180, 40), Accent = Theme.PerfB };
            arena.Controls.Add(dpiHost);

            startButton = new NeonButton { Text = L.T("Começar (20 s)", "Start (20 s)"), Style = ButtonStyle.Primary, Location = new Point(20, 656), Size = new Size(240, 48), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            againButton = new NeonButton { Text = L.T("Testar de novo", "Test again"), Location = new Point(20, 656), Size = new Size(240, 48), AccentA = Theme.PerfA, AccentB = Theme.PerfB, Visible = false };
            startButton.Click += (s, e) => StartRound();
            againButton.Click += (s, e) => ShowIntro();
            Controls.Add(startButton);
            Controls.Add(againButton);

            footer = Note("", 280, 660, 740, 44, Theme.Faint, 8.5f);
            Controls.Add(footer);

            frame = new Timer { Interval = 15 };
            frame.Tick += (s, e) => Tick();
            frame.Start();

            FinishLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ShowIntro();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            frame.Stop();
            base.OnFormClosed(e);
        }

        // ------------------------------------------------------------------ fluxo

        private void ShowIntro()
        {
            phase = Phase.Intro;
            startButton.Visible = true;
            againButton.Visible = false;
            footer.Text = L.T("Não sabe o DPI? Veja no programa do seu mouse. Para comparar, troque o DPI lá e faça o teste de novo: " +
                              "os resultados ficam lado a lado.",
                              "Do not know your DPI? Check your mouse software. To compare, change the DPI there and run the test again: " +
                              "the results stay side by side.");

            if (dpiBox.Text.Length == 0 && host.CurrentDpi != null)
            {
                int current = host.CurrentDpi();
                if (current > 0) dpiBox.Text = current.ToString();
            }
            dpiHost.Location = new Point((arena.Width - dpiHost.Width) / 2, (int)(arena.Height * 0.79f));
            dpiHost.Visible = true;
            arena.Cursor = Cursors.Default;
            arena.Mode = AimArena.View.Intro;
            arena.Invalidate();
        }

        private void StartRound()
        {
            if (phase != Phase.Intro) return;
            int dpi;
            if (int.TryParse(dpiBox.Text, out dpi) && dpi > 0)
            {
                dpi = RoundDpi(dpi);
                dpiBox.Text = dpi.ToString();
                if (host.SaveDpi != null) host.SaveDpi(dpi);
            }
            else dpi = 0;

            round = new RoundResult { Label = dpi > 0 ? dpi + " DPI" : "DPI ?", Dpi = dpi };
            phase = Phase.Countdown;
            phaseStart = clock.Elapsed.TotalSeconds;
            startButton.Visible = againButton.Visible = false;
            dpiHost.Visible = false;
            footer.Text = L.T("Acerte o máximo de alvos, rápido e no centro · Esc cancela", "Hit as many targets as you can, fast and centered · Esc cancels");
            arena.Cursor = Cursors.Cross;
            arena.Mode = AimArena.View.Countdown;
            arena.RoundLabel = round.Label;
            arena.Invalidate();
        }

        /// <summary>Arredonda para 50 em 50, como a maioria dos programas de mouse.</summary>
        internal static int RoundDpi(double dpi)
        {
            return Math.Max(100, Math.Min(26000, (int)Math.Round(dpi / 50) * 50));
        }

        private void Tick()
        {
            double now = clock.Elapsed.TotalSeconds;
            if (phase == Phase.Countdown)
            {
                double left = CountdownSeconds - (now - phaseStart);
                arena.CountdownValue = (int)Math.Ceiling(left);
                if (left <= 0)
                {
                    phase = Phase.Playing;
                    phaseStart = now;
                    arena.Mode = AimArena.View.Playing;
                    SpawnTarget(arena.PointToClient(Cursor.Position));
                }
                arena.Invalidate();
            }
            else if (phase == Phase.Playing)
            {
                double elapsed = now - phaseStart;
                arena.TimeLeft = Math.Max(0, 1 - elapsed / RoundSeconds);
                arena.Hits = round.Hits;
                arena.Misses = round.Misses;
                if (elapsed >= RoundSeconds)
                {
                    round.Seconds = RoundSeconds;
                    round.PendingMisses += targetMisses;
                    targetMisses = 0;
                    History.Add(round);
                    if (History.Count > HistoryMax) History.RemoveAt(0);
                    Log.Info(string.Format(L.T("Teste de mira: {0} · {1} acertos, {2} erros, tempo {3:0} ms, parada {4:0.00}, passou {5:0%}", "Aim test: {0} · {1} hits, {2} misses, time {3:0} ms, stop {4:0.00}, overshot {5:0%}"),
                        round.Label, round.Hits, round.Misses, round.Median(t => t.TimeMs), round.Median(t => t.EndRatio), round.OvershootRate));
                    ShowResults();
                    return;
                }
                arena.Invalidate();
            }
            else
            {
                arena.Step();
            }
        }

        private void SpawnTarget(Point cursor)
        {
            float scale = arena.Width / 1000f;
            radius = 22 * scale;
            float margin = radius + 30 * scale;
            var from = new PointF(cursor.X, cursor.Y);
            PointF p = from;
            for (int tries = 0; tries < 50; tries++)
            {
                p = new PointF(margin + (float)rnd.NextDouble() * (arena.Width - 2 * margin),
                               margin + 50 * scale + (float)rnd.NextDouble() * (arena.Height - 2 * margin - 50 * scale));
                double d = Dist(from, p);
                if (d >= 180 * scale && d <= 560 * scale) break;
            }
            target = p;
            spawnCursor = from;
            spawnTime = clock.Elapsed.TotalMilliseconds;
            path.Clear();
            path.Add(new double[] { spawnTime, from.X, from.Y });
            targetMisses = 0;
            arena.Target = target;
            arena.Radius = radius;
        }

        private void OnArenaMove(Point p)
        {
            if (phase != Phase.Playing) return;
            path.Add(new double[] { clock.Elapsed.TotalMilliseconds, p.X, p.Y });
        }

        private void OnArenaClick(Point p)
        {
            if (phase != Phase.Playing) return;
            double d = Dist(new PointF(p.X, p.Y), target);
            if (d > radius)
            {
                targetMisses++;
                arena.Flash(p, false);
                return;
            }
            path.Add(new double[] { clock.Elapsed.TotalMilliseconds, p.X, p.Y });
            round.Targets.Add(Analyze(path, spawnCursor, target, radius, spawnTime, d / radius, targetMisses));
            arena.Flash(p, true);
            SpawnTarget(p);
        }

        // ------------------------------------------------------------------ medições

        /// <summary>
        /// Analisa o trajeto do cursor até um alvo. path: pontos (t em ms, x, y), do surgimento do alvo ao clique.
        /// </summary>
        internal static TargetResult Analyze(IList<double[]> path, PointF from, PointF target, float radius,
            double spawnTime, double clickOffset, int misses)
        {
            var r = new TargetResult { ClickOffset = clickOffset, Misses = misses };
            double dx = target.X - from.X, dy = target.Y - from.Y;
            double D = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            double ux = dx / D, uy = dy / D;
            r.Distance = D;
            r.TimeMs = path[path.Count - 1][0] - spawnTime;

            // Projeção de cada ponto na reta cursor→alvo e velocidade entre pontos.
            int n = path.Count;
            var proj = new double[n];
            var speed = new double[n];
            for (int i = 0; i < n; i++)
            {
                proj[i] = (path[i][1] - from.X) * ux + (path[i][2] - from.Y) * uy;
                if (i > 0)
                {
                    double dt = Math.Max(1, path[i][0] - path[i - 1][0]);
                    speed[i] = Math.Sqrt(Sq(path[i][1] - path[i - 1][1]) + Sq(path[i][2] - path[i - 1][2])) / dt;
                }
            }
            // Suaviza a velocidade (média de 3) para não confundir ruído com parada.
            var smooth = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = 0; int c = 0;
                for (int k = Math.Max(0, i - 1); k <= Math.Min(n - 1, i + 1); k++) { s += speed[k]; c++; }
                smooth[i] = s / c;
            }

            int peak = 0;
            for (int i = 1; i < n; i++) if (smooth[i] > smooth[peak]) peak = i;
            double peakSpeed = Math.Max(0.01, smooth[peak]);

            // Fim do 1º movimento: velocidade cai abaixo de 20% do pico, ou fica parado (>40 ms sem amostra).
            int end = n - 1;
            for (int i = peak + 1; i < n; i++)
            {
                bool slow = smooth[i] < 0.2 * peakSpeed;
                bool gap = path[i][0] - path[i - 1][0] > 40;
                if (slow || gap) { end = gap ? i - 1 : i; break; }
            }
            r.EndRatio = proj[Math.Max(0, end)] / D;

            double maxProj = 0;
            foreach (double v in proj) maxProj = Math.Max(maxProj, v);
            r.Overshot = maxProj > D + radius;

            // Correções: cada vez que volta a acelerar depois de quase parar.
            bool slowNow = true;
            for (int i = end + 1; i < n; i++)
            {
                if (slowNow && smooth[i] > 0.3 * peakSpeed) { r.Corrections++; slowNow = false; }
                else if (!slowNow && smooth[i] < 0.15 * peakSpeed) slowNow = true;
            }
            return r;
        }

        private static double Sq(double v) { return v * v; }

        private static double Dist(PointF a, PointF b)
        {
            return Math.Sqrt(Sq(a.X - b.X) + Sq(a.Y - b.Y));
        }

        // ------------------------------------------------------------------ resultado e sugestões

        private void ShowResults()
        {
            phase = Phase.Results;
            arena.Cursor = Cursors.Default;
            dpiHost.Visible = false;

            List<string> lines = Advise(History, round, host.CurrentProfile != null ? host.CurrentProfile() : null);
            RoundResult best = BestOf(History);

            arena.Mode = AimArena.View.Results;
            arena.Rounds = History;
            arena.Best = History.Count > 1 ? best : null;
            arena.Current = round;
            arena.Advice = lines;
            arena.Invalidate();

            startButton.Visible = false;
            againButton.Visible = true;
            footer.Text = L.T("Trocou o DPI no programa do mouse? Clique em Testar de novo e informe o novo valor: o resultado entra na tabela. " +
                              "Sugestões são um ponto de partida: repita o teste algumas vezes antes de decidir.",
                              "Changed the DPI in your mouse software? Click Test again and enter the new value: the result goes into the table. " +
                              "Suggestions are a starting point: repeat the test a few times before deciding.");
        }

        internal static RoundResult BestOf(List<RoundResult> rounds)
        {
            RoundResult best = null;
            foreach (var r in rounds)
                if (r.Hits > 0 && (best == null || r.Score > best.Score)) best = r;
            return best;
        }

        /// <summary>O que fazer, a partir da rodada que acabou de jogar (focus) e das anteriores.</summary>
        internal static List<string> Advise(List<RoundResult> rounds, RoundResult focus, MouseProfile current)
        {
            var lines = new List<string>();
            if (focus == null || focus.Hits == 0)
            {
                lines.Add(L.T("Nenhum alvo acertado. Tente de novo com calma: o objetivo é acertar rápido e no centro.", "No targets hit. Try again calmly: the goal is to hit them fast and centered."));
                return lines;
            }

            RoundResult best = BestOf(rounds);
            double end = focus.Median(t => t.EndRatio);
            double corr = focus.Average(t => t.Corrections);
            double offset = focus.Average(t => t.ClickOffset);

            if (best != null && best != focus && rounds.Count > 1)
            {
                lines.Add(L.T("Seu melhor resultado até agora: ", "Your best result so far: ") + best.Label + " (" + best.Score.ToString("0") +
                          L.T(" pontos/min, contra ", " points/min, against ") + focus.Score.ToString("0") + L.T(" agora).", " now)."));
            }

            // Parada do 1º movimento: >1 passa do alvo (rápido demais), <1 para antes (lento demais).
            if (end > 1.10 || focus.OvershootRate > 0.35)
            {
                lines.Add(L.T("Você costuma passar do alvo (", "You tend to overshoot the target (") + focus.OvershootRate.ToString("0%") +
                          L.T(" das vezes) e voltar: o ponteiro está rápido para a sua mão.", " of the time) and come back: the pointer is fast for your hand."));
                lines.Add(Suggestion(focus, 0.8, L.T("mais baixo", "lower")));
            }
            else if (end < 0.80 && corr > 1.2)
            {
                lines.Add(L.T("Seu 1º movimento para antes do alvo e precisa de correções (", "Your first movement stops short of the target and needs corrections (") + corr.ToString("0.0") +
                          L.T(" por alvo): o ponteiro está lento para a sua mão.", " per target): the pointer is slow for your hand."));
                lines.Add(Suggestion(focus, 1.25, L.T("mais alto", "higher")));
            }
            else
            {
                lines.Add(L.T("O 1º movimento chega no alvo (parada em ", "Your first movement reaches the target (stops at ") + (end * 100).ToString("0") +
                          L.T("% da distância): ", "% of the distance): ") +
                          (focus.Dpi > 0 ? focus.Dpi + L.T(" DPI está", " DPI is") : L.T("o DPI está", "the DPI is")) + L.T(" equilibrado para a sua mão.", " balanced for your hand."));
            }

            if (focus.MissRate > 0.25)
                lines.Add(L.T("Muitos cliques fora (", "Many missed clicks (") + focus.MissRate.ToString("0%") +
                          L.T("): você está clicando antes de parar no alvo. Priorize acertar; a velocidade vem com o treino.", "): you are clicking before stopping on the target. Focus on hitting; speed comes with practice."));
            else if (offset > 0.6)
                lines.Add(L.T("Os acertos estão longe do centro: mire no meio do alvo antes de clicar.", "Your hits are far from the center: aim at the middle of the target before clicking."));

            if (current != null && current.EnhancePointerPrecision)
                lines.Add(L.T("A aceleração do Windows está ligada no modo atual: use o modo Performance (ou desligue em Ajustes) para o mesmo gesto sempre levar ao mesmo lugar.", "Windows acceleration is on in the current mode: use Performance mode (or turn it off in Settings) so the same motion always lands in the same place."));
            if (current != null && current.PointerSpeed != 10)
                lines.Add(L.T("Teste feito com velocidade do Windows ", "Test done with Windows speed ") + current.PointerSpeed +
                          L.T("/20; no jogo vale DPI × sensibilidade do jogo.", "/20; in game what counts is DPI × in-game sensitivity."));
            else
                lines.Add(L.T("No jogo, a sensibilidade final é DPI × sensibilidade do jogo; ajuste a do jogo depois de fixar o DPI.", "In game, the final sensitivity is DPI × in-game sensitivity; tune the game one after settling the DPI."));
            return lines;
        }

        /// <summary>Sugere um DPI para testar no programa do mouse.</summary>
        private static string Suggestion(RoundResult r, double factor, string direction)
        {
            if (r.Dpi <= 0)
                return L.T("Sugestão: " + (factor < 1 ? "baixe" : "suba") + " o DPI uns 20% no programa do seu mouse e faça o teste de novo. " +
                           "Informe o DPI no começo do teste para receber um valor exato.",
                           "Suggestion: " + (factor < 1 ? "lower" : "raise") + " the DPI by about 20% in your mouse software and run the test again. " +
                           "Enter your DPI at the start of the test to get an exact value.");
            return L.T("Sugestão: no programa do seu mouse, experimente ", "Suggestion: in your mouse software, try ") + RoundDpi(r.Dpi * factor) + " DPI (" + direction +
                   L.T(") e faça o teste de novo para comparar.", ") and run the test again to compare.");
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && (phase == Phase.Playing || phase == Phase.Countdown))
            {
                e.Handled = true;
                ShowIntro();
                return;
            }
            base.OnKeyDown(e);
        }
    }

    /// <summary>Área do jogo: desenha alvos, contagem, placar e resultados.</summary>
    internal class AimArena : GControl
    {
        public enum View { Intro, Countdown, Playing, Results }

        public View Mode;
        public PointF Target;
        public float Radius;
        public int CountdownValue;
        public double TimeLeft;
        public int Hits, Misses;
        public string RoundLabel = "";
        public List<RoundResult> Rounds = new List<RoundResult>();
        public RoundResult Best;     // melhor rodada (marcada com ★)
        public RoundResult Current;    // rodada analisada (destacada)
        public List<string> Advice = new List<string>();

        private PointF flashAt;
        private bool flashHit;
        private double flashT = 1;
        private double pulse;

        public AimArena()
        {
            BackColor = Theme.Panel;
        }

        protected override bool Interactive { get { return false; } }

        public void Flash(Point p, bool hit)
        {
            flashAt = p;
            flashHit = hit;
            flashT = 0;
        }

        public void Step()
        {
            pulse += 0.05;
            if (flashT < 1) flashT += 0.08;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.HighQuality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRounded(g, r, 16, Theme.Panel);
            using (GraphicsPath clip = Theme.Rounded(r, 16))
            {
                g.SetClip(clip);
                using (var grid = new Pen(Theme.Alpha(Color.White, 7)))
                {
                    for (float x = 0; x < Width; x += 40) g.DrawLine(grid, x, 0, x, Height);
                    for (float y = 0; y < Height; y += 40) g.DrawLine(grid, 0, y, Width, y);
                }
                switch (Mode)
                {
                    case View.Intro: DrawIntro(g); break;
                    case View.Countdown: DrawCountdown(g); break;
                    case View.Playing: DrawPlaying(g); break;
                    case View.Results: DrawResults(g); break;
                }
                g.ResetClip();
            }
            Theme.StrokeRounded(g, r, 16, Theme.Border, 1f);
        }

        private void DrawIntro(Graphics g)
        {
            float cx = Width / 2f;
            DrawTarget(g, new PointF(cx, Height * 0.24f), 46, 1f);
            var title = Theme.Display(26f);
            using (var tb = Theme.Gradient(new RectangleF(0, 0, Width, 60), Theme.PerfA, Theme.PerfB))
                Theme.DrawSpacedCentered(g, L.T("TESTE DE MIRA", "AIM TEST"), title, tb, new RectangleF(0, Height * 0.38f, Width, 50), 2f);
            Theme.DrawText(g,
                L.T("Alvos aparecem um de cada vez. Leve o mouse até eles e clique o mais rápido e no centro que conseguir.\n" +
                    "O MouseModes mede se você passa do alvo, se para antes, quantas correções faz e a precisão do clique,\n" +
                    "e no fim sugere o que mudar no DPI.",
                    "Targets show up one at a time. Move the mouse to them and click as fast and as centered as you can.\n" +
                    "MouseModes measures whether you overshoot the target, stop short, how many corrections you make and how precise the click is,\n" +
                    "and at the end it suggests what to change in the DPI."),
                Theme.Ui(10.5f), Theme.Muted, new RectangleF(60, Height * 0.50f, Width - 120, 90), StringAlignment.Center);
            using (var lb = new SolidBrush(Theme.Muted))
                Theme.DrawSpacedCentered(g, L.T("SEU DPI ATUAL", "YOUR CURRENT DPI"), Theme.Ui(7.5f, FontStyle.Bold), lb,
                    new RectangleF(0, Height * 0.79f - 22, Width, 18), 1.6f);
            Theme.DrawText(g, L.T("Use a mesma pegada de quando joga · Enter começa · Esc cancela a rodada", "Use the same grip as when you play · Enter starts · Esc cancels the round"), Theme.Ui(9f), Theme.Faint,
                new RectangleF(0, Height * 0.93f, Width, 24), StringAlignment.Center);
        }

        private void DrawCountdown(Graphics g)
        {
            Theme.DrawText(g, RoundLabel, Theme.Display(16f), Theme.Muted, new RectangleF(0, Height * 0.30f, Width, 30), StringAlignment.Center);
            using (var b = Theme.Gradient(new RectangleF(0, 0, Width, Height), Theme.PerfA, Theme.PerfB, 90f))
                Theme.DrawSpacedCentered(g, Math.Max(1, CountdownValue).ToString(), Theme.Display(90f), b, new RectangleF(0, Height * 0.36f, Width, 160), 0f);
            Theme.DrawText(g, L.T("deixe o mouse parado e prepare-se", "keep the mouse still and get ready"), Theme.Ui(10f), Theme.Faint, new RectangleF(0, Height * 0.68f, Width, 24), StringAlignment.Center);
        }

        private void DrawPlaying(Graphics g)
        {
            // Barra de tempo
            var bar = new RectangleF(20, 18, Width - 40, 6);
            Theme.FillRounded(g, bar, 3, Theme.Panel2);
            var fill = new RectangleF(bar.X, bar.Y, (float)(bar.Width * TimeLeft), bar.Height);
            if (fill.Width > 1)
                using (var lb = Theme.Gradient(fill, Theme.PerfA, Theme.PerfB))
                    Theme.FillRounded(g, fill, 3, lb);

            Theme.DrawText(g, RoundLabel, Theme.Display(11f), Theme.Muted, new RectangleF(20, 30, 300, 22));
            Theme.DrawText(g, Hits + L.T(" acertos   ", " hits   ") + Misses + L.T(" erros", " misses"), Theme.Display(11f), Theme.Text,
                new RectangleF(Width - 320, 30, 300, 22), StringAlignment.Far);

            DrawTarget(g, Target, Radius, 1f);

            if (flashT < 1)
            {
                float t = (float)flashT;
                Color c = flashHit ? Color.FromArgb(57, 255, 136) : Theme.Danger;
                float rr = (flashHit ? Radius : 8) + t * 26;
                using (var p = new Pen(Theme.Alpha(c, (int)(220 * (1 - t))), 2.5f))
                    g.DrawEllipse(p, flashAt.X - rr, flashAt.Y - rr, rr * 2, rr * 2);
            }
        }

        private static void DrawTarget(Graphics g, PointF c, float radius, float alpha)
        {
            for (int i = 3; i >= 1; i--)
                using (var halo = new SolidBrush(Theme.Alpha(Theme.PerfA, (int)(18 * i * alpha))))
                    g.FillEllipse(halo, c.X - radius - i * 5, c.Y - radius - i * 5, (radius + i * 5) * 2, (radius + i * 5) * 2);
            var outer = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            using (var lb = Theme.Gradient(outer, Theme.PerfA, Theme.PerfB, 45f))
                g.FillEllipse(lb, outer);
            float r2 = radius * 0.62f;
            using (var b = new SolidBrush(Theme.Panel)) g.FillEllipse(b, c.X - r2, c.Y - r2, r2 * 2, r2 * 2);
            float r3 = radius * 0.3f;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, c.X - r3, c.Y - r3, r3 * 2, r3 * 2);
        }

        private void DrawResults(Graphics g)
        {
            float x = 36, y = 28;
            Theme.DrawSpaced(g, L.T("RESULTADO", "RESULT"), Theme.Ui(8f, FontStyle.Bold), Theme.Muted, x, y, 2f);
            y += 24;

            // Tabela: uma linha por rodada
            string[] head =
            {
                "DPI", L.T("PONTOS/MIN", "POINTS/MIN"), L.T("ACERTOS", "HITS"), L.T("ERROS", "MISSES"), L.T("TEMPO MÉDIO", "AVG TIME"),
                L.T("PASSOU DO ALVO", "OVERSHOT"), L.T("PARADA DO 1º MOV.", "1ST MOVE STOP")
            };
            float[] col = { 0, 150, 290, 390, 480, 620, 770 };
            for (int i = 0; i < head.Length; i++)
                Theme.DrawSpaced(g, head[i], Theme.Ui(7f, FontStyle.Bold), Theme.Faint, x + col[i], y, 1.2f);
            y += 22;
            foreach (var r in Rounds)
            {
                bool best = Current != null ? r == Current : r == Best;
                var row = new RectangleF(x - 12, y - 6, Width - 2 * x + 24, 34);
                if (best)
                {
                    Theme.FillRounded(g, row, 8, Theme.Alpha(Theme.PerfA, 40));
                    Theme.StrokeRounded(g, row, 8, Theme.Alpha(Theme.PerfA, 160), 1f);
                }
                Color fg = best ? Color.White : Theme.Text;
                Font f = Theme.Display(12f);
                string[] vals =
                {
                    (r == Best ? "★ " : "") + r.Label,
                    r.Score.ToString("0"),
                    r.Hits.ToString(),
                    r.Misses.ToString(),
                    r.Hits > 0 ? r.Median(t => t.TimeMs).ToString("0") + " ms" : "—",
                    r.Hits > 0 ? r.OvershootRate.ToString("0%") : "—",
                    r.Hits > 0 ? (r.Median(t => t.EndRatio) * 100).ToString("0") + "%" : "—"
                };
                for (int i = 0; i < vals.Length; i++)
                    Theme.DrawText(g, vals[i], f, fg, new RectangleF(x + col[i], y, 150, 24), wrap: false);
                y += 40;
            }

            y += 10;
            Theme.DrawSpaced(g, L.T("O QUE FAZER", "WHAT TO DO"), Theme.Ui(8f, FontStyle.Bold), Theme.Muted, x, y, 2f);
            y += 26;
            foreach (string line in Advice)
            {
                using (var dot = new SolidBrush(Theme.PerfB)) g.FillEllipse(dot, x, y + 7, 6, 6);
                SizeF size = g.MeasureString(line, Theme.Ui(10f), (int)(Width - 2 * x - 20));
                Theme.DrawText(g, line, Theme.Ui(10f), Theme.Text, new RectangleF(x + 16, y, Width - 2 * x - 20, size.Height + 4));
                y += size.Height + 10;
            }

            Theme.DrawText(g, L.T("Parada do 1º movimento: 100% = parou em cima do alvo · acima passa do alvo · abaixo para antes.", "1st move stop: 100% = stopped on the target · above overshoots · below stops short."),
                Theme.Ui(8.5f), Theme.Faint, new RectangleF(x, Height - 34, Width - 2 * x, 20));
        }
    }
}
