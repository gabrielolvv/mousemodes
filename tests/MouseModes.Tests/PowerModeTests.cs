using System;
using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Modo de energia com um Windows de mentira: nenhum teste mexe na energia do PC de verdade.</summary>
    public class PowerModeTests
    {
        private sealed class FakePowerMode : IPowerMode
        {
            public Guid? Mode = PowerModeKeeper.BestEfficiency;
            public bool Accept = true;
            public readonly List<string> Calls = new List<string>();

            public bool OnAC { get; set; }

            public FakePowerMode() { OnAC = true; }

            public Guid? Get() { return Mode; }

            public bool Set(Guid mode)
            {
                Calls.Add(PowerModeKeeper.Name(mode));
                if (Accept) Mode = mode;
                return Accept;
            }
        }

        private static PowerModeKeeper New(AppConfig cfg, FakePowerMode windows)
        {
            return new PowerModeKeeper(cfg, () => { }, windows);
        }

        [Fact]
        public void Ensure_GoesToBestPerformanceAndComesBack()
        {
            var cfg = new AppConfig();
            var windows = new FakePowerMode();
            PowerModeKeeper keeper = New(cfg, windows);

            keeper.Ensure(true);
            keeper.Ensure(true); // a cada 3 s: não troca de novo
            Assert.Equal(PowerModeKeeper.BestPerformance, windows.Mode);
            Assert.Equal(PowerModeKeeper.BestEfficiency.ToString(), cfg.PowerModeBackup);

            keeper.Ensure(false);
            Assert.Equal(PowerModeKeeper.BestEfficiency, windows.Mode);
            Assert.Null(cfg.PowerModeBackup);
            Assert.Equal(2, windows.Calls.Count);
        }

        [Fact]
        public void Ensure_LeavesItAloneWhenItIsAlreadyBest()
        {
            var cfg = new AppConfig();
            var windows = new FakePowerMode { Mode = PowerModeKeeper.BestPerformance };
            PowerModeKeeper keeper = New(cfg, windows);

            keeper.Ensure(true);
            keeper.Ensure(false);

            Assert.Empty(windows.Calls);
            Assert.Null(cfg.PowerModeBackup);
        }

        [Fact]
        public void Ensure_DoesNotTouchItOnBatteryAndRestoresBackOnTheCharger()
        {
            var cfg = new AppConfig();
            var windows = new FakePowerMode { OnAC = false };
            PowerModeKeeper keeper = New(cfg, windows);

            keeper.Ensure(true);
            Assert.Empty(windows.Calls); // na bateria: não gasta a carga

            windows.OnAC = true;
            keeper.Ensure(true);         // ligou na tomada no meio do jogo
            Assert.Equal(PowerModeKeeper.BestPerformance, windows.Mode);

            windows.OnAC = false;        // tirou da tomada e saiu do Performance
            keeper.Ensure(false);
            Assert.NotNull(cfg.PowerModeBackup); // o da tomada volta quando a tomada voltar

            windows.OnAC = true;
            keeper.Ensure(false);
            Assert.Equal(PowerModeKeeper.BestEfficiency, windows.Mode);
            Assert.Null(cfg.PowerModeBackup);
        }

        [Fact]
        public void Restore_KeepsWhatThePersonChoseDuringTheGame()
        {
            var cfg = new AppConfig();
            var windows = new FakePowerMode();
            PowerModeKeeper keeper = New(cfg, windows);
            keeper.Ensure(true);

            windows.Mode = PowerModeKeeper.Balanced; // mudou no Windows com o jogo aberto
            keeper.Ensure(false);

            Assert.Equal(PowerModeKeeper.Balanced, windows.Mode);
            Assert.Null(cfg.PowerModeBackup);
        }

        [Fact]
        public void Ensure_DoesNotRetryEvery3SecondsWhenWindowsRefuses()
        {
            var cfg = new AppConfig();
            var windows = new FakePowerMode { Accept = false };
            PowerModeKeeper keeper = New(cfg, windows);

            keeper.Ensure(true);
            keeper.Ensure(true);
            Assert.Single(windows.Calls);
            Assert.Null(cfg.PowerModeBackup);

            keeper.Ensure(false); // saiu do Performance: na próxima partida tenta de novo
            windows.Accept = true;
            keeper.Ensure(true);
            Assert.Equal(PowerModeKeeper.BestPerformance, windows.Mode);

            var old = new FakePowerMode { Mode = null }; // Windows sem o controle de modo de energia
            New(new AppConfig(), old).Ensure(true);
            Assert.Empty(old.Calls);
        }

        [Fact]
        public void Restore_AfterACrashUsesTheSavedMode()
        {
            var cfg = new AppConfig { PowerModeBackup = PowerModeKeeper.BetterPerformance.ToString() };
            var windows = new FakePowerMode { Mode = PowerModeKeeper.BestPerformance };

            New(cfg, windows).Restore();

            Assert.Equal(PowerModeKeeper.BetterPerformance, windows.Mode);
            Assert.Null(cfg.PowerModeBackup);
        }

        [Fact]
        public void PowerSource_ReadsWithoutErrors()
        {
            // Só lê o estado da bateria deste PC; não muda nada.
            Assert.Null(Record.Exception(() => { GC.KeepAlive(PowerSource.OnAC); GC.KeepAlive(PowerSource.BatteryPercent); }));
        }
    }
}
