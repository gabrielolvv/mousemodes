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
