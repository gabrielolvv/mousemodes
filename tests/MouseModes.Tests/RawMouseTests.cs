using System.Windows.Forms;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>
    /// Várias telas ouvindo o mouse ao mesmo tempo (medidor de polling, teste de clique, DPI real). As leituras são
    /// entregues à mão: nada aqui mexe no mouse de verdade.
    /// </summary>
    public class RawMouseTests
    {
        [Fact]
        public void EveryOpenScreenGetsTheReadingAndClosingOneKeepsTheOthers()
        {
            using (var formA = new Form())
            using (var formB = new Form())
            using (var rawA = new RawMouse())
            using (var rawB = new RawMouse())
            {
                int a = 0, b = 0;
                string error;
                Assert.True(rawA.Register(formA, true, (dx, dy, f) => a += dx, out error), error);
                Assert.True(rawB.Register(formB, true, (dx, dy, f) => b += dx, out error), error);
                Assert.Equal(2, RawMouse.ListenerCount);

                RawMouse.Dispatch(5, 0, 0);
                Assert.Equal(5, a);
                Assert.Equal(5, b);

                // Fechar uma tela não desliga a outra (antes, o RIDEV_REMOVE de uma tirava o mouse de todas).
                rawA.Dispose();
                Assert.True(RawMouse.Listening);
                RawMouse.Dispatch(3, 0, 0);
                Assert.Equal(5, a);
                Assert.Equal(8, b);

                rawB.Dispose();
                Assert.Equal(0, RawMouse.ListenerCount);
                Assert.False(RawMouse.Listening);
            }
        }

        [Fact]
        public void AScreenWithoutSinkOnlyGetsInputWhileInFront()
        {
            using (var form = new Form())
            using (var raw = new RawMouse())
            {
                CreateHandle(form);
                int clicks = 0;
                string error;
                Assert.True(raw.Register(form, false, (dx, dy, f) => clicks++, out error), error);

                // A tela do teste nunca está em primeiro plano: o clique não conta.
                RawMouse.Dispatch(0, 0, RawMouse.LeftDown);
                Assert.Equal(0, clicks);
            }
            Assert.False(RawMouse.Listening);
        }

        private static void CreateHandle(Form form)
        {
            Assert.NotEqual(System.IntPtr.Zero, form.Handle); // cria o handle sem mostrar a tela
        }
    }
}
