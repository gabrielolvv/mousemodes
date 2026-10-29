using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>
    /// Segundo plano calmo. Os testes que mexem em processo abrem um ping próprio e limitam o calmante a ele
    /// (Scope): nenhum programa de verdade do PC muda de prioridade. O config vai para uma pasta temporária.
    /// </summary>
    public class BackgroundCalmerTests
    {
        private static readonly IList<string> NoGames = new List<string>();

        [Theory]
        [InlineData("chrome", ProcessPriorityClass.Normal, true, null, true)]
        [InlineData("OneDrive", ProcessPriorityClass.Normal, true, "cs2", true)]
        [InlineData("chrome", ProcessPriorityClass.Normal, true, "chrome", false)]       // está na frente
        [InlineData("chrome", ProcessPriorityClass.Normal, false, null, false)]          // outra sessão
        [InlineData("chrome", ProcessPriorityClass.High, true, null, false)]             // já ajustado por alguém
        [InlineData("chrome", ProcessPriorityClass.BelowNormal, true, null, false)]
        [InlineData("explorer", ProcessPriorityClass.Normal, true, null, false)]         // Windows
        [InlineData("MouseModes", ProcessPriorityClass.Normal, true, null, false)]
        [InlineData("Discord", ProcessPriorityClass.Normal, true, null, false)]          // voz
        [InlineData("obs64", ProcessPriorityClass.Normal, true, null, false)]            // transmissão
        [InlineData("lghub_agent", ProcessPriorityClass.Normal, true, null, false)]      // software do mouse
        [InlineData("FiveM_b3095_GTAProcess", ProcessPriorityClass.Normal, true, null, false)] // jogo da lista
        public void ShouldCalm_FollowsTheRules(string name, ProcessPriorityClass priority, bool sameSession,
                                               string foreground, bool expected)
        {
            var games = new List<string> { "FiveM", "cs2.exe" };
            Assert.Equal(expected, BackgroundCalmer.ShouldCalm(name, priority, sameSession, games, foreground));
        }

        [Fact]
        public void Calm_LowersTheProgramAndRestoreAllPutsItBack()
        {
            using (var temp = new TempFolder())
            using (Process ping = StartPing())
            {
                try
                {
                    var cfg = new AppConfig();
                    var calmer = NewCalmer(cfg, temp, ping);

                    calmer.Calm(NoGames, null);
                    Assert.Equal(ProcessPriorityClass.BelowNormal, PriorityOf(ping));
                    Assert.True(calmer.Active);
                    Assert.Single(cfg.CalmBackup);
                    Assert.Equal(ping.Id, cfg.CalmBackup[0].Pid);
                    Assert.Equal(ProcessPriorityClass.Normal, cfg.CalmBackup[0].Priority);

                    calmer.RestoreAll();
                    Assert.Equal(ProcessPriorityClass.Normal, PriorityOf(ping));
                    Assert.False(calmer.Active);
                    Assert.Empty(cfg.CalmBackup);
                }
                finally
                {
                    Stop(ping);
                }
            }
        }

        [Fact]
        public void Calm_LeavesTheForegroundProgramAndTheGamesAlone()
        {
            using (var temp = new TempFolder())
            using (Process ping = StartPing())
            {
                try
                {
                    var calmer = NewCalmer(new AppConfig(), temp, ping);

                    calmer.Calm(NoGames, "PING");
                    Assert.Equal(ProcessPriorityClass.Normal, PriorityOf(ping));

                    calmer.Calm(new List<string> { "ping" }, null);
                    Assert.Equal(ProcessPriorityClass.Normal, PriorityOf(ping));
                    Assert.False(calmer.Active);
                }
                finally
                {
                    Stop(ping);
                }
            }
        }

        [Fact]
        public void Release_PutsTheProgramBackWhileItIsInFront()
        {
            using (var temp = new TempFolder())
            using (Process ping = StartPing())
            {
                try
                {
                    var cfg = new AppConfig();
                    var calmer = NewCalmer(cfg, temp, ping);
                    calmer.Calm(NoGames, null);
                    Assert.Equal(ProcessPriorityClass.BelowNormal, PriorityOf(ping));

                    calmer.Release("ping");
                    Assert.Equal(ProcessPriorityClass.Normal, PriorityOf(ping));
                    Assert.Empty(cfg.CalmBackup);

                    // Foi para trás de novo: a próxima checagem acalma outra vez.
                    calmer.Calm(NoGames, "explorer");
                    Assert.Equal(ProcessPriorityClass.BelowNormal, PriorityOf(ping));
                    calmer.RestoreAll();
                }
                finally
                {
                    Stop(ping);
                }
            }
        }

        [Fact]
        public void Recover_UndoesWhatACrashLeftBehind()
        {
            using (var temp = new TempFolder())
            using (Process ping = StartPing())
            {
                try
                {
                    ping.PriorityClass = ProcessPriorityClass.BelowNormal;
                    var cfg = new AppConfig();
                    cfg.CalmBackup.Add(Entry(ping, ping.StartTime.ToFileTimeUtc()));

                    BackgroundCalmer.Recover(cfg, () => cfg.SaveTo(temp.File("config.xml")));

                    Assert.Equal(ProcessPriorityClass.Normal, PriorityOf(ping));
                    Assert.Empty(cfg.CalmBackup);
                    Assert.Empty(AppConfig.Import(temp.File("config.xml")).CalmBackup);
                }
                finally
                {
                    Stop(ping);
                }
            }
        }

        [Fact]
        public void Recover_DoesNotTouchAnotherProcessWithTheSamePid()
        {
            using (var temp = new TempFolder())
            using (Process ping = StartPing())
            {
                try
                {
                    ping.PriorityClass = ProcessPriorityClass.BelowNormal;
                    var cfg = new AppConfig();
                    // Mesmo PID, outro início: o Windows reaproveitou o número para outro programa.
                    cfg.CalmBackup.Add(Entry(ping, ping.StartTime.ToFileTimeUtc() - 1));

                    BackgroundCalmer.Recover(cfg, () => cfg.SaveTo(temp.File("config.xml")));

                    Assert.Equal(ProcessPriorityClass.BelowNormal, PriorityOf(ping));
                    Assert.Empty(cfg.CalmBackup);
                }
                finally
                {
                    Stop(ping);
                }
            }
        }

        private static BackgroundCalmer NewCalmer(AppConfig cfg, TempFolder temp, Process only)
        {
            int pid = only.Id;
            return new BackgroundCalmer(cfg, () => cfg.SaveTo(temp.File("config.xml"))) { Scope = id => id == pid };
        }

        private static CalmedProcess Entry(Process proc, long startTime)
        {
            return new CalmedProcess
            {
                Pid = proc.Id,
                Name = proc.ProcessName,
                StartTime = startTime,
                Priority = ProcessPriorityClass.Normal
            };
        }

        /// <summary>Um processo inofensivo, só deste teste, que fica aberto por até 60 s.</summary>
        private static Process StartPing()
        {
            var info = new ProcessStartInfo("ping", "-n 60 127.0.0.1")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            Process proc = Process.Start(info);
            Assert.Equal(ProcessPriorityClass.Normal, proc.PriorityClass);
            return proc;
        }

        private static ProcessPriorityClass PriorityOf(Process proc)
        {
            proc.Refresh();
            return proc.PriorityClass;
        }

        private static void Stop(Process proc)
        {
            try { if (!proc.HasExited) proc.Kill(); } catch { }
        }
    }
}
