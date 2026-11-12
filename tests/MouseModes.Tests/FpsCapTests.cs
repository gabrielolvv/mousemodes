using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Limite de FPS com um driver de mentira: nenhum teste grava no driver da placa de verdade.</summary>
    public class FpsCapTests
    {
        private sealed class FakeLimiter : IFpsLimiter
        {
            public readonly Dictionary<string, int> Driver = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Writes = new List<string>();
            public bool AskAdmin;
            public string Refuse;      // exe que o driver recusa
            public int? Keeps;         // o driver "aceita" mas fica com este valor

            public string Name { get { return "Teste"; } }

            public string Read(string exe, out int fps)
            {
                int value;
                fps = Driver.TryGetValue(exe, out value) ? value : 0;
                return null;
            }

            public string Write(string exe, int fps, out bool needsAdmin)
            {
                Writes.Add(exe + "=" + fps);
                needsAdmin = AskAdmin;
                if (AskAdmin) return "o driver pede administrador";
                if (exe == Refuse) return "recusou";
                Driver[exe] = Keeps ?? fps;
                return null;
            }

            /// <summary>O que a cópia administradora faria.</summary>
            public void WriteAsAdmin(List<FpsCapChange> changes)
            {
                foreach (FpsCapChange c in changes) Driver[c.Exe] = c.Fps;
            }

            /// <summary>A cópia administradora que gravou só parte antes de falhar (ou de passar do tempo).</summary>
            public void WriteAsAdmin(FpsCapChange change)
            {
                Driver[change.Exe] = change.Fps;
            }
        }

        private static AppConfig Config(params object[] gameAndCap)
        {
            var cfg = new AppConfig();
            for (int i = 0; i < gameAndCap.Length; i += 2)
            {
                string game = (string)gameAndCap[i];
                cfg.Games.Add(game);
                var profile = new MouseProfile { FpsCap = (int)gameAndCap[i + 1] };
                cfg.GameProfiles.Add(new GameProfile { Process = game, Profile = profile });
            }
            return cfg;
        }

        [Theory]
        [InlineData(0, 144, 0)]
        [InlineData(FpsCap.Auto, 144, 141)]
        [InlineData(FpsCap.Auto, 60, 57)]
        [InlineData(FpsCap.Auto, 0, 0)]     // taxa desconhecida: não inventa um limite
        [InlineData(240, 144, 240)]
        [InlineData(5000, 144, FpsCap.MaxFps)]
        public void Resolve_TurnsTheChoiceIntoFps(int choice, int hz, int expected)
        {
            Assert.Equal(expected, FpsCap.Resolve(choice, hz));
        }

        [Theory]
        [InlineData("cs2", "cs2.exe")]
        [InlineData("VALORANT-Win64-Shipping", "valorant-win64-shipping.exe")]
        [InlineData("r5apex.exe", "r5apex.exe")]
        public void ExeFor_AddsTheExtension(string game, string exe)
        {
            Assert.Equal(exe, FpsCap.ExeFor(game));
        }

        [Theory]
        [InlineData("cs2.exe", true)]
        [InlineData("Rainbow Six (DX11).exe", true)]
        [InlineData(@"C:\Games\cs2.exe", false)]
        [InlineData("cs2.exe\" --outro", false)]
        [InlineData("cs2", false)]
        [InlineData("", false)]
        public void ValidExe_OnlyAcceptsAPlainFileName(string exe, bool valid)
        {
            Assert.Equal(valid, FpsCap.ValidExe(exe));
        }

        [Fact]
        public void Wanted_OnlyGamesInTheListWithAChosenLimit()
        {
            AppConfig cfg = Config("cs2", FpsCap.Auto, "valorant", 0, "r5apex", 240);
            cfg.GameProfiles.Add(new GameProfile { Process = "saiu-da-lista", Profile = new MouseProfile { FpsCap = 120 } });

            Dictionary<string, int> wanted = FpsCap.Wanted(cfg, 165);

            Assert.Equal(2, wanted.Count);
            Assert.Equal(162, wanted["cs2.exe"]);
            Assert.Equal(240, wanted["r5apex.exe"]);
        }

        [Fact]
        public void Wanted_GoesToTheExeTheDetectorSawForAPrefix()
        {
            AppConfig cfg = Config("JogoPrefixo", 144, "cs2", 240);

            Assert.True(cfg.RememberGameExe("JogoPrefixo-Win64-Shipping"));
            Dictionary<string, int> wanted = FpsCap.Wanted(cfg, 165);

            Assert.Equal(144, wanted["jogoprefixo-win64-shipping.exe"]);
            Assert.False(wanted.ContainsKey("jogoprefixo.exe"));
            Assert.Equal(240, wanted["cs2.exe"]); // ainda não visto: o nome da lista com ".exe"
            Assert.Equal("cs2.exe", FpsCap.TargetExe(cfg, "cs2"));
        }

        [Fact]
        public void RememberGameExe_KeepsOnePerGameAndDropsGamesThatLeftTheList()
        {
            AppConfig cfg = Config("JogoA", 0, "JogoAB", 0);

            Assert.True(cfg.RememberGameExe("JogoABCliente"));    // começo dos dois nomes
            Assert.Equal("JogoABCliente", cfg.SeenGameExe("JogoA"));
            Assert.Equal("JogoABCliente", cfg.SeenGameExe("jogoab.exe"));
            Assert.False(cfg.RememberGameExe("jogoabcliente"));   // o mesmo, só muda maiúscula: nada a gravar
            Assert.False(cfg.RememberGameExe("OutroPrograma"));

            Assert.True(cfg.RememberGameExe("JogoAOutro"));        // só do primeiro: troca o dele
            Assert.Equal("JogoAOutro", cfg.SeenGameExe("JogoA"));
            Assert.Equal("JogoABCliente", cfg.SeenGameExe("JogoAB"));
            Assert.Equal(2, cfg.GameExes.Count);

            cfg.Games.Remove("JogoAB");
            Assert.True(cfg.RememberGameExe("OutroPrograma"));     // saiu da lista: sai daqui também
            Assert.Null(cfg.SeenGameExe("JogoAB"));
            Assert.Single(cfg.GameExes);
        }

        [Fact]
        public void UsesAuto_OnlyForGamesStillInTheList()
        {
            AppConfig cfg = Config("cs2", 240);
            Assert.False(FpsCap.UsesAuto(cfg));

            cfg.GameProfiles.Add(new GameProfile { Process = "saiu-da-lista", Profile = new MouseProfile { FpsCap = FpsCap.Auto } });
            Assert.False(FpsCap.UsesAuto(cfg));

            cfg.GameProfiles[0].Profile.FpsCap = FpsCap.Auto;
            Assert.True(FpsCap.UsesAuto(cfg));
        }

        [Fact]
        public void Plan_SetsWhatChangedAndClearsWhatLeft()
        {
            var wanted = new Dictionary<string, int> { { "cs2.exe", 141 }, { "r5apex.exe", 240 } };
            var applied = new[] { "cs2.exe=141", "r5apex.exe=120", "valorant.exe=144", "estragado", "../x.exe=10" };

            List<FpsCapChange> changes = FpsCap.Plan(wanted, applied);

            Assert.Equal(new[] { "r5apex.exe=240", "valorant.exe=0" }, changes.ConvertAll(c => FpsCap.Format(c.Exe, c.Fps)).ToArray());
            Assert.Empty(FpsCap.Plan(new Dictionary<string, int> { { "cs2.exe", 141 } }, new[] { "cs2.exe=141" }));
        }

        [Fact]
        public void Apply_WritesReadsBackAndRemembers()
        {
            var driver = new FakeLimiter();
            driver.Driver["valorant.exe"] = 144;
            var applied = new List<string> { "valorant.exe=144" };
            var changes = new List<FpsCapChange> { new FpsCapChange { Exe = "cs2.exe", Fps = 141 }, new FpsCapChange { Exe = "valorant.exe", Fps = 0 } };

            List<string> problems = FpsCap.Apply(changes, driver, applied, pending => { throw new InvalidOperationException("não devia pedir administrador"); });

            Assert.Empty(problems);
            Assert.Equal(new[] { "cs2.exe=141" }, applied.ToArray());
            Assert.Equal(141, driver.Driver["cs2.exe"]);
            Assert.Equal(0, driver.Driver["valorant.exe"]);
        }

        [Fact]
        public void Apply_AsksForTheAdministratorOnceForEverythingPending()
        {
            var driver = new FakeLimiter { AskAdmin = true };
            var applied = new List<string>();
            var changes = new List<FpsCapChange> { new FpsCapChange { Exe = "cs2.exe", Fps = 141 }, new FpsCapChange { Exe = "r5apex.exe", Fps = 240 } };
            int asked = 0;

            List<string> problems = FpsCap.Apply(changes, driver, applied, pending =>
            {
                asked++;
                Assert.Equal(2, pending.Count);
                driver.WriteAsAdmin(pending);
                return null;
            });

            Assert.Equal(1, asked);
            Assert.Empty(problems);
            Assert.Equal(new[] { "cs2.exe=141", "r5apex.exe=240" }, applied.ToArray());
        }

        [Fact]
        public void Apply_RemembersOnlyWhatTheDriverConfirms()
        {
            var cancelled = new FakeLimiter { AskAdmin = true };
            var applied = new List<string>();
            List<string> problems = FpsCap.Apply(new List<FpsCapChange> { new FpsCapChange { Exe = "cs2.exe", Fps = 141 } }, cancelled, applied,
                                                 pending => "cancelado no pedido de administrador");
            Assert.Equal(new[] { "cs2.exe: cancelado no pedido de administrador" }, problems.ToArray());
            Assert.Empty(applied);

            var stubborn = new FakeLimiter { Keeps = 60, Refuse = "r5apex.exe" };
            problems = FpsCap.Apply(new List<FpsCapChange> { new FpsCapChange { Exe = "cs2.exe", Fps = 141 }, new FpsCapChange { Exe = "r5apex.exe", Fps = 240 } },
                                    stubborn, applied, pending => null);
            Assert.Equal(2, problems.Count);
            Assert.Contains("60 FPS", problems[0]);
            Assert.Equal("r5apex.exe: recusou", problems[1]);
            Assert.Empty(applied);
        }

        [Fact]
        public void Apply_KeepsWhatTheAdministratorCopyWroteEvenWhenItFailed()
        {
            var driver = new FakeLimiter { AskAdmin = true };
            driver.Driver["valorant.exe"] = 144;
            var applied = new List<string> { "valorant.exe=144" };
            var changes = new List<FpsCapChange>
            {
                new FpsCapChange { Exe = "cs2.exe", Fps = 141 },
                new FpsCapChange { Exe = "r5apex.exe", Fps = 240 },
                new FpsCapChange { Exe = "valorant.exe", Fps = 0 }
            };

            // Gravou cs2.exe e tirou o do valorant.exe, mas não chegou ao r5apex.exe antes de passar do tempo.
            List<string> problems = FpsCap.Apply(changes, driver, applied, pending =>
            {
                driver.WriteAsAdmin(pending[0]);
                driver.WriteAsAdmin(pending[2]);
                return "o driver não respondeu a tempo";
            });

            Assert.Equal(new[] { "r5apex.exe: o driver não respondeu a tempo" }, problems.ToArray());
            Assert.Equal(new[] { "cs2.exe=141" }, applied.ToArray());
        }

        [Fact]
        public void ParseHelperArgs_RejectsAnythingThatIsNotAPlainExeAndFps()
        {
            List<FpsCapChange> ok = FpsCap.ParseHelperArgs(new[] { FpsCap.HelperArg, "cs2.exe=141", "valorant.exe=0" });
            Assert.Equal(new[] { "cs2.exe=141", "valorant.exe=0" }, ok.ConvertAll(c => FpsCap.Format(c.Exe, c.Fps)).ToArray());

            Assert.Null(FpsCap.ParseHelperArgs(new[] { FpsCap.HelperArg }));
            Assert.Null(FpsCap.ParseHelperArgs(new[] { FpsCap.HelperArg, @"C:\x\cs2.exe=141" }));
            Assert.Null(FpsCap.ParseHelperArgs(new[] { FpsCap.HelperArg, "cs2.exe=-5" }));
            Assert.Null(FpsCap.ParseHelperArgs(new[] { FpsCap.HelperArg, "cs2.exe=99999" }));
            Assert.Null(FpsCap.ParseHelperArgs(new[] { "cs2.exe=141" }));
        }

        [Fact]
        public void NvidiaStructures_MatchTheSizesOfNvapiH()
        {
            // MAKE_NVAPI_VERSION(tipo, versão) = sizeof | versão << 16, com os tamanhos do nvapi.h (R590).
            Assert.Equal(0x00013020u, NvidiaLimiter.SettingVersion);
            Assert.Equal(0x0004500Cu, NvidiaLimiter.ApplicationVersion);
            Assert.Equal(0x00011014u, NvidiaLimiter.ProfileVersion);
            Assert.Equal(4100, NvidiaLimiter.SettingIdOffset);
            Assert.Equal(4108, NvidiaLimiter.SettingLocationOffset);
            Assert.Equal(8220, NvidiaLimiter.CurrentValueOffset);
            Assert.True(NvidiaLimiter.CurrentValueOffset + 4100 == NvidiaLimiter.SettingSize);
        }

        [Theory]
        [InlineData(0u, 141)] // NVDRS_CURRENT_PROFILE_LOCATION: o limite é do perfil do jogo
        [InlineData(1u, 0)]   // NVDRS_GLOBAL_PROFILE_LOCATION: herdado do "Taxa máxima de quadros" geral
        [InlineData(2u, 0)]   // NVDRS_BASE_PROFILE_LOCATION
        public void NvidiaCap_OnlyCountsTheGamesOwnProfile(uint location, int expected)
        {
            using (var setting = new NativeBuffer(NvidiaLimiter.SettingSize))
            {
                setting.WriteUInt32(NvidiaLimiter.SettingLocationOffset, location);
                setting.WriteUInt32(NvidiaLimiter.CurrentValueOffset, 141);
                Assert.Equal(expected, NvidiaLimiter.CapOf(setting));
            }
        }

        [Fact]
        public void IntelDevices_KeepTheMostSignificantFailure()
        {
            const uint other = 0x40000001; // outro erro da IGCL
            uint status = IntelLimiter.UnsupportedFeature; // nenhuma placa respondeu ainda

            // Arc pede administrador, a UHD diz "não suportado": o pedido de administrador não pode se perder.
            status = IntelLimiter.MoreSignificant(status, IntelLimiter.InsufficientPermissions);
            status = IntelLimiter.MoreSignificant(status, IntelLimiter.UnsupportedFeature);
            Assert.Equal(IntelLimiter.InsufficientPermissions, status);
            Assert.Equal(IntelLimiter.InsufficientPermissions, IntelLimiter.MoreSignificant(IntelLimiter.InsufficientPermissions, other));

            Assert.Equal(other, IntelLimiter.MoreSignificant(IntelLimiter.UnsupportedFeature, other));
            Assert.Equal(other, IntelLimiter.MoreSignificant(other, IntelLimiter.UnsupportedFeature));
            Assert.Equal(IntelLimiter.UnsupportedFeature, IntelLimiter.MoreSignificant(IntelLimiter.UnsupportedFeature, IntelLimiter.UnsupportedFeature));
        }

        [Fact]
        public void IntelStructure_MatchesIgclApiH()
        {
            // ctl_3d_feature_getset_t: 56 bytes em 64 bits, 40 em 32 (ponteiros e o union int32 alinhados como em C).
            Assert.Equal(IntPtr.Size == 8 ? 56 : 40, Marshal.SizeOf(typeof(IntelLimiter.FeatureGetSet)));
            Assert.Equal(IntPtr.Size == 8 ? 32 : 24, (int)Marshal.OffsetOf(typeof(IntelLimiter.FeatureGetSet), "Enable"));
            Assert.Equal(36, Marshal.SizeOf(typeof(IntelLimiter.InitArgs)));
        }

        [Fact]
        public void NativeBuffer_WritesUnicodeWithinBounds()
        {
            using (var b = NativeBuffer.Unicode("cs2.exe", 16))
            {
                Assert.Equal("cs2.exe", Marshal.PtrToStringUni(b.Pointer));
                Assert.Throws<ArgumentOutOfRangeException>(() => b.WriteUInt32(14, 1));
            }
            using (var cut = NativeBuffer.Unicode("um-nome-bem-longo.exe", 8))
                Assert.Equal("um-", Marshal.PtrToStringUni(cut.Pointer)); // 3 letras + o zero final
        }

        [Fact]
        public void Apply_WithoutElevationLeavesWhatNeedsTheAdministratorPending()
        {
            var driver = new FakeLimiter { AskAdmin = true };
            var applied = new List<string>();
            var changes = new List<FpsCapChange> { new FpsCapChange { Exe = "cs2.exe", Fps = 141 } };

            List<string> problems = FpsCap.Apply(changes, driver, applied, FpsCap.WithoutElevation);

            Assert.Equal(new[] { "cs2.exe: " + FpsCap.WithoutElevation(changes) }, problems.ToArray());
            Assert.Empty(applied); // fica para o próximo Salvar dos Ajustes
            Assert.False(driver.Driver.ContainsKey("cs2.exe"));
            Assert.Empty(FpsCap.Plan(new Dictionary<string, int>(), applied)); // e não vira um "tirar o limite"
            Assert.Single(FpsCap.Plan(new Dictionary<string, int> { { "cs2.exe", 141 } }, applied));
        }

        [Fact]
        public void OpenIfSame_OnlyOpensTheFileWithTheSameContentAndLocksIt()
        {
            using (var temp = new TempFolder())
            {
                string exe = temp.Text("app.exe", "o executável que está rodando");
                byte[] running = FpsCap.HashOf(exe);

                using (System.IO.FileStream same = FpsCap.OpenIfSame(exe, running))
                {
                    Assert.NotNull(same);
                    // Aberto: ninguém troca, grava nem renomeia o arquivo até o pedido de administrador sair.
                    Assert.Throws<System.IO.IOException>(() => System.IO.File.WriteAllText(exe, "outro"));
                    Assert.Throws<System.IO.IOException>(() => System.IO.File.Move(exe, temp.File("antigo.exe")));
                }

                System.IO.File.WriteAllText(exe, "outro programa no lugar");
                Assert.Null(FpsCap.OpenIfSame(exe, running));
                Assert.Null(FpsCap.OpenIfSame(exe, null));
                Assert.Null(FpsCap.OpenIfSame(temp.File("nao-existe.exe"), running));
                // Depois da recusa o arquivo não fica preso.
                System.IO.File.WriteAllText(exe, "o executável que está rodando");
                using (System.IO.FileStream back = FpsCap.OpenIfSame(exe, running)) Assert.NotNull(back);
            }
        }

        [Fact]
        public void UnsafeFileNextTo_FindsWhatWouldLoadCodeIntoTheAdministratorCopy()
        {
            using (var temp = new TempFolder())
            {
                string folder = temp.File("app");
                temp.Text(@"app\MouseModes.exe", "o app");
                temp.Text(@"app\Desinstalar.exe", "o desinstalador");
                temp.Text(@"app\leia.txt", "nada a ver");
                Assert.Null(FpsCap.UnsafeFileNextTo(folder, "MouseModes.exe"));

                // AppDomainManager pelo .config, redirecionamento de DLL pelo .local (arquivo ou pasta), DLL ao lado.
                foreach (string name in new[] { "MouseModes.exe.config", "MouseModes.exe.local", "version.dll", "QUALQUER.DLL" })
                {
                    string path = temp.Text(@"app\" + name, "x");
                    Assert.Equal(path, FpsCap.UnsafeFileNextTo(folder, "MouseModes.exe"), StringComparer.OrdinalIgnoreCase);
                    System.IO.File.Delete(path);
                }
                string localDir = temp.File(@"app\MouseModes.exe.local");
                System.IO.Directory.CreateDirectory(localDir);
                Assert.Equal(localDir, FpsCap.UnsafeFileNextTo(folder, "MouseModes.exe"));
                System.IO.Directory.Delete(localDir);

                // O nome vem do .exe que está rodando: o .config de outro .exe não conta.
                temp.Text(@"app\Outro.exe.config", "x");
                Assert.Null(FpsCap.UnsafeFileNextTo(folder, "MouseModes.exe"));
                Assert.NotNull(FpsCap.UnsafeFileNextTo(folder, "Outro.exe"));
            }
        }
    }
}
