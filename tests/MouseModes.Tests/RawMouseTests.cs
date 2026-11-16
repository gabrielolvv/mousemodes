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

        [Fact]
        public void ReadingsFromTheMouseThreadArriveOnTheScreenThreadInOrderWithTheirTime()
        {
            using (var form = new Form())
            using (var raw = new RawMouse())
            {
                CreateHandle(form);
                var got = new System.Collections.Generic.List<long>();
                int screenThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                bool wrongThread = false;
                string error;
                // O registro é de verdade: se alguém mexer no mouse durante o teste, essas leituras também chegam. Só
                // contam as que o teste manda, marcadas com um deslocamento que mouse nenhum produz.
                const int marker = 123457;
                Assert.True(raw.Register(form, true, (dx, dy, f, t) =>
                {
                    if (dx != marker) return;
                    if (System.Threading.Thread.CurrentThread.ManagedThreadId != screenThread) wrongThread = true;
                    got.Add(t);
                }, out error), error);

                // Como a thread do mouse faz: entrega de outra thread, com a hora de cada leitura.
                var sender = new System.Threading.Thread(() =>
                {
                    for (long t = 1; t <= 500; t++) RawMouse.Post(marker, 0, 0, t);
                });
                sender.Start();
                sender.Join();

                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (got.Count < 500 && watch.Elapsed.TotalSeconds < 5) Application.DoEvents();

                Assert.False(wrongThread);
                Assert.Equal(500, got.Count);
                for (int i = 0; i < got.Count; i++) Assert.Equal(i + 1, got[i]); // a hora de cada uma, na ordem
            }
        }

        [Fact]
        public void TheMouseThreadStopsWithTheLastScreenAndStartsAgain()
        {
            using (var form = new Form())
            {
                string error;
                using (var raw = new RawMouse())
                {
                    Assert.True(raw.Register(form, true, (dx, dy, f) => { }, out error), error);
                    Assert.True(RawMouse.Listening);
                }
                Assert.False(RawMouse.Listening);
                using (var raw = new RawMouse())
                {
                    long seen = 0;
                    Assert.True(raw.Register(form, true, (dx, dy, f, t) => seen = t, out error), error);
                    Assert.True(RawMouse.Listening);
                    RawMouse.Dispatch(0, 0, RawMouse.LeftDown);
                    Assert.True(seen > 0); // a entrega direta também traz a hora
                }
                Assert.False(RawMouse.Listening);
            }
        }

        private static void CreateHandle(Form form)
        {
            Assert.NotEqual(System.IntPtr.Zero, form.Handle); // cria o handle sem mostrar a tela
        }
    }
}
