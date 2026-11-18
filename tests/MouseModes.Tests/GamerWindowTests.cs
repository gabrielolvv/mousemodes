using System;
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
                Assert.Same(window.Scroller, window.Last.Parent.Parent); // dentro do painel de conteúdo da rolagem
                Assert.Equal(bottomTop - bar, window.Last.Top);
                // Cresce só pela coluna da barra fina (12 px a 96 dpi), não pela largura da barra nativa do Windows.
                Assert.Equal(width + window.ScaledScrollGutter, window.Width);
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
                Assert.Same(window.Scroller, window.Last.Parent.Parent); // dentro do painel de conteúdo da rolagem
            }
        }

        [Fact]
        public void FitToWorkingArea_DoesNotScrollSidewaysForAControlPastTheRightEdge()
        {
            // Como as páginas dos Ajustes: passam da borda direita de propósito (a barra nativa delas fica escondida lá).
            using (var window = new TallWindow(760))
            {
                var wide = new Panel { Location = new Point(200, 60), Size = new Size(200 + SystemInformation.VerticalScrollBarWidth, 300) };
                window.Controls.Add(wide);

                Assert.True(window.FitToWorkingArea(new Rectangle(0, 0, 1920, 600)));

                // Só o painel de conteúdo entra na rolagem, e ele cabe ao lado da barra vertical: nada cria rolagem
                // para o lado. O controle largo fica recortado nele.
                Assert.Single(window.Scroller.Controls);
                Control content = window.Scroller.Controls[0];
                Assert.Same(content, wide.Parent);
                Assert.True(content.Right <= window.Scroller.Width - SystemInformation.VerticalScrollBarWidth);
                Assert.True(wide.Right > content.Width);
            }
        }

        [Fact]
        public void FitToWorkingArea_HidesTheNativeBarBehindAThinBarInTheMargin()
        {
            using (var window = new TallWindow(760))
            {
                int width = window.ClientSize.Width;

                Assert.True(window.FitToWorkingArea(new Rectangle(0, 0, 1920, 600)));

                ThinScrollBar thin = window.ScrollerBar;
                Panel scroller = window.Scroller;
                Control content = scroller.Controls[0];
                Assert.NotNull(thin);
                Assert.Same(window, thin.Parent); // fora da rolagem: não rola junto
                Assert.Same(scroller, thin.Target);
                Assert.False(thin.TabStop);
                // A barra fina fica logo depois do conteúdo (que manteve a largura de antes) e vai até a borda direita
                // (no Windows 10 ela pinta a coluna da borda).
                Assert.Equal(width - 2, content.Width);
                Assert.Equal(scroller.Left + content.Width, thin.Left);
                Assert.Equal(window.ClientSize.Width, thin.Right);
                Assert.Equal(scroller.Top, thin.Top);
                Assert.Equal(scroller.Height, thin.Height);
                // O painel vai até depois da coluna da barra fina com a barra nativa, e a região dele acaba onde ela começa:
                // a barra nativa não aparece.
                Assert.Equal(content.Width + SystemInformation.VerticalScrollBarWidth, scroller.Width);
                Assert.NotNull(scroller.Region);
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    RectangleF visible = scroller.Region.GetBounds(g);
                    Assert.Equal(new RectangleF(0, 0, thin.Left - scroller.Left, scroller.Height), visible);
                }
            }
        }

        [Fact]
        public void FitToWorkingArea_ThinBarFollowsTheScroll()
        {
            using (var window = new TallWindow(760))
            {
                Assert.True(window.FitToWorkingArea(new Rectangle(0, 0, 1920, 600)));
                // Cria as janelas sem mostrar nada: a barra fina lê a rolagem do próprio Windows.
                ThinScrollBar thin = window.ScrollerBar;
                foreach (Control c in new Control[] { window, window.Scroller, window.Scroller.Controls[0], thin })
                    Assert.NotEqual(IntPtr.Zero, c.Handle);
                window.Scroller.PerformLayout();
                thin.Sync();
                Assert.True(thin.Scrollable);
                Assert.Equal(0, thin.Position);

                // Como o foco do teclado indo para um controle fora da vista: o painel rola e a barra fina acompanha.
                window.Scroller.ScrollControlIntoView(window.Last);
                Application.DoEvents(); // a leitura que ficou na fila (a barra nativa muda depois dos controles)
                Assert.True(-window.Scroller.AutoScrollPosition.Y > 0);
                Assert.Equal(-window.Scroller.AutoScrollPosition.Y, thin.Position);
            }
        }

        [Fact]
        public void FitToWorkingArea_ScrollsToControlsBelowAClippedClientArea()
        {
            // Como o Raio-X com a comparação numa tela de 1080 px: a janela pediu mais altura do que o Windows deixa e
            // os últimos controles ficaram abaixo da área cliente. Eles continuam alcançáveis pela rolagem.
            using (var window = new TallWindow(700))
            {
                int bar = window.Bar.Height;
                var below = new Label { Location = new Point(20, window.ClientSize.Height + 60), Size = new Size(100, 30) };
                window.Controls.Add(below);
                int belowTop = below.Top;

                Assert.True(window.FitToWorkingArea(new Rectangle(0, 0, 1920, 600)));

                Control content = window.Scroller.Controls[0];
                Assert.Same(content, below.Parent);
                Assert.Equal(belowTop - bar, below.Top);
                Assert.True(content.Height >= below.Bottom + 16); // com a margem de baixo
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
