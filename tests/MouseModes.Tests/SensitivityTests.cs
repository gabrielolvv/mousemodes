using System;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Conversor de sensibilidade: só contas, nada lê nem muda o mouse.</summary>
    public class SensitivityTests
    {
        [Fact]
        public void Cm360_MatchesTheKnownValueForCs2()
        {
            // CS2 com sensibilidade 1 e 800 DPI: 360 / (1 × 0,022 × 800) polegadas = 20,45 pol = 51,95 cm.
            Assert.Equal(51.95, Sensitivity.Cm360(1, 0.022, 800), 2);
        }

        [Theory]
        [InlineData(1.0, 0.022, 800, 0.07, 800, 0.3143)]    // CS2 → Valorant: divide por 3,18
        [InlineData(1.0, 0.022, 800, 0.0066, 800, 3.3333)]  // CS2 → Overwatch 2: multiplica por 3,33
        [InlineData(0.4, 0.07, 800, 0.07, 1600, 0.2)]       // mesmo jogo, DPI dobrado: metade da sensibilidade
        public void Convert_KeepsTheSameDistanceFor360(double sens, double yawFrom, int dpiFrom, double yawTo, int dpiTo, double expected)
        {
            double converted = Sensitivity.Convert(sens, yawFrom, dpiFrom, yawTo, dpiTo);
            Assert.Equal(expected, converted, 4);
            Assert.Equal(Sensitivity.Cm360(sens, yawFrom, dpiFrom), Sensitivity.Cm360(converted, yawTo, dpiTo), 6);
        }

        [Theory]
        [InlineData("1.25", 1.25)]
        [InlineData("1,25", 1.25)]
        [InlineData(" 0.4 ", 0.4)]
        [InlineData("2", 2.0)]
        public void TryParse_AcceptsDotAndComma(string text, double expected)
        {
            double value;
            Assert.True(Sensitivity.TryParse(text, out value));
            Assert.Equal(expected, value, 6);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("1.2.3")]
        public void TryParse_RejectsWhatIsNotAPositiveNumber(string text)
        {
            double value;
            Assert.False(Sensitivity.TryParse(text, out value));
        }

        [Theory]
        [InlineData(0.31428, "0.314")]
        [InlineData(3.33333, "3.333")]
        [InlineData(2.0, "2")]
        [InlineData(12.345, "12.35")]
        public void Format_UsesADotLikeTheGames(double value, string expected)
        {
            Assert.Equal(expected, Sensitivity.Format(value));
        }

        [Fact]
        public void Convert_RefusesZeroOrNegativeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Sensitivity.Convert(0, 0.022, 800, 0.07, 800));
            Assert.Throws<ArgumentOutOfRangeException>(() => Sensitivity.Cm360(1, 0.022, 0));
        }

        [Fact]
        public void Games_HavePositiveYawsAndNames()
        {
            Assert.NotEmpty(Sensitivity.Games);
            foreach (GameYaw game in Sensitivity.Games)
            {
                Assert.False(string.IsNullOrEmpty(game.Name));
                Assert.True(game.Yaw > 0);
            }
        }
    }
}
