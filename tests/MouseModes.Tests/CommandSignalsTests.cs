using System;
using System.Threading;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>
    /// Comandos --performance, --daily e --toggle. Cada teste usa eventos com nome próprio: nada aqui fala com o
    /// MouseModes aberto neste PC nem troca o modo.
    /// </summary>
    public class CommandSignalsTests
    {
        private static string NewPrefix()
        {
            return "Local\\MouseModes_Test_" + Guid.NewGuid().ToString("N");
        }

        private static string[] Args(string line)
        {
            return line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        [Theory]
        [InlineData("--performance", CommandSignals.Performance)]
        [InlineData("--DAILY", CommandSignals.Daily)]
        [InlineData("--tray --toggle", CommandSignals.Toggle)]
        [InlineData("--daily --performance", CommandSignals.Daily)] // o primeiro vale
        public void Find_ReturnsTheFirstModeCommand(string line, string expected)
        {
            Assert.Equal(expected, CommandSignals.Find(Args(line)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("--tray")]
        [InlineData("performance -daily --performancex")]
        public void Find_IgnoresAnythingElse(string line)
        {
            Assert.Null(CommandSignals.Find(Args(line)));
        }

        [Fact]
        public void Send_FailsWhenNoCopyIsListening()
        {
            Assert.False(CommandSignals.Send(CommandSignals.Toggle, NewPrefix()));
        }

        [Fact]
        public void Send_ReachesTheListeningCopy()
        {
            string prefix = NewPrefix();
            using (var commands = new CommandSignals(prefix))
            using (var got = new ManualResetEventSlim())
            {
                string received = null;
                commands.Listen(command =>
                {
                    received = command;
                    got.Set();
                });

                Assert.True(CommandSignals.Send(CommandSignals.Daily, prefix));
                Assert.True(got.Wait(TimeSpan.FromSeconds(5)));
                Assert.Equal(CommandSignals.Daily, received);
            }
        }

        [Fact]
        public void Dispose_StopsListening()
        {
            string prefix = NewPrefix();
            int calls = 0;
            var commands = new CommandSignals(prefix);
            commands.Listen(command => Interlocked.Increment(ref calls));
            commands.Dispose();

            Assert.False(CommandSignals.Send(CommandSignals.Performance, prefix));
            Thread.Sleep(200);
            Assert.Equal(0, calls);
        }
    }
}
