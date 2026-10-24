using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>O que o teste de mira precisa do resto do app (DPI do mouse, aplicar resultado).</summary>
    public class AimTestHost
    {
        /// <summary>Estágios do mouse, ou null se não houver Darmoshark compatível.</summary>
        public Func<DpiStages> LoadStages;
        /// <summary>Ativa um estágio (índice). Retorna mensagem de erro ou null.</summary>
        public Func<int, string> SetStage;
        /// <summary>Devolve o DPI do modo atual ao terminar o teste.</summary>
        public Action Restore;
        /// <summary>Grava no modo Performance: (estágio ou -1, DPI exato ou 0).</summary>
        public Action<int, int> ApplyToPerformance;
        /// <summary>DPI em uso agora (texto), para mostrar no teste rápido.</summary>
        public Func<string> CurrentDpi;
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
        public int StageIndex = -1;
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

        private const double QuickSeconds = 20, CompareSeconds = 15, CountdownSeconds = 3;

        private readonly AimTestHost host;
        private readonly AimArena arena;
        private readonly NeonButton quickButton;
        private readonly NeonButton compareButton;
        private readonly NeonButton applyButton;
        private readonly NeonButton againButton;
        private readonly Label footer;
        private readonly Timer frame;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random rnd = new Random();

        private Phase phase = Phase.Intro;
        private DpiStages stages;
        private readonly List<RoundResult> rounds = new List<RoundResult>();
        private readonly Queue<int> pendingStages = new Queue<int>();
        private RoundResult round;
        private double phaseStart;
        private double roundSeconds;
        private bool dpiChanged;

        // alvo atual
        private PointF target;
        private float radius;
        private double spawnTime;
        private PointF spawnCursor;
        private readonly List<double[]> path = new List<double[]>(); // t, x, y
        private int targetMisses;

        // sugestão final
        private int suggestStage = -1, suggestCustom;

        public AimTestForm(AimTestHost host) : base("Teste de mira", true)
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

            quickButton = new NeonButton { Text = "Teste rápido (20 s)", Style = ButtonStyle.Primary, Location = new Point(20, 656), Size = new Size(240, 48), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            compareButton = new NeonButton { Text = "Comparar DPIs", Glyph = GlyphKind.Gauge, Location = new Point(272, 656), Size = new Size(240, 48), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            applyButton = new NeonButton { Text = "Usar no Performance", Style = ButtonStyle.Primary, Location = new Point(20, 656), Size = new Size(330, 48), AccentA = Theme.PerfA, AccentB = Theme.PerfB, Visible = false };
            againButton = new NeonButton { Text = "Testar de novo", Location = new Point(362, 656), Size = new Size(200, 48), AccentA = Theme.PerfA, AccentB = Theme.PerfB, Visible = false };
            quickButton.Click += (s, e) => StartQuick();
            compareButton.Click += (s, e) => StartCompare();
            applyButton.Click += (s, e) => ApplySuggestion();
            againButton.Click += (s, e) => ShowIntro();
            Controls.Add(quickButton);
            Controls.Add(compareButton);
            Controls.Add(applyButton);
            Controls.Add(againButton);

            footer = Note("", 580, 660, 440, 44, Theme.Faint, 8.5f);
            Controls.Add(footer);

            frame = new Timer { Interval = 15 };
            frame.Tick += (s, e) => Tick();
            frame.Start();

            FinishLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            stages = host.LoadStages != null ? host.LoadStages() : null;
            ShowIntro();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            frame.Stop();
            RestoreDpi();
            base.OnFormClosed(e);
        }

        // ------------------------------------------------------------------ fluxo

        private void ShowIntro()
        {
            phase = Phase.Intro;
            RestoreDpi();
            rounds.Clear();
            pendingStages.Clear();
            quickButton.Visible = compareButton.Visible = true;
            applyButton.Visible = againButton.Visible = false;
            bool canCompare = stages != null && CompareCandidates().Count >= 2;
            compareButton.Enabled = canCompare;
            footer.Text = canCompare
                ? "Comparar DPIs troca o DPI do seu mouse a cada rodada (" + string.Join(", ", CompareCandidates().ConvertAll(i => stages.Values[i].ToString()).ToArray()) + ") e volta ao normal no fim."
                : "Comparar DPIs precisa de um mouse Darmoshark compatível conectado.";
            arena.Cursor = Cursors.Default;
            arena.Mode = AimArena.View.Intro;
            arena.Invalidate();
        }

        private List<int> CompareCandidates()
        {
            var list = new List<int>();
            if (stages == null) return list;
            for (int i = 0; i < stages.Count; i++)
                if (stages.Values[i] >= 200 && stages.Values[i] <= 3200) list.Add(i);
            if (list.Count < 2)
            {
                list.Clear();
                for (int i = 0; i < Math.Min(4, stages.Count); i++) list.Add(i);
            }
            if (list.Count > 4) list = list.GetRange(0, 4);
            return list;
        }

        private void StartQuick()
        {
            rounds.Clear();
            pendingStages.Clear();
            string dpi = host.CurrentDpi != null ? host.CurrentDpi() : "";
            int dpiValue;
            int.TryParse(dpi, out dpiValue);
            BeginRound(new RoundResult { Label = dpiValue > 0 ? dpiValue + " DPI" : "DPI atual", Dpi = dpiValue }, QuickSeconds);
        }

        private void StartCompare()
        {
            rounds.Clear();
            pendingStages.Clear();
            // Ordem embaralhada: assim o aquecimento não favorece sempre o mesmo DPI.
            var order = CompareCandidates();
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }
            foreach (int i in order) pendingStages.Enqueue(i);
            NextCompareRound();
        }

        private void NextCompareRound()
        {
            if (pendingStages.Count == 0) { ShowResults(); return; }
            int stage = pendingStages.Dequeue();
            string err = host.SetStage != null ? host.SetStage(stage) : "indisponível";
            if (err != null)
            {
                Log.Warn("Teste de mira: não consegui trocar o DPI (" + err + ")");
                footer.Text = "Não consegui trocar o DPI: " + err;
                ShowResults();
                return;
            }
            dpiChanged = true;
            BeginRound(new RoundResult { Label = stages.Values[stage] + " DPI", StageIndex = stage, Dpi = stages.Values[stage] }, CompareSeconds);
        }

        private void BeginRound(RoundResult r, double seconds)
        {
            round = r;
            roundSeconds = seconds;
            phase = Phase.Countdown;
            phaseStart = clock.Elapsed.TotalSeconds;
            quickButton.Visible = compareButton.Visible = applyButton.Visible = againButton.Visible = false;
            int total = rounds.Count + 1 + pendingStages.Count;
            footer.Text = total > 1 ? "Rodada " + (rounds.Count + 1) + " de " + total + " · Esc cancela" : "Acerte o máximo de alvos, rápido e no centro · Esc cancela";
            arena.Cursor = Cursors.Cross;
            arena.Mode = AimArena.View.Countdown;
            arena.RoundLabel = r.Label;
            arena.Invalidate();
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
                arena.TimeLeft = Math.Max(0, 1 - elapsed / roundSeconds);
                arena.Hits = round.Hits;
                arena.Misses = round.Misses;
                if (elapsed >= roundSeconds)
                {
                    round.Seconds = roundSeconds;
                    round.PendingMisses += targetMisses;
                    targetMisses = 0;
                    rounds.Add(round);
                    Log.Info(string.Format("Teste de mira: {0} · {1} acertos, {2} erros, tempo {3:0} ms, parada {4:0.00}, passou {5:0%}",
                        round.Label, round.Hits, round.Misses, round.Median(t => t.TimeMs), round.Median(t => t.EndRatio), round.OvershootRate));
                    if (pendingStages.Count > 0) NextCompareRound();
                    else ShowResults();
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
            round.Targets.Add(Analyze(d / radius));
            arena.Flash(p, true);
            SpawnTarget(p);
        }

        // ------------------------------------------------------------------ medições

        private TargetResult Analyze(double clickOffset)
        {
            var r = new TargetResult { ClickOffset = clickOffset, Misses = targetMisses };
            double dx = target.X - spawnCursor.X, dy = target.Y - spawnCursor.Y;
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
                proj[i] = (path[i][1] - spawnCursor.X) * ux + (path[i][2] - spawnCursor.Y) * uy;
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
            RestoreDpi();
            arena.Cursor = Cursors.Default;
            suggestStage = -1;
            suggestCustom = 0;

            var lines = new List<string>();
            RoundResult best = null;
            foreach (var r in rounds)
                if (r.Hits > 0 && (best == null || r.Score > best.Score)) best = r;

            if (best == null)
            {
                lines.Add("Nenhum alvo acertado. Tente de novo com calma: o objetivo é acertar rápido e no centro.");
            }
            else
            {
                double end = best.Median(t => t.EndRatio);
                double corr = best.Average(t => t.Corrections);
                double offset = best.Average(t => t.ClickOffset);

                if (rounds.Count > 1)
                {
                    lines.Add("Melhor resultado: " + best.Label + " (" + best.Score.ToString("0") + " pontos/min).");
                    var second = rounds.FindAll(r => r != best && r.Hits > 0);
                    second.Sort((a, b) => b.Score.CompareTo(a.Score));
                    if (second.Count > 0 && best.Score - second[0].Score < best.Score * 0.05)
                        lines.Add("Ficou bem perto de " + second[0].Label + ": os dois servem, escolha o que parecer mais confortável.");
                    if (best.StageIndex >= 0) suggestStage = best.StageIndex;
                }

                // Parada do 1º movimento: >1 passa do alvo (rápido demais), <1 para antes (lento demais).
                if (end > 1.10 || best.OvershootRate > 0.35)
                {
                    lines.Add("Você costuma passar do alvo (" + best.OvershootRate.ToString("0%") + " das vezes) e voltar: o ponteiro está rápido para a sua mão.");
                    if (rounds.Count == 1) Suggest(best, 0.8, lines, "mais baixo");
                    else lines.Add("Se continuar passando, teste um DPI um pouco abaixo de " + best.Dpi + ".");
                }
                else if (end < 0.80 && corr > 1.2)
                {
                    lines.Add("Seu 1º movimento para antes do alvo e precisa de correções (" + corr.ToString("0.0") + " por alvo): o ponteiro está lento para a sua mão.");
                    if (rounds.Count == 1) Suggest(best, 1.25, lines, "mais alto");
                    else lines.Add("Se continuar parando antes, teste um DPI um pouco acima de " + best.Dpi + ".");
                }
                else
                {
                    lines.Add("O 1º movimento chega no alvo (parada em " + (end * 100).ToString("0") + "% da distância): o DPI está equilibrado para a sua mão.");
                }

                if (best.MissRate > 0.25)
                    lines.Add("Muitos cliques fora (" + best.MissRate.ToString("0%") + "): você está clicando antes de parar no alvo. Priorize acertar; a velocidade vem com o treino.");
                else if (offset > 0.6)
                    lines.Add("Os acertos estão longe do centro: mire no meio do alvo antes de clicar.");

                MouseProfile current = host.CurrentProfile != null ? host.CurrentProfile() : null;
                if (current != null && current.EnhancePointerPrecision)
                    lines.Add("A aceleração do Windows está ligada neste modo: desligue para o mesmo gesto sempre levar ao mesmo lugar.");
                if (current != null && current.PointerSpeed != 10)
                    lines.Add("Teste feito com velocidade do Windows " + current.PointerSpeed + "/20; no jogo vale DPI × sensibilidade do jogo.");
                else
                    lines.Add("No jogo, a sensibilidade final é DPI × sensibilidade do jogo; ajuste a do jogo depois de fixar o DPI.");
            }

            arena.Mode = AimArena.View.Results;
            arena.Rounds = rounds;
            arena.Best = best;
            arena.Advice = lines;
            arena.Invalidate();

            quickButton.Visible = compareButton.Visible = false;
            againButton.Visible = true;
            bool canApply = host.ApplyToPerformance != null && (suggestStage >= 0 || suggestCustom > 0);
            applyButton.Visible = canApply;
            againButton.Location = new Point(canApply ? 362 : 20, againButton.Location.Y);
            if (canApply)
                applyButton.Text = "Usar " + (suggestCustom > 0 ? suggestCustom : stages.Values[suggestStage]) + " DPI no Performance";
            footer.Text = "Sugestões são um ponto de partida: repita o teste algumas vezes antes de decidir.";
        }

        /// <summary>Teste rápido: sugere um estágio vizinho ou um DPI exato.</summary>
        private void Suggest(RoundResult r, double factor, List<string> lines, string direction)
        {
            if (r.Dpi <= 0 || stages == null) return;
            int wanted = DpiController.NormalizeDpi((int)(r.Dpi * factor));
            for (int i = 0; i < stages.Count; i++)
            {
                bool ok = factor < 1 ? stages.Values[i] < r.Dpi && stages.Values[i] >= r.Dpi * 0.6
                                     : stages.Values[i] > r.Dpi && stages.Values[i] <= r.Dpi * 1.6;
                if (ok && (suggestStage < 0 || Math.Abs(stages.Values[i] - wanted) < Math.Abs(stages.Values[suggestStage] - wanted)))
                    suggestStage = i;
            }
            if (suggestStage >= 0)
                lines.Add("Sugestão: experimente o estágio de " + stages.Values[suggestStage] + " DPI (" + direction + ").");
            else
            {
                suggestCustom = wanted;
                lines.Add("Sugestão: experimente " + wanted + " DPI (" + direction + ").");
            }
        }

        private void ApplySuggestion()
        {
            if (host.ApplyToPerformance == null) return;
            if (suggestCustom > 0) host.ApplyToPerformance(-1, suggestCustom);
            else if (suggestStage >= 0) host.ApplyToPerformance(suggestStage, 0);
            applyButton.Text = "Aplicado no Performance ✓";
            applyButton.Enabled = false;
        }

        private void RestoreDpi()
        {
            if (!dpiChanged) return;
            dpiChanged = false;
            try { if (host.Restore != null) host.Restore(); }
            catch (Exception ex) { Log.Error("Teste de mira: restaurar DPI", ex); }
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
        public RoundResult Best;
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
            DrawTarget(g, new PointF(cx, Height * 0.32f), 46, 1f);
            var title = Theme.Display(26f);
            using (var tb = Theme.Gradient(new RectangleF(0, 0, Width, 60), Theme.PerfA, Theme.PerfB))
                Theme.DrawSpacedCentered(g, "TESTE DE MIRA", title, tb, new RectangleF(0, Height * 0.46f, Width, 50), 2f);
            Theme.DrawText(g,
                "Alvos aparecem um de cada vez. Leve o mouse até eles e clique o mais rápido e no centro que conseguir.\n" +
                "O MouseModes mede se você passa do alvo, se para antes, quantas correções faz e a precisão do clique,\n" +
                "e no fim sugere o que mudar no DPI.",
                Theme.Ui(10.5f), Theme.Muted, new RectangleF(60, Height * 0.58f, Width - 120, 90), StringAlignment.Center);
            Theme.DrawText(g, "Use a mesma pegada de quando joga · Esc cancela a rodada", Theme.Ui(9f), Theme.Faint,
                new RectangleF(0, Height * 0.78f, Width, 24), StringAlignment.Center);
        }

        private void DrawCountdown(Graphics g)
        {
            Theme.DrawText(g, RoundLabel, Theme.Display(16f), Theme.Muted, new RectangleF(0, Height * 0.30f, Width, 30), StringAlignment.Center);
            using (var b = Theme.Gradient(new RectangleF(0, 0, Width, Height), Theme.PerfA, Theme.PerfB, 90f))
                Theme.DrawSpacedCentered(g, Math.Max(1, CountdownValue).ToString(), Theme.Display(90f), b, new RectangleF(0, Height * 0.36f, Width, 160), 0f);
            Theme.DrawText(g, "deixe o mouse parado e prepare-se", Theme.Ui(10f), Theme.Faint, new RectangleF(0, Height * 0.68f, Width, 24), StringAlignment.Center);
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
            Theme.DrawText(g, Hits + " acertos   " + Misses + " erros", Theme.Display(11f), Theme.Text,
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
            Theme.DrawSpaced(g, "RESULTADO", Theme.Ui(8f, FontStyle.Bold), Theme.Muted, x, y, 2f);
            y += 24;

            // Tabela: uma linha por rodada
            string[] head = { "DPI", "PONTOS/MIN", "ACERTOS", "ERROS", "TEMPO MÉDIO", "PASSOU DO ALVO", "PARADA DO 1º MOV." };
            float[] col = { 0, 150, 290, 390, 480, 620, 770 };
            for (int i = 0; i < head.Length; i++)
                Theme.DrawSpaced(g, head[i], Theme.Ui(7f, FontStyle.Bold), Theme.Faint, x + col[i], y, 1.2f);
            y += 22;
            foreach (var r in Rounds)
            {
                bool best = r == Best;
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
                    r.Label,
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
            Theme.DrawSpaced(g, "O QUE FAZER", Theme.Ui(8f, FontStyle.Bold), Theme.Muted, x, y, 2f);
            y += 26;
            foreach (string line in Advice)
            {
                using (var dot = new SolidBrush(Theme.PerfB)) g.FillEllipse(dot, x, y + 7, 6, 6);
                SizeF size = g.MeasureString(line, Theme.Ui(10f), (int)(Width - 2 * x - 20));
                Theme.DrawText(g, line, Theme.Ui(10f), Theme.Text, new RectangleF(x + 16, y, Width - 2 * x - 20, size.Height + 4));
                y += size.Height + 10;
            }

            Theme.DrawText(g, "Parada do 1º movimento: 100% = parou em cima do alvo · acima passa do alvo · abaixo para antes.",
                Theme.Ui(8.5f), Theme.Faint, new RectangleF(x, Height - 34, Width - 2 * x, 20));
        }
    }
}
