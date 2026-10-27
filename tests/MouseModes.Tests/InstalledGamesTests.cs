using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Bibliotecas da Steam e da Epic de mentira, numa pasta temporária (nomes e arquivos inventados).</summary>
    public class InstalledGamesTests
    {
        private static string Vdf(string path)
        {
            return path.Replace(@"\", @"\\");
        }

        [Fact]
        public void Steam_ReadsAllLibrariesAndPicksTheGameExecutable()
        {
            using (var dir = new TempFolder())
            {
                string steam = dir.File("Steam"), lib2 = dir.File("Lib2");
                dir.Text(@"Steam\steamapps\libraryfolders.vdf",
                    "\"libraryfolders\"\n{\n \"0\" { \"path\" \"" + Vdf(steam) + "\" }\n \"1\" { \"path\" \"" + Vdf(lib2) + "\" }\n}");

                // Jogo Unreal: exe pequeno na raiz, o de verdade em Binaries; desinstalador e crash handler maiores.
                dir.Text(@"Steam\steamapps\appmanifest_100.acf",
                    "\"AppState\"\n{\n \"name\" \"Jogo Teste Um\"\n \"installdir\" \"JogoUm\"\n \"UserConfig\" { \"name\" \"outro\" }\n}");
                dir.Exe(@"Steam\steamapps\common\JogoUm\JogoUm.exe", 1);
                dir.Exe(@"Steam\steamapps\common\JogoUm\JogoUm\Binaries\Win64\JogoUm-Win64-Shipping.exe", 50);
                dir.Exe(@"Steam\steamapps\common\JogoUm\unins000.exe", 100);
                dir.Exe(@"Steam\steamapps\common\JogoUm\UnityCrashHandler64.exe", 200);

                // Ferramenta da Steam: não é jogo.
                dir.Text(@"Steam\steamapps\appmanifest_228980.acf",
                    "\"AppState\"\n{\n \"name\" \"Steamworks Common Redistributables\"\n \"installdir\" \"Steamworks Shared\"\n}");
                dir.Exe(@"Steam\steamapps\common\Steamworks Shared\something.exe", 10);

                // Segunda biblioteca: "crash" no nome do jogo não pode descartá-lo; o anti-cheat sim.
                dir.Text(@"Lib2\steamapps\appmanifest_200.acf",
                    "\"AppState\"\n{\n \"name\" \"Crash Bandicoot Teste\"\n \"installdir\" \"CrashGame\"\n}");
                dir.Exe(@"Lib2\steamapps\common\CrashGame\CrashBandicootTeste.exe", 30);
                dir.Exe(@"Lib2\steamapps\common\CrashGame\EasyAntiCheat_Setup.exe", 90);

                List<InstalledGame> games = InstalledGames.FromSteam(steam);

                Assert.Equal(2, games.Count);
                Assert.Contains(games, g => g.Name == "Jogo Teste Um" && g.Process == "JogoUm-Win64-Shipping" && g.Store == "Steam");
                Assert.Contains(games, g => g.Name == "Crash Bandicoot Teste" && g.Process == "CrashBandicootTeste");
            }
        }

        [Fact]
        public void Steam_OldLibraryFormatStillWorks()
        {
            using (var dir = new TempFolder())
            {
                string steam = dir.File("Steam"), lib2 = dir.File("Lib2");
                dir.Text(@"Steam\steamapps\libraryfolders.vdf", "\"LibraryFolders\"\n{\n \"1\" \"" + Vdf(lib2) + "\"\n}");
                dir.Text(@"Lib2\steamapps\appmanifest_300.acf", "\"AppState\"\n{\n \"name\" \"Jogo Antigo\"\n \"installdir\" \"Antigo\"\n}");
                dir.Exe(@"Lib2\steamapps\common\Antigo\Antigo.exe", 5);

                Assert.Contains(InstalledGames.FromSteam(steam), g => g.Process == "Antigo");
            }
        }

        [Fact]
        public void Epic_PrefersTheRealGameOverTheLauncherAndSkipsIncompleteInstalls()
        {
            using (var dir = new TempFolder())
            {
                string game = dir.File(@"Epic Games\JogoEpic");
                dir.Exe(@"Epic Games\JogoEpic\Jogo\Binaries\Win64\JogoLauncher.exe", 40);
                dir.Exe(@"Epic Games\JogoEpic\Jogo\Binaries\Win64\JogoClient-Win64-Shipping.exe", 20);
                dir.Text(@"Manifests\A1.item",
                    "{\n \"DisplayName\": \"Jogo Epic\",\n \"InstallLocation\": \"" + Vdf(game) + "\",\n" +
                    " \"LaunchExecutable\": \"Jogo/Binaries/Win64/JogoLauncher.exe\",\n \"bIsIncompleteInstall\": false\n}");
                dir.Text(@"Manifests\B2.item",
                    "{ \"DisplayName\": \"Pela Metade\", \"InstallLocation\": \"" + Vdf(game) + "\", \"bIsIncompleteInstall\": true }");

                List<InstalledGame> games = InstalledGames.FromEpic(dir.File("Manifests"));

                InstalledGame only = Assert.Single(games);
                Assert.Equal("Jogo Epic", only.Name);
                Assert.Equal("JogoClient-Win64-Shipping", only.Process);
                Assert.Equal("Epic", only.Store);
            }
        }

        [Fact]
        public void MissingFoldersGiveNothingWithoutErrors()
        {
            using (var dir = new TempFolder())
            {
                Assert.Empty(InstalledGames.FromEpic(dir.File("nada")));
                Assert.Empty(InstalledGames.FromSteam(dir.File("nada")));
                Assert.Null(InstalledGames.MainExecutable(dir.File("nada")));
            }
        }

        [Theory]
        [InlineData("unins000", false)]
        [InlineData("UnityCrashHandler64", false)]
        [InlineData("EasyAntiCheat_Setup", false)]
        [InlineData("FortniteLauncher", false)]
        [InlineData("vc_redist.x64", false)]
        [InlineData("CrashBandicootTeste", true)]
        [InlineData("HaloReach", true)]
        [InlineData("cs2", true)]
        public void MainExecutable_SkipsOnlyNonGames(string exe, bool kept)
        {
            using (var dir = new TempFolder())
            {
                dir.Exe(@"Jogo\" + exe + ".exe", 10);
                Assert.Equal(kept ? exe : null, InstalledGames.MainExecutable(dir.File("Jogo")));
            }
        }
    }
}
