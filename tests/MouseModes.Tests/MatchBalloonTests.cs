using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Aviso do fim da partida: dentro do limite do Windows e com a chamada para clicar sempre inteira.</summary>
    public class MatchBalloonTests
    {
        private const string Action = "Clique para ver e resolver.";

        [Fact]
        public void MatchBalloonText_KeepsShortLinesInOrder()
        {
            string text = TrayApp.MatchBalloonText(new List<string> { "Culpado.", "Quadros.", "Rede." }, Action);

            Assert.Equal("Culpado.\nQuadros.\nRede.\n" + Action, text);
        }

        [Fact]
        public void MatchBalloonText_KeepsAtMostThreeLines()
        {
            string text = TrayApp.MatchBalloonText(new List<string> { "Culpado.", "", "Quadros.", "Rede.", "Processador." }, Action);

            Assert.Equal("Culpado.\nQuadros.\nRede.\n" + Action, text);
        }

        [Fact]
        public void MatchBalloonText_DropsTheLeastImportantLinesToFit()
        {
            string culprit = "OneDrive foi o que mais pesou em segundo plano (34% do processador em média, 120 MB/s de disco, " +
                             "8 Mbit/s de rede, 2 GB de memória).";
            string frames = "180 FPS em média, 1% mínimo 92 FPS, 14 travadinhas acima de 50 ms em 41 min de partida.";
            string network = "Rede: 6 pico(s) de ping, vindos do Wi-Fi.";
            Assert.True(culprit.Length + frames.Length + network.Length + Action.Length + 3 > TrayApp.BalloonTextMax);

            string text = TrayApp.MatchBalloonText(new List<string> { culprit, frames, network }, Action);

            Assert.True(text.Length <= TrayApp.BalloonTextMax);
            Assert.Equal(culprit + "\n" + frames + "\n" + Action, text);
        }

        [Fact]
        public void MatchBalloonText_CutsALoneLongLineButNeverTheAction()
        {
            string text = TrayApp.MatchBalloonText(new List<string> { new string('x', 400), "Rede." }, "Click to see and fix it.");

            Assert.Equal(TrayApp.BalloonTextMax, text.Length);
            Assert.EndsWith("…\nClick to see and fix it.", text);
            Assert.StartsWith("xxxx", text);
        }

        [Fact]
        public void MatchBalloonText_OnlyTheActionWhenThereIsNoLine()
        {
            Assert.Equal(Action, TrayApp.MatchBalloonText(new List<string>(), Action));
        }

        [Fact]
        public void FitBalloonTitle_StaysWithinTheWindowsLimit()
        {
            Assert.Equal("Raio-X da partida: cs2", TrayApp.FitBalloonTitle("Raio-X da partida: cs2"));

            string title = TrayApp.FitBalloonTitle("Raio-X da partida: " + new string('j', 80));

            Assert.Equal(TrayApp.BalloonTitleMax, title.Length);
            Assert.EndsWith("…", title);
        }
    }
}
