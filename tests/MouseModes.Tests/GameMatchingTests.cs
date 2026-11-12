using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    public class GameMatchingTests
    {
        [Theory]
        [InlineData("FiveM.exe", "FiveM")]
        [InlineData("  cs2  ", "cs2")]
        [InlineData("GAME.EXE", "GAME")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void Normalize_RemovesExeAndSpaces(string entry, string expected)
        {
            Assert.Equal(expected, GameDetector.Normalize(entry));
        }

        [Theory]
        [InlineData("FiveM_b3095_GTAProcess", true)]   // começo do nome
        [InlineData("fivem", true)]                    // sem diferenciar maiúsculas
        [InlineData("cs2", true)]                      // ".exe" na lista é ignorado
        [InlineData("GTA5", false)]
        [InlineData("MyFiveM", false)]                 // só o começo conta
        public void Matches_ComparesTheStartOfTheProcessName(string process, bool expected)
        {
            var games = new List<string> { "FiveM", "cs2.exe", "", "  " };
            Assert.Equal(expected, GameBooster.Matches(process, games));
        }

        [Theory]
        [InlineData("Game", 1, @"D:\Jogos\MeuRPG\Game.exe", true)]                                 // o Game.exe do RPG Maker
        [InlineData("Game", 1, null, true)]                                                          // sem acesso ao caminho
        [InlineData("GameInputSvc", 0, @"C:\Windows\System32\GameInputSvc.exe", false)]            // serviço (sessão 0)
        [InlineData("GameBarPresenceWriter", 1, @"C:\WINDOWS\System32\GameBarPresenceWriter.exe", false)]
        [InlineData("GameBar", 1, @"C:\Program Files\WindowsApps\Microsoft.XboxGamingOverlay_8wekyb3d8bbwe\GameBar.exe", false)]
        [InlineData("GamingServicesNet", 1, null, false)]
        [InlineData("Windows2", 1, @"C:\Windows2\Windows2.exe", true)]                              // só a pasta do Windows
        public void CanBeGame_LeavesWindowsProcessesOut(string name, int session, string path, bool expected)
        {
            Assert.Equal(expected, GameDetector.CanBeGame(name, session, 1, path, @"C:\Windows"));
        }

        [Fact]
        public void PerformanceFor_UsesTheGameProfileWhenThereIsOne()
        {
            var cfg = new AppConfig { Performance = new MouseProfile { PointerSpeed = 10 } };
            var fivem = new MouseProfile { PointerSpeed = 7 };
            cfg.GameProfiles.Add(new GameProfile { Process = "FiveM.exe", Profile = fivem });

            Assert.Same(fivem, cfg.PerformanceFor("FiveM_b3095_GTAProcess"));
            Assert.Same(cfg.Performance, cfg.PerformanceFor("cs2"));
            Assert.Same(cfg.Performance, cfg.PerformanceFor(null));
        }
    }
}
