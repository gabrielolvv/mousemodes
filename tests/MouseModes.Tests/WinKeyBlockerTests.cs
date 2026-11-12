using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Só a decisão de engolir ou não a tecla Windows; nenhum hook de teclado é instalado.</summary>
    public class WinKeyBlockerTests
    {
        private static bool Down(ref WinKeyBlocker.WinKeyPress state, bool isGame)
        {
            return WinKeyBlocker.Decide(ref state, false, isGame);
        }

        private static bool Up(ref WinKeyBlocker.WinKeyPress state, bool isGame)
        {
            return WinKeyBlocker.Decide(ref state, true, isGame);
        }

        [Fact]
        public void InTheGameBothDownAndUpAreSwallowed()
        {
            var state = WinKeyBlocker.WinKeyPress.Up;
            Assert.True(Down(ref state, true));
            Assert.True(Down(ref state, true)); // repetição da tecla segurada
            Assert.True(Up(ref state, true));
            Assert.Equal(WinKeyBlocker.WinKeyPress.Up, state);
        }

        [Fact]
        public void PressedOnTheDesktopThenGameInFront_UpStillPasses()
        {
            var state = WinKeyBlocker.WinKeyPress.Up;
            Assert.False(Down(ref state, false)); // na área de trabalho: o Windows viu a descida
            Assert.False(Down(ref state, true));  // o jogo veio para a frente com a tecla segurada
            Assert.False(Up(ref state, true));    // a subida passa: sem ela o Windows acharia a tecla presa
        }

        [Fact]
        public void SwallowedInTheGameThenDesktopInFront_UpIsSwallowedToo()
        {
            var state = WinKeyBlocker.WinKeyPress.Up;
            Assert.True(Down(ref state, true));
            Assert.True(Up(ref state, false)); // o Windows não viu a descida: também não vê a subida
        }

        [Fact]
        public void AnOrphanUpIsNeverSwallowed()
        {
            var state = WinKeyBlocker.WinKeyPress.Up;
            Assert.False(Up(ref state, true));
        }

        [Fact]
        public void EachPressDecidesAgain()
        {
            var state = WinKeyBlocker.WinKeyPress.Up;
            Down(ref state, true);
            Up(ref state, true);
            Assert.False(Down(ref state, false));
            Assert.False(Up(ref state, false));
        }
    }
}
