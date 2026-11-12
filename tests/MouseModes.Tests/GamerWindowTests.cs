using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>
    /// Janela mais alta que a tela (escala do Windows alta): sem mostrar nada, só a disposição dos controles.
    /// O WinForms limita a altura de uma janela a SystemInformation.MaxWindowTrackSize (altura da área virtual + 20):
    /// no runner do GitHub (1024x768) nenhuma janela passa de 788 px, então os testes não criam janelas acima de 760.
    /// </summary>
    public class GamerWindowTests
    {
        private sealed class TallWindow : GamerWindow
        {
            public readonly Label Last;

            /// <param name="bottomMargin">Espaço vazio abaixo do último controle (as janelas do app têm 12 a 18 px).</param>
            public TallWindow(int height, int bottomMargin = 20) : base("Teste", true)
            {
                ClientSize = new Size(400, height);
                Last = Note("fim", 20, height - bottomMargin - 20, 360, 20, Theme.Muted);
                Controls.Add(Last);
                FinishLayout();
            }

            public TitleBar Bar { get { return TitleBar; } }
        }

        [Fact]
        public void FitToWorkingArea_ScrollsTheContentAndKeepsTheTitleBar()
        {
            using (var window = new TallWindow(760))
            {
                int bar = window.Bar.Height;
                int width = window.Width;
                int bottomTop = window.Last.Top;

                Assert.True(window.FitToWorkingArea(new Rectangle(0, 0, 1920, 600)));

                Assert.Equal(600, window.Height); // a área útil inteira, sem sobra
                Assert.Equal(0, window.Top);
                Assert.Same(window, window.Bar.Parent); // a barra de título não rola
                Assert.NotNull(window.Scroller);
                Assert.True(window.Scroller.AutoScroll);
                Assert.Same(window.Scroller, window.Last.Parent);
                Assert.Equal(bottomTop - bar, window.Last.Top);
                Assert.Equal(width + SystemInformation.VerticalScrollBarWidth, window.Width);
                Assert.True(window.Scroller.Bottom <= window.ClientSize.Height);

                Assert.False(window.FitToWorkingArea(new Rectangle(0, 0, 1920, 600))); // uma vez só
            }
        }

        [Fact]
        public void FitToWorkingArea_OnlyCutsTheEmptyBottomMarginWhenTheControlsFit()
        {
            // Windows 11 em 1366x768 a 100%: área útil de 720 px e a janela principal com 724 px.
            using (var window = new TallWindow(724, 14))
            {
                int width = window.Width;
                int bottom = window.Last.Bottom;

                Assert.False(window.FitToWorkingArea(new Rectangle(0, 0, 1366, 720)));

                Assert.Null(window.Scroller);
                Assert.Same(window, window.Last.Parent);
                Assert.Equal(bottom, window.Last.Bottom);
                Assert.Equal(720, window.Height);
                Assert.Equal(0, window.Top);
                Assert.Equal(width, window.Width);
                Assert.True(window.Last.Bottom <= window.ClientSize.Height - 1); // acima da borda
            }
        }

        [Fact]
        public void FitToWorkingArea_UsesTheTopOfTheWorkingArea()
        {
            // Barra de tarefas em cima (como 1080p a 150%, em tamanho que cabe em qualquer tela de 768 px):
            // janela de 724 px numa área de 712 que começa em 72; o último controle termina em 706.
            using (var window = new TallWindow(724, 18))
            {
                Assert.False(window.FitToWorkingArea(new Rectangle(0, 72, 1280, 712)));

                Assert.Null(window.Scroller);
                Assert.Equal(712, window.Height);
                Assert.Equal(72, window.Top);
            }
        }

        [Fact]
        public void FitToWorkingArea_ScrollsWhenTheLastControlDoesNotFit()
        {
            // Só 2 px de margem: o último controle passaria da área útil.
            using (var window = new TallWindow(724, 2))
            {
                Assert.True(window.FitToWorkingArea(new Rectangle(0, 30, 1366, 720)));

                Assert.NotNull(window.Scroller);
                Assert.Equal(720, window.Height);
                Assert.Equal(30, window.Top);
                Assert.Same(window.Scroller, window.Last.Parent);
            }
        }

        [Fact]
        public void FitToWorkingArea_LeavesAWindowThatFitsAlone()
        {
            using (var window = new TallWindow(500))
            {
                Size size = window.Size;

                Assert.False(window.FitToWorkingArea(new Rectangle(0, 0, 1366, 728)));

                Assert.Null(window.Scroller);
                Assert.Same(window, window.Last.Parent);
                Assert.Equal(size, window.Size);
            }
        }
    }
}
