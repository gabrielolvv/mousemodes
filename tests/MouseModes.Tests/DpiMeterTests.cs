using System;
using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>DPI real: a conta das contagens do sensor numa distância conhecida.</summary>
    public class DpiMeterTests
    {
        [Fact]
        public void Dpi_IsCountsPerInch()
        {
            // 10 cm a 800 DPI de verdade: 800 × 10 / 2,54 ≈ 3150 contagens.
            double inches = DpiMeter.Inches(10, false);
            Assert.Equal(800, DpiMeter.Dpi(3150, 0, inches), 0);
            Assert.Equal(1600, DpiMeter.Dpi(6400, 0, DpiMeter.Inches(4, true)), 6);
        }

        [Fact]
        public void Dpi_UsesTheStraightLineAndIgnoresTheDirection()
        {
            // Régua na diagonal ou arrasto para a esquerda/para cima: vale o deslocamento, não o sinal.
            Assert.Equal(1000, DpiMeter.Dpi(3000, 4000, 5), 6);
            Assert.Equal(1000, DpiMeter.Dpi(-3000, -4000, 5), 6);
            Assert.Equal(5000, DpiMeter.Counts(-3000, 4000));
        }

        [Fact]
        public void Dpi_RefusesAZeroDistance()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DpiMeter.Dpi(100, 0, 0));
        }

        [Fact]
        public void Median_HandlesOddAndEvenCounts()
        {
            Assert.Equal(770, DpiMeter.Median(new List<double> { 790, 760, 770 }));
            Assert.Equal(765, DpiMeter.Median(new List<double> { 790, 760, 770, 740 }));
        }

        [Fact]
        public void Spread_IsTheRangeOverTheMedian()
        {
            Assert.Equal(0, DpiMeter.Spread(new List<double> { 763 }));
            Assert.Equal(0.1, DpiMeter.Spread(new List<double> { 760, 800, 840 }), 6);
        }

        [Fact]
        public void Deviation_IsRelativeToTheDeclaredDpi()
        {
            Assert.Equal(-0.04625, DpiMeter.Deviation(763, 800), 6);
            Assert.Equal(0.05, DpiMeter.Deviation(1680, 1600), 6);
        }
    }
}
