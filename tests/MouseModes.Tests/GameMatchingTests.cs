using System.Collections.Generic;
using System.Diagnostics;
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
        public void IsListedGame_LeavesProcessesFromTheWindowsFolderOut()
        {
            // Só lê: nenhum processo muda. O próprio processo dos testes fica fora da pasta do Windows; o cmd, dentro.
            // (Um cmd, não um ping: os testes do segundo plano calmo procuram pings novos.)
            using (Process me = Process.GetCurrentProcess())
            {
                Assert.True(GameDetector.IsListedGame(me, me.ProcessName, new List<string> { me.ProcessName }));
                Assert.False(GameDetector.IsListedGame(me, me.ProcessName, new List<string> { "NaoEsteJogo" }));
            }
            var info = new ProcessStartInfo("cmd", "/c waitfor /t 10 MouseModesJogoNuncaChega >nul 2>&1") { UseShellExecute = false, CreateNoWindow = true };
            using (Process cmd = Process.Start(info))
            {
                try
                {
                    Assert.False(GameDetector.IsListedGame(cmd, "cmd", new List<string> { "cmd" }));
                }
                finally
                {
                    try { if (!cmd.HasExited) cmd.Kill(); } catch { }
                }
            }
        }

        [Fact]
        public void Biggest_PrefersTheGameOverItsLauncher()
        {
            var running = new List<RunningGame>
            {
                new RunningGame("FiveM", 10, 300L << 20),                   // o launcher, aberto primeiro
                new RunningGame("FiveM_ChromeBrowser", 11, 120L << 20),
                new RunningGame("FiveM_b3095_GTAProcess", 12, 3000L << 20),  // o jogo
                new RunningGame("cs2", 13, 2000L << 20)
            };

            Assert.Equal("FiveM_b3095_GTAProcess", GameDetector.Biggest("FiveM.exe", running));
            Assert.Equal("cs2", GameDetector.Biggest("cs2", running));
            Assert.Null(GameDetector.Biggest("valorant", running));
            Assert.Null(GameDetector.Biggest("", running));
            // Empate: fica o primeiro visto.
            Assert.Equal("JogoA", GameDetector.Biggest("Jogo", new List<RunningGame> { new RunningGame("JogoA", 1, 0), new RunningGame("JogoB", 2, 0) }));
        }

        [Fact]
        public void GameExeWatch_PicksTheGameOnceItGrowsPastTheLauncher()
        {
            var watch = new GameExeWatch();
            var launcher = new RunningGame("FiveM", 10, 300L << 20);

            Assert.Same(launcher, watch.Pick("FiveM", new List<RunningGame> { launcher }, null));
            // O jogo abre pequeno (carregando): o launcher continua sendo o maior.
            Assert.Null(watch.Pick("FiveM", new List<RunningGame> { launcher, new RunningGame("FiveM_b3095_GTAProcess", 12, 40L << 20) }, "FiveM"));
            // Cresceu: agora ele passa o launcher, mesmo sem processo novo.
            RunningGame grown = new RunningGame("FiveM_b3095_GTAProcess", 12, 3000L << 20);
            Assert.Same(grown, watch.Pick("FiveM", new List<RunningGame> { launcher, grown }, "FiveM"));
        }

        [Fact]
        public void GameExeWatch_KeepsAnExeFromAnotherRunUntilItShowsUp()
        {
            var watch = new GameExeWatch();
            var launcher = new RunningGame("FiveM", 10, 300L << 20);

            // O launcher abre sozinho: o jogo lembrado de outra execução fica.
            Assert.Null(watch.Pick("FiveM", new List<RunningGame> { launcher }, "FiveM_b3095_GTAProcess"));
            // Lembrado errado de outra execução (o launcher) e o jogo aparece maior: troca.
            var game = new RunningGame("FiveM_b3095_GTAProcess", 12, 3000L << 20);
            Assert.Same(game, watch.Pick("FiveM", new List<RunningGame> { launcher, game }, "FiveM"));
        }

        [Fact]
        public void RememberGameExe_WithWatch_KeepsTheGameWhenItClosesBeforeTheLauncher()
        {
            var cfg = new AppConfig();
            cfg.Games.Clear();
            cfg.Games.Add("FiveM.exe");
            var watch = new GameExeWatch();
            var launcher = new RunningGame("FiveM", 10, 300L << 20);
            var game = new RunningGame("FiveM_b3095_GTAProcess", 12, 3000L << 20);

            Assert.True(cfg.RememberGameExe(new List<RunningGame> { launcher }, watch));
            Assert.Equal("FiveM", cfg.SeenGameExe("FiveM"));
            Assert.True(cfg.RememberGameExe(new List<RunningGame> { launcher, game }, watch));
            Assert.Equal("FiveM_b3095_GTAProcess", cfg.SeenGameExe("FiveM"));

            // O jogo fechou antes do launcher: nenhum processo novo, fica o jogo.
            Assert.False(cfg.RememberGameExe(new List<RunningGame> { launcher }, watch));
            Assert.Equal("FiveM_b3095_GTAProcess", cfg.SeenGameExe("FiveM"));

            // Abriu um processo novo, mas o maior aberto (o launcher) é menor que o jogo já visto: fica o jogo.
            var helper = new RunningGame("FiveM_ChromeBrowser", 30, 120L << 20);
            Assert.False(cfg.RememberGameExe(new List<RunningGame> { launcher, helper }, watch));
            Assert.Equal("FiveM_b3095_GTAProcess", cfg.SeenGameExe("FiveM"));

            // Tudo fechou e o launcher abre sozinho de novo: o jogo lembrado fica até aparecer outro maior que ele.
            watch.Clear();
            Assert.False(cfg.RememberGameExe(new List<RunningGame> { launcher }, watch));
            Assert.Equal("FiveM_b3095_GTAProcess", cfg.SeenGameExe("FiveM"));
        }

        [Fact]
        public void GameExeWatch_ForgetsAnEntryWithNothingOpen()
        {
            var watch = new GameExeWatch();
            var launcher = new RunningGame("FiveM", 10, 300L << 20);
            var game = new RunningGame("FiveM_b3095_GTAProcess", 12, 3000L << 20);
            var other = new RunningGame("cs2", 40, 2000L << 20);

            Assert.Same(game, watch.Pick("FiveM", new List<RunningGame> { launcher, game }, null));
            // Só o cs2 aberto: o FiveM sai da memória (sem chamar Clear).
            Assert.Null(watch.Pick("FiveM", new List<RunningGame> { other }, "FiveM_b3095_GTAProcess"));
            // O launcher reabre sozinho: o jogo lembrado ainda não apareceu nesta vez, então fica.
            Assert.Null(watch.Pick("FiveM", new List<RunningGame> { launcher }, "FiveM_b3095_GTAProcess"));
            // Sem nada lembrado, vale o maior que abrir.
            Assert.Same(launcher, new GameExeWatch().Pick("FiveM", new List<RunningGame> { launcher }, null));
        }

        [Fact]
        public void RememberGameExe_KeepsTheBiggestProcessOfEachEntry()
        {
            var cfg = new AppConfig();
            cfg.Games.Clear();
            cfg.Games.Add("FiveM.exe");
            cfg.Games.Add("cs2");

            // Só o launcher aberto: é o que há.
            Assert.True(cfg.RememberGameExe(new List<RunningGame> { new RunningGame("FiveM", 10, 300L << 20) }));
            Assert.Equal("FiveM", cfg.SeenGameExe("FiveM"));

            // O jogo abriu e cresceu: troca para ele, mesmo o launcher vindo primeiro na lista do Windows.
            var both = new List<RunningGame> { new RunningGame("FiveM", 10, 300L << 20), new RunningGame("FiveM_b3095_GTAProcess", 12, 3000L << 20) };
            Assert.True(cfg.RememberGameExe(both));
            Assert.Equal("FiveM_b3095_GTAProcess", cfg.SeenGameExe("FiveM"));
            Assert.Null(cfg.SeenGameExe("cs2"));
            Assert.False(cfg.RememberGameExe(both)); // nada mudou: nada a gravar
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
