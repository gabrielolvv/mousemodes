using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>
    /// Keycaps do atalho (posições, sem desenhar) e o campo que grava o atalho nos Ajustes, sem abrir janela: as teclas
    /// entram direto no HandleKey e nada é registrado no Windows.
    /// </summary>
    public class HotkeyBoxTests
    {
        private static readonly uint Ctrl = HotkeyWindow.ModsFrom(Keys.Control);
        private static readonly uint Alt = HotkeyWindow.ModsFrom(Keys.Alt);
        private static readonly uint Shift = HotkeyWindow.ModsFrom(Keys.Shift);

        // Largura de texto inventada: 7 px por letra.
        private static float Width7(string s)
        {
            return s.Length * 7f;
        }

        [Fact]
        public void Layout_PlacesEachKeyWithAPlusBetween()
        {
            KeycapLayout layout = Keycaps.Layout(new[] { "CTRL", "ALT", "M" }, Width7, 0, 0, 22, 1);

            Assert.Equal(3, layout.Caps.Length);
            Assert.Equal(2, layout.Plus.Length);
            Assert.Equal(new RectangleF(0.5f, 0.5f, 42, 22), layout.Caps[0]);   // 28 de texto + 14 de folga
            Assert.Equal(new RectangleF(46.5f, 0, 10, 22), layout.Plus[0]);     // 4 depois da tecla
            Assert.Equal(new RectangleF(61, 0.5f, 35, 22), layout.Caps[1]);    // o "+" avança 14 (e cada tecla, meio pixel)
            Assert.Equal(new RectangleF(114.5f, 0.5f, 26, 22), layout.Caps[2]); // "M" fica na largura mínima
            Assert.Equal(144.5f, layout.End);
        }

        [Fact]
        public void Layout_ScalesWithDpiAndStartsWhereAsked()
        {
            KeycapLayout layout = Keycaps.Layout(new[] { "M" }, Width7, 10, 5, 44, 2);

            Assert.Empty(layout.Plus);
            Assert.Equal(new RectangleF(10.5f, 5.5f, 52, 44), layout.Caps[0]); // mínimo de 26 px vezes 2
            Assert.Equal(10.5f + 52 + 8, layout.End);
        }

        [Fact]
        public void Layout_TrailingPlusLeavesRoomForTheMissingKey()
        {
            KeycapLayout layout = Keycaps.Layout(new[] { "CTRL", "ALT" }, Width7, 0, 0, 22, 1, trailingPlus: true);

            Assert.Equal(2, layout.Plus.Length);
            Assert.Equal(new RectangleF(61 + 35 + 4, 0, 10, 22), layout.Plus[1]);
            Assert.Equal(61 + 35 + 4 + 14f, layout.End);
        }

        [Fact]
        public void Layout_WithoutKeysIsEmpty()
        {
            KeycapLayout layout = Keycaps.Layout(null, Width7, 3, 0, 22, 1, trailingPlus: true);

            Assert.Empty(layout.Caps);
            Assert.Empty(layout.Plus);
            Assert.Equal(3f, layout.End);
        }

        [Fact]
        public void ModifierCaps_AreTheCapsWithoutTheFinalKey()
        {
            Assert.Equal(new[] { "CTRL", "ALT", "SHIFT" }, HotkeyWindow.ModifierCaps(Shift | Alt | Ctrl));
            Assert.Empty(HotkeyWindow.ModifierCaps(0));
            Assert.Equal(new[] { "CTRL", "SHIFT", "F12" }, HotkeyWindow.Caps(Ctrl | Shift, Keys.F12));
        }

        [Fact]
        public void HotkeyBox_ShowsTheSavedCombinationAsKeycaps()
        {
            using (var box = new HotkeyBox())
            {
                box.SetValue(Ctrl | Alt, Keys.K);

                Assert.Equal(HotkeyBoxState.Combination, box.State);
                Assert.Equal(new[] { "CTRL", "ALT", "K" }, box.ShownCaps);
                Assert.Equal("CTRL + ALT + K", box.Text);
                Assert.True(box.TabStop);
            }
        }

        [Fact]
        public void HotkeyBox_ScreenReaderGetsTheCombinationText()
        {
            using (var box = new HotkeyBox())
            {
                box.SetValue(Ctrl | Shift, Keys.F9);

                Assert.Equal(AccessibleRole.HotkeyField, box.AccessibilityObject.Role);
                Assert.Equal("CTRL + SHIFT + F9", box.AccessibilityObject.Value);
                Assert.Equal("CTRL + SHIFT + F9", box.AccessibilityObject.Name);
            }
        }

        [Fact]
        public void HotkeyBox_WithoutAHotkeyShowsTheEmptyText()
        {
            using (var box = new HotkeyBox())
            {
                box.SetValue(0, Keys.None);

                Assert.Equal(HotkeyBoxState.Empty, box.State);
                Assert.Null(box.ShownCaps);
                Assert.Equal(L.T("Nenhum atalho", "No hotkey"), box.Text);
            }
        }

        [Fact]
        public void HotkeyBox_CapturesANewCombination()
        {
            using (var box = new HotkeyBox())
            {
                box.SetValue(Ctrl | Alt, Keys.M);

                Assert.True(box.HandleKey(Keys.ControlKey | Keys.Control));
                Assert.Equal(HotkeyBoxState.Pending, box.State);
                Assert.Equal(new[] { "CTRL" }, box.ShownCaps);
                Assert.Equal("CTRL + …", box.Text);

                Assert.True(box.HandleKey(Keys.ShiftKey | Keys.Control | Keys.Shift));
                Assert.Equal(new[] { "CTRL", "SHIFT" }, box.ShownCaps);
                Assert.Equal("CTRL + SHIFT + …", box.Text);
                Assert.Equal(Keys.M, box.Key); // ainda não trocou

                Assert.True(box.HandleKey(Keys.F12 | Keys.Control | Keys.Shift));
                Assert.Equal(HotkeyBoxState.Combination, box.State);
                Assert.Equal(Ctrl | Shift, box.Mods);
                Assert.Equal(Keys.F12, box.Key);
                Assert.Equal("CTRL + SHIFT + F12", box.Text);
                Assert.Equal("Ctrl+Shift+F12", HotkeyWindow.Format(box.Mods, box.Key)); // o formato salvo não muda
            }
        }

        [Theory]
        [InlineData(Keys.Back)]
        [InlineData(Keys.Delete)]
        [InlineData(Keys.K)]
        [InlineData(Keys.Tab | Keys.Shift)]
        public void HotkeyBox_ExplainsWhyAKeyCannotBeTheHotkey(Keys keyData)
        {
            using (var box = new HotkeyBox())
            {
                box.SetValue(Ctrl | Alt, Keys.M);

                Assert.True(box.HandleKey(keyData));

                Assert.Equal(HotkeyBoxState.Problem, box.State);
                Assert.Null(box.ShownCaps);
                Assert.Equal(HotkeyWindow.Validate(HotkeyWindow.ModsFrom(keyData & Keys.Modifiers), keyData & Keys.KeyCode), box.Text);
                Assert.Equal(Ctrl | Alt, box.Mods); // o atalho gravado continua o mesmo
                Assert.Equal(Keys.M, box.Key);
            }
        }

        [Fact]
        public void HotkeyBox_EscapeCancelsAndTabMovesOn()
        {
            using (var box = new HotkeyBox())
            {
                box.SetValue(Ctrl | Alt, Keys.M);
                box.HandleKey(Keys.ControlKey | Keys.Control);

                Assert.True(box.HandleKey(Keys.Escape));
                Assert.Equal(HotkeyBoxState.Combination, box.State);
                Assert.Equal("CTRL + ALT + M", box.Text);

                Assert.False(box.HandleKey(Keys.Tab));
                Assert.Equal(HotkeyBoxState.Combination, box.State);
            }
        }

        [Fact]
        public void HotkeyBox_AModifierWithoutItsFlagKeepsWaiting()
        {
            using (var box = new HotkeyBox())
            {
                box.SetValue(Ctrl | Alt, Keys.M);

                Assert.True(box.HandleKey(Keys.Menu));

                Assert.Equal(HotkeyBoxState.Waiting, box.State);
                Assert.Null(box.ShownCaps);
            }
        }
    }
}
