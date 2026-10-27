using System;
using System.Collections.Generic;
using System.Drawing;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Cálculos do teste de mira com trajetos e rodadas de mentira (sem abrir a janela).</summary>
    public class AimTestTests
    {
        private static readonly PointF From = new PointF(0, 0);
        private static readonly PointF Target = new PointF(300, 0);
        private const float Radius = 22;

        /// <summary>Movimento suave (acelera e freia) de x0 a x1, em amostras de 8 ms a partir de t0.</summary>
        private static double Move(List<double[]> path, double t0, double ms, double x0, double x1)
        {
            for (double t = 8; t <= ms; t += 8)
            {
                double u = t / ms;
                double ease = u * u * (3 - 2 * u);
                path.Add(new[] { t0 + t, x0 + (x1 - x0) * ease, 0 });
            }
            return t0 + ms;
        }

        private static List<double[]> Start()
        {
            return new List<double[]> { new double[] { 0, 0, 0 } };
        }

        [Fact]
        public void Analyze_StraightToTheTarget()
        {
            var path = Start();
            Move(path, 0, 400, 0, 300);

            TargetResult r = AimTestForm.Analyze(path, From, Target, Radius, 0, 0.1, 0);

            Assert.InRange(r.EndRatio, 0.95, 1.05);
            Assert.False(r.Overshot);
            Assert.Equal(0, r.Corrections);
            Assert.Equal(300, r.Distance, 3);
            Assert.Equal(400, r.TimeMs, 3);
        }

        [Fact]
        public void Analyze_PassingTheTargetAndComingBack()
        {
            var path = Start();
            double t = Move(path, 0, 400, 0, 380);
            Move(path, t, 250, 380, 300);

            TargetResult r = AimTestForm.Analyze(path, From, Target, Radius, 0, 0.1, 0);

            Assert.True(r.Overshot);
            Assert.True(r.EndRatio > 1.1, "parada em " + r.EndRatio);
        }

        [Fact]
        public void Analyze_StoppingShortAndCorrecting()
        {
            var path = Start();
            double t = Move(path, 0, 400, 0, 200);
            path.Add(new double[] { t + 100, 200, 0 }); // parado 100 ms
            Move(path, t + 100, 300, 200, 300);

            TargetResult r = AimTestForm.Analyze(path, From, Target, Radius, 0, 0.1, 2);

            Assert.InRange(r.EndRatio, 0.6, 0.75);
            Assert.False(r.Overshot);
            Assert.True(r.Corrections >= 1, "correções: " + r.Corrections);
            Assert.Equal(2, r.Misses);
        }

        [Theory]
        [InlineData(1700, 1700)]
        [InlineData(1724, 1700)]
        [InlineData(1726, 1750)]
        [InlineData(10, 100)]
        [InlineData(99999, 26000)]
        public void RoundDpi_StepsOf50WithinLimits(double dpi, int rounded)
        {
            Assert.Equal(rounded, AimTestForm.RoundDpi(dpi));
        }

        private static RoundResult Round(int dpi, double endRatio, bool overshot, int corrections, int targets = 10, int misses = 0)
        {
            var r = new RoundResult { Label = dpi > 0 ? dpi + " DPI" : "DPI ?", Dpi = dpi, Seconds = 20, PendingMisses = misses };
            for (int i = 0; i < targets; i++)
                r.Targets.Add(new TargetResult { EndRatio = endRatio, Overshot = overshot, Corrections = corrections, ClickOffset = 0.2, TimeMs = 600 });
            return r;
        }

        [Fact]
        public void Score_RewardsHitsAndDiscountsMisses()
        {
            RoundResult clean = Round(1600, 1, false, 0, targets: 20);
            RoundResult sloppy = Round(1600, 1, false, 0, targets: 20, misses: 10);

            Assert.Equal(20 * 10 * (1.15 - 0.3 * 0.2) * 60 / 20, clean.Score, 6);
            Assert.True(sloppy.Score < clean.Score);
            Assert.Equal(10 / 30.0, sloppy.MissRate, 6);
            Assert.Equal(0, new RoundResult().Score);
        }

        [Fact]
        public void BestOf_IgnoresRoundsWithoutHits()
        {
            RoundResult low = Round(800, 1, false, 0, targets: 5);
            RoundResult high = Round(1600, 1, false, 0, targets: 15);
            RoundResult empty = Round(3200, 1, false, 0, targets: 0);
            Assert.Same(high, AimTestForm.BestOf(new List<RoundResult> { low, high, empty }));
            Assert.Null(AimTestForm.BestOf(new List<RoundResult> { empty }));
        }

        private static string All(List<string> lines)
        {
            return string.Join(" | ", lines.ToArray());
        }

        [Fact]
        public void Advise_TooFastSuggestsALowerDpi()
        {
            RoundResult r = Round(1700, 1.3, true, 0);
            string advice = All(AimTestForm.Advise(new List<RoundResult> { r }, r, new MouseProfile { PointerSpeed = 10 }));
            Assert.Contains("passar do alvo", advice);
            Assert.Contains("1350 DPI (mais baixo)", advice);
        }

        [Fact]
        public void Advise_TooSlowSuggestsAHigherDpi()
        {
            RoundResult r = Round(800, 0.7, false, 2);
            string advice = All(AimTestForm.Advise(new List<RoundResult> { r }, r, null));
            Assert.Contains("para antes do alvo", advice);
            Assert.Contains("1000 DPI (mais alto)", advice);
        }

        [Fact]
        public void Advise_BalancedAndOtherTips()
        {
            RoundResult r = Round(1600, 1.0, false, 0);
            var accelOn = new MouseProfile { PointerSpeed = 12, EnhancePointerPrecision = true };
            string advice = All(AimTestForm.Advise(new List<RoundResult> { r }, r, accelOn));
            Assert.Contains("1600 DPI está equilibrado", advice);
            Assert.Contains("aceleração do Windows está ligada", advice);
            Assert.Contains("velocidade do Windows 12/20", advice);
        }

        [Fact]
        public void Advise_WithoutDpiOrHits()
        {
            RoundResult noDpi = Round(0, 1.3, true, 0);
            Assert.Contains("baixe o DPI uns 20%", All(AimTestForm.Advise(new List<RoundResult> { noDpi }, noDpi, null)));

            RoundResult none = Round(1600, 1, false, 0, targets: 0);
            Assert.Contains("Nenhum alvo acertado", All(AimTestForm.Advise(new List<RoundResult> { none }, none, null)));
        }

        [Fact]
        public void Advise_MentionsTheBestEarlierRound()
        {
            RoundResult earlier = Round(800, 1, false, 0, targets: 20);
            RoundResult now = Round(1600, 1, false, 0, targets: 8);
            string advice = All(AimTestForm.Advise(new List<RoundResult> { earlier, now }, now, null));
            Assert.Contains("Seu melhor resultado até agora: 800 DPI", advice);
        }
    }
}
