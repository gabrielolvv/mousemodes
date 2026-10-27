using System.Windows.Forms;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Leitura, gravação e regras do atalho. Nada aqui registra atalho no Windows.</summary>
    public class HotkeyTests
    {
        private static readonly uint Ctrl = HotkeyWindow.ModsFrom(Keys.Control);
        private static readonly uint Alt = HotkeyWindow.ModsFrom(Keys.Alt);
        private static readonly uint Shift = HotkeyWindow.ModsFrom(Keys.Shift);

        [Theory]
        [InlineData("Ctrl+Alt+K", "CTRL + ALT + K")]
        [InlineData("ctrl + shift + f9", "CTRL + SHIFT + F9")]
        [InlineData("Alt+D5", "ALT + 5")]
        [InlineData("Ctrl+NumPad1", "CTRL + NUM 1")]
        [InlineData("Ctrl+Alt+Shift+Space", "CTRL + ALT + SHIFT + ESPAÇO")]
        public void TryParse_ReadsTheConfigFormat(string text, string shown)
        {
            uint mods;
            Keys key;
            Assert.True(HotkeyWindow.TryParse(text, out mods, out key));
            Assert.Equal(shown, HotkeyWindow.Describe(mods, key));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("K")]                 // sem Ctrl nem Alt
        [InlineData("Shift+K")]           // Shift sozinho atrapalharia a digitação
        [InlineData("Ctrl+Alt")]          // falta a tecla
        [InlineData("Ctrl+K+M")]          // duas teclas
        [InlineData("Ctrl+65")]           // número solto viraria uma tecla qualquer
        [InlineData("Ctrl+K, M")]         // o Enum.TryParse juntaria as duas
        [InlineData("Ctrl+Teclado")]      // não existe
        [InlineData("Alt+Tab")]           // do Windows
        [InlineData("Ctrl+LWin")]
        public void TryParse_RejectsWhatCannotBeAHotkey(string text)
        {
            uint mods;
            Keys key;
            Assert.False(HotkeyWindow.TryParse(text, out mods, out key));
        }

        [Fact]
        public void FormatAndTryParse_RoundTrip()
        {
            string text = HotkeyWindow.Format(Ctrl | Shift, Keys.F9);
            Assert.Equal("Ctrl+Shift+F9", text);

            uint mods;
            Keys key;
            Assert.True(HotkeyWindow.TryParse(text, out mods, out key));
            Assert.Equal(Ctrl | Shift, mods);
            Assert.Equal(Keys.F9, key);
        }

        [Fact]
        public void Validate_ExplainsWhatIsMissing()
        {
            Assert.Null(HotkeyWindow.Validate(Ctrl | Alt, Keys.K));
            Assert.NotNull(HotkeyWindow.Validate(Shift, Keys.K));
            Assert.NotNull(HotkeyWindow.Validate(Ctrl, Keys.ControlKey));
            Assert.NotNull(HotkeyWindow.Validate(Alt, Keys.Escape));
        }

        [Fact]
        public void TheFixedChoicesAreFoundByTheirKeys()
        {
            for (int i = 0; i < HotkeyWindow.Choices.Length; i++)
            {
                uint mods;
                Keys key;
                HotkeyWindow.Choice(i, out mods, out key);
                Assert.Equal(i, HotkeyWindow.ChoiceIndex(mods, key));
                Assert.Equal(HotkeyWindow.Describe(i), HotkeyWindow.Describe(mods, key));
            }
            Assert.Equal(-1, HotkeyWindow.ChoiceIndex(Ctrl | Alt, Keys.K));
        }

        [Fact]
        public void DescribePreference_UsesTheCustomHotkeyOnlyWhenItIsValid()
        {
            Assert.Equal("CTRL + ALT + K", HotkeyWindow.DescribePreference(0, "Ctrl+Alt+K"));
            Assert.Equal("CTRL + SHIFT + M", HotkeyWindow.DescribePreference(1, null));
            Assert.Equal("CTRL + SHIFT + M", HotkeyWindow.DescribePreference(1, "Shift+K"));
            Assert.Equal("CTRL + ALT + M", HotkeyWindow.DescribePreference(99, null));
        }

        [Fact]
        public void CustomHotkeyTravelsWithTheExportedConfig()
        {
            var here = new AppConfig();
            here.CopySettingsFrom(new AppConfig { Hotkey = 1, CustomHotkey = "Ctrl+Alt+K" }.ForExport());
            Assert.Equal("Ctrl+Alt+K", here.CustomHotkey);
            Assert.Equal(1, here.Hotkey);
        }
    }
}
