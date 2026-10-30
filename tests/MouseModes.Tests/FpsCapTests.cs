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
            Assert.Equal(8220, NvidiaLimiter.CurrentValueOffset);
            Assert.True(NvidiaLimiter.CurrentValueOffset + 4100 == NvidiaLimiter.SettingSize);
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
    }
}
