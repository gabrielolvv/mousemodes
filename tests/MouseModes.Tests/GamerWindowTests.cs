using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Janela mais alta que a tela (escala do Windows alta): sem mostrar nada, só a disposição dos controles.</summary>
    public class GamerWindowTests
    {
        private sealed class TallWindow : GamerWindow
        {
            public readonly Label Last;

            public TallWindow(int height) : base("Teste", true)
            {
                ClientSize = new Size(400, height);
                Last = Note("fim", 20, height - 40, 360, 20, Theme.Muted);
                Controls.Add(Last);
                FinishLayout();
            }

            public TitleBar Bar { get { return TitleBar; } }
        }

        [Fact]
        public void FitToWorkingArea_ScrollsTheContentAndKeepsTheTitleBar()
        {
            using (var window = new TallWindow(900))
            {
                int bar = window.Bar.Height;
                int width = window.Width;
                int bottomTop = window.Last.Top;

                Assert.True(window.FitToWorkingArea(new Rectangle(0, 0, 1920, 600)));

                Assert.Equal(580, window.Height);
                Assert.Equal(10, window.Top);
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
