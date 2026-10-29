using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>
    /// Mede quantas vezes por segundo o mouse manda movimento (polling rate) usando Raw Input,
    /// o mesmo caminho que os jogos usam. Mostra também a variação entre leituras (jitter).
    /// </summary>
    public class PollingRateForm : GamerWindow
    {
        private const int MaxSamples = 400;
        private const double PauseMs = 50; // intervalos maiores = mouse parado, não contam

        private static readonly int[] StandardRates = { 125, 250, 500, 1000, 2000, 4000, 8000 };

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<double> intervals = new List<double>();
        private readonly BigReadout readout;
        private readonly ChipRow chips;
        private readonly IntervalGraph graph;
        private readonly Label status;
        private readonly Timer refresh;
        private readonly RawMouse raw = new RawMouse();
        private double lastEventMs = -1;
        private double lastMoveMs = -1;
        private int peakRate;
        private double phase;

        public PollingRateForm() : base("Polling rate", true)
        {
            CloseOnEscape = true;
            ClientSize = new Size(480, 578);
            TitleBar.AccentA = Theme.PerfA;
            TitleBar.AccentB = Theme.PerfB;

            var help = Note(L.T("Mexa o mouse rápido, em círculos, por uns 3 segundos.", "Move the mouse fast, in circles, for about 3 seconds."), 20, 56, 440, 20, Theme.Muted, 9f);
            help.TextAlign = ContentAlignment.MiddleCenter;

            readout = new BigReadout { Location = new Point(12, 80), Size = new Size(456, 160), Caption = L.T("aguardando movimento", "waiting for movement") };
            chips = new ChipRow
            {
                Location = new Point(20, 250),
                Size = new Size(440, 48),
                Labels = new[] { L.T("MEDIDO", "MEASURED"), L.T("PICO", "PEAK"), L.T("INTERVALO", "INTERVAL"), "JITTER" },
                Values = new[] { "—", "—", "—", "—" }
            };
            var graphLabel = new SectionLabel { Text = L.T("Intervalo entre leituras (últimas 120)", "Interval between reports (last 120)"), Location = new Point(20, 314), Size = new Size(440, 20) };
            graph = new IntervalGraph { Location = new Point(20, 338), Size = new Size(440, 120) };
            status = Note("", 20, 466, 440, 36, Theme.Faint, 8.5f);

            var reset = new NeonButton { Text = L.T("Zerar", "Reset"), Location = new Point(20, 516), Size = new Size(440, 44), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            reset.Click += (s, e) => Reset();

            Controls.Add(help);
            Controls.Add(readout);
            Controls.Add(chips);
            Controls.Add(graphLabel);
            Controls.Add(graph);
            Controls.Add(status);
            Controls.Add(reset);

            refresh = new Timer { Interval = 50 };
            refresh.Tick += (s, e) => UpdateView();
            refresh.Start();

            FinishLayout();
        }

        private void Reset()
        {
            intervals.Clear();
            peakRate = 0;
            lastEventMs = -1;
            readout.Value = "—";
            readout.Caption = L.T("aguardando movimento", "waiting for movement");
            chips.Values = new[] { "—", "—", "—", "—" };
            status.Text = "";
            graph.SetSamples(intervals, 120);
            readout.Invalidate();
            chips.Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            string error;
            if (!raw.Register(this, true, OnRawInput, out error))
            {
                status.Text = L.T("Raw Input indisponível: ", "Raw Input unavailable: ") + error;
                Log.Warn(L.T("Medidor de polling: Raw Input indisponível (", "Polling meter: Raw Input unavailable (") + error + ")");
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            refresh.Stop();
            raw.Dispose();
            base.OnFormClosed(e);
        }

        private void OnRawInput(int dx, int dy, ushort buttons)
        {
            if (dx == 0 && dy == 0) return; // clique ou roda, não é movimento

            double now = clock.Elapsed.TotalMilliseconds;
            if (lastEventMs >= 0)
            {
                double delta = now - lastEventMs;
                if (delta > 0 && delta < PauseMs)
                {
                    intervals.Add(delta);
                    if (intervals.Count > MaxSamples) intervals.RemoveAt(0);
                }
            }
            lastEventMs = now;
            lastMoveMs = now;
        }

        private void UpdateView()
        {
            phase += 0.08;
            readout.Pulse = (float)((Math.Sin(phase) + 1) / 2);
            readout.Invalidate();

            if (intervals.Count < 50) return;

            var sorted = new List<double>(intervals);
            sorted.Sort();
            double median = sorted[sorted.Count / 2];
            if (median <= 0) return;

            int rate = (int)Math.Round(1000.0 / median);
            if (rate > peakRate) peakRate = rate;

            double mean = 0;
            foreach (double v in intervals) mean += v;
            mean /= intervals.Count;
            double variance = 0;
            foreach (double v in intervals) variance += (v - mean) * (v - mean);
            double jitter = Math.Sqrt(variance / intervals.Count);

            bool moving = clock.Elapsed.TotalMilliseconds - lastMoveMs < 300;
            readout.Value = Nearest(rate).ToString();
            readout.Caption = moving ? L.T("medindo...", "measuring...") : L.T("mouse parado — mexa de novo para continuar", "mouse stopped — move it again to continue");
            chips.Values = new[]
            {
                rate + " Hz",
                peakRate + " Hz",
                median.ToString("0.00") + " ms",
                jitter.ToString("0.00") + " ms"
            };
            chips.Invalidate();
            if (moving) graph.SetSamples(intervals, 120);
            status.Text = rate >= 3000
                ? L.T("Acima de 2000 Hz o Windows pode juntar leituras; o valor real pode ser maior.", "Above 2000 Hz Windows may merge reports; the real value may be higher.")
                : L.T("O número grande é o padrão mais próximo do medido (125, 250, 500, 1000...).", "The big number is the standard rate closest to what was measured (125, 250, 500, 1000...).");
        }

        private static int Nearest(int rate)
        {
            int best = StandardRates[0];
            foreach (int r in StandardRates)
            {
                if (Math.Abs(r - rate) < Math.Abs(best - rate)) best = r;
            }
            return best;
        }
    }
}
