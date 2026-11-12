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

        /// <summary>
        /// Quanto esperar o launcher abrir o ping. Ele é acalmado de propósito (prioridade baixa, modo de eficiência,
        /// núcleos E): com o PC ocupado ele quase não ganha processador, por isso o launcher é o cmd (leve) e a espera é
        /// longa. No caso normal o ping aparece em uns 3 s.
        /// </summary>
        private static readonly TimeSpan LauncherWait = TimeSpan.FromSeconds(90);

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
        public void Calm_PutsTheProgramOnTheEfficiencyCoresOfAHybridCpu()
        {
            using (var temp = new TempFolder())
            using (Process ping = StartPing())
            {
                try
                {
                    var cfg = new AppConfig();
                    var calmer = NewCalmer(cfg, temp, ping);
                    bool hybrid = CpuSets.Background != null;

                    calmer.Calm(NoGames, null);
                    // No CI o processador pode não ser híbrido: aí os núcleos ficam como estão.
                    Assert.Equal(hybrid, cfg.CalmBackup[0].Cores);
                    Assert.Equal(hybrid ? CpuSets.Background.Length : 0, CpuSets.CountOf(ping.Handle));

                    calmer.RestoreAll();
                    Assert.Equal(0, CpuSets.CountOf(ping.Handle));
                }
                finally
                {
                    Stop(ping);
                }
            }
        }

        [Fact]
        public void Calm_AdoptsWhatACalmedProgramOpensAndGivesItBackLater()
        {
            using (var temp = new TempFolder())
            using (Process launcher = StartLauncher())
            {
                Process child = null;
                try
                {
                    var cfg = new AppConfig();
                    var calmer = NewCalmer(cfg, temp, launcher);
                    calmer.Calm(NoGames, null);
                    Assert.Equal(ProcessPriorityClass.BelowNormal, PriorityOf(launcher));

                    // O Windows passa a prioridade baixa para o que o programa acalmado abrir.
                    child = WaitForChild(launcher);
                    Assert.Equal(ProcessPriorityClass.BelowNormal, PriorityOf(child));

                    int launcherId = launcher.Id, childId = child.Id;
                    calmer.Scope = id => id == launcherId || id == childId;
                    calmer.Calm(NoGames, null);
                    CalmedProcess adopted = cfg.CalmBackup.Find(e => e.Pid == childId);
                    Assert.NotNull(adopted);
                    Assert.Equal(ProcessPriorityClass.Normal, adopted.Priority); // é o que ele teria sem o pai acalmado

                    calmer.RestoreAll();
                    Assert.Equal(ProcessPriorityClass.Normal, PriorityOf(launcher));
                    Assert.Equal(ProcessPriorityClass.Normal, PriorityOf(child));
                }
                finally
                {
                    if (child != null) Stop(child);
                    Stop(launcher);
                }
            }
        }

        [Fact]
        public void Calm_GivesAGameOpenedByACalmedLauncherItsNormalPriorityRightAway()
        {
            using (var temp = new TempFolder())
            using (Process launcher = StartLauncher())
            {
                Process game = null;
                try
                {
                    var cfg = new AppConfig();
                    var calmer = NewCalmer(cfg, temp, launcher);
                    var games = new List<string> { "ping" }; // o ping faz o papel do jogo aberto pela Steam
                    calmer.Calm(games, null);

                    game = WaitForChild(launcher);
                    Assert.Equal(ProcessPriorityClass.BelowNormal, PriorityOf(game));

                    int launcherId = launcher.Id, gameId = game.Id;
                    calmer.Scope = id => id == launcherId || id == gameId;
                    calmer.Calm(games, null);
                    Assert.Equal(ProcessPriorityClass.Normal, PriorityOf(game));
                    Assert.Null(cfg.CalmBackup.Find(e => e.Pid == gameId)); // o jogo não é acalmado

                    calmer.RestoreAll();
                }
                finally
                {
                    if (game != null) Stop(game);
                    Stop(launcher);
                }
            }
        }

        [Fact]
        public void Calm_AdoptsAGrandchildOpenedThroughAMiddleProcess()
        {
            // Launcher acalmado → cmd → ping: como Steam → bootstrapper do anti-cheat → jogo.
            using (var temp = new TempFolder())
            using (Process launcher = StartLauncher("cmd /c ping -n 120 127.0.0.1"))
            {
                Process grandchild = null;
                try
                {
                    var cfg = new AppConfig();
                    var calmer = NewCalmer(cfg, temp, launcher);
                    calmer.Calm(NoGames, null);

                    grandchild = WaitForPing(started => true, launcher.Id);
                    Assert.Equal(ProcessPriorityClass.BelowNormal, PriorityOf(grandchild));

                    int launcherId = launcher.Id, pingId = grandchild.Id;
                    calmer.Scope = id => id == launcherId || id == pingId;
                    calmer.Calm(NoGames, null);
                    CalmedProcess adopted = cfg.CalmBackup.Find(e => e.Pid == pingId);
                    Assert.NotNull(adopted);
                    Assert.Equal(ProcessPriorityClass.Normal, adopted.Priority);
                    // Ele passa a prioridade baixa adiante desde que nasceu.
                    Assert.Equal(adopted.StartTime, adopted.CalmedAt);

                    calmer.RestoreAll();
                    Assert.Equal(ProcessPriorityClass.Normal, PriorityOf(grandchild));
                }
                finally
                {
                    if (grandchild != null) Stop(grandchild);
                    Stop(launcher);
                }
            }
        }

        [Fact]
        public void Calm_GivesBackAGameWhoseLauncherChainAlreadyClosed()
        {
            // O cmd do meio abre o "jogo" e fecha na hora: não dá para subir até o launcher acalmado.
            using (var temp = new TempFolder())
            using (Process launcher = StartLauncher("cmd /c start /b ping -n 120 127.0.0.1"))
            {
                Process game = null;
                try
                {
                    var cfg = new AppConfig();
                    var calmer = NewCalmer(cfg, temp, launcher);
                    // O ping fica na pasta do Windows, que nunca conta como jogo: aqui ele faz o papel de um jogo de verdade.
                    calmer.IsGame = (proc, name, list) => GameBooster.Matches(name, list);
                    var games = new List<string> { "ping" };
                    DateTime began = DateTime.Now;
                    calmer.Calm(games, null);

                    game = WaitForPing(started => started >= began, null);
                    Assert.Equal(ProcessPriorityClass.BelowNormal, PriorityOf(game));

                    int launcherId = launcher.Id, gameId = game.Id;
                    calmer.Scope = id => id == launcherId || id == gameId;
                    calmer.Calm(games, null);
                    Assert.Equal(ProcessPriorityClass.Normal, PriorityOf(game));
                    Assert.Null(cfg.CalmBackup.Find(e => e.Pid == gameId));

                    calmer.RestoreAll();
                }
                finally
                {
                    if (game != null) Stop(game);
                    Stop(launcher);
                }
            }
        }

        [Fact]
        public void Calm_AdoptsAProgramWhoseLauncherChainAlreadyClosed()
        {
            // Como o Update.exe --processStart do Squirrel ou um cmd /c start acalmados: abre o programa e fecha na hora.
            // O programa não é jogo, mas herdou a prioridade baixa e precisa voltar ao normal no fim do Performance.
            // A prova de que é nosso: quem o abriu foi acalmado (o registro fica mesmo depois de ele fechar).
            using (var temp = new TempFolder())
            using (Process launcher = StartLauncher("start /b ping -n 120 127.0.0.1"))
            {
                Process program = null;
                try
                {
                    var cfg = new AppConfig();
                    var calmer = NewCalmer(cfg, temp, launcher);
                    DateTime began = DateTime.Now;
                    calmer.Calm(NoGames, null);

                    program = WaitForPing(started => started >= began, launcher.Id);
                    Assert.Equal(ProcessPriorityClass.BelowNormal, PriorityOf(program));
                    WaitForOrphan(program);
                    calmer.Calm(NoGames, null); // vê o launcher fechado: ele sai da lista dos acalmados

                    int launcherId = launcher.Id, programId = program.Id;
                    calmer.Scope = id => id == launcherId || id == programId;
                    calmer.Calm(NoGames, null);
                    CalmedProcess adopted = cfg.CalmBackup.Find(e => e.Pid == programId);
                    Assert.NotNull(adopted);
                    Assert.Equal(ProcessPriorityClass.Normal, adopted.Priority);
                    Assert.Equal(adopted.StartTime, adopted.CalmedAt);

                    calmer.RestoreAll();
                    Assert.Equal(ProcessPriorityClass.Normal, PriorityOf(program));
                }
                finally
                {
                    if (program != null) Stop(program);
                    Stop(launcher);
                }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)] // o ping bate com a lista de jogos, mas fica na pasta do Windows: não é jogo
        public void Calm_LeavesAnOrphanAloneWhenWhoOpenedItWasNeverCalmed(bool inGameList)
        {
            // Uma tarefa agendada nasce abaixo do normal e o processo que a abriu já fechou: nada a ver com o calmante.
            using (var temp = new TempFolder())
            using (Process calmedPing = StartPing())
            using (Process task = StartLauncher("start /b ping -n 120 127.0.0.1"))
            {
                Process program = null;
                try
                {
                    task.PriorityClass = ProcessPriorityClass.BelowNormal; // como o Agendador de Tarefas faz
                    var cfg = new AppConfig();
                    var calmer = NewCalmer(cfg, temp, calmedPing);
                    DateTime began = DateTime.Now;
                    calmer.Calm(NoGames, null);
                    Assert.True(calmer.Active); // tem algo acalmado neste Performance

                    program = WaitForPing(started => started >= began, task.Id);
                    Assert.Equal(ProcessPriorityClass.BelowNormal, PriorityOf(program));
                    WaitForOrphan(program);

                    int calmedId = calmedPing.Id, taskId = task.Id, programId = program.Id;
                    calmer.Scope = id => id == calmedId || id == taskId || id == programId;
                    calmer.Calm(inGameList ? new List<string> { "ping" } : NoGames, null);

                    Assert.Equal(ProcessPriorityClass.BelowNormal, PriorityOf(program));
                    Assert.Null(cfg.CalmBackup.Find(e => e.Pid == programId));
                    calmer.RestoreAll();
                    Assert.Equal(ProcessPriorityClass.BelowNormal, PriorityOf(program)); // não era nosso: fica como estava
                }
                finally
                {
                    if (program != null) Stop(program);
                    Stop(task);
                    Stop(calmedPing);
                }
            }
        }

        [Fact]
        public void LaunchedByCalmed_NeedsTheSameProcessAlreadyCalmedWhenTheOrphanStarted()
        {
            var history = new List<CalmedRecord>
            {
                new CalmedRecord { Pid = 100, CalmedAt = 1000 },             // ainda aberto
                new CalmedRecord { Pid = 200, CalmedAt = 1000, Gone = 5000 } // visto fechado na checagem de 5000
            };

            Assert.True(BackgroundCalmer.LaunchedByCalmed(history, 100, 1500));
            Assert.True(BackgroundCalmer.LaunchedByCalmed(history, 200, 4000));
            Assert.False(BackgroundCalmer.LaunchedByCalmed(history, 300, 1500));  // quem abriu nunca foi acalmado
            Assert.False(BackgroundCalmer.LaunchedByCalmed(history, 100, 900));   // nasceu antes de o pai ser acalmado
            Assert.False(BackgroundCalmer.LaunchedByCalmed(history, 200, 6000));  // o PID 200 já era de outro processo
            Assert.False(BackgroundCalmer.LaunchedByCalmed(new List<CalmedRecord>(), 100, 1500));
        }

        [Fact]
        public void MarkExited_UsesTheHandleForTheExactExitTimeAndThenClosesIt()
        {
            // O handle segura o PID (o Windows não o reaproveita enquanto ele está aberto) e diz quando o processo
            // fechou. Só abre handles e lê horas: nenhum processo muda de prioridade.
            using (Process ping = StartPing())
            {
                try
                {
                    long started = ping.StartTime.ToFileTimeUtc();
                    Assert.Null(BackgroundCalmer.OpenTracked(ping.Id, started + 1)); // outro processo com o mesmo PID
                    var record = new CalmedRecord { Pid = ping.Id, CalmedAt = started, Handle = BackgroundCalmer.OpenTracked(ping.Id, started) };
                    Assert.NotNull(record.Handle);
                    var withoutHandle = new CalmedRecord { Pid = 1, CalmedAt = started }; // fica para a regra do PID na foto
                    var history = new List<CalmedRecord> { record, withoutHandle };

                    BackgroundCalmer.MarkExited(history);
                    Assert.Equal(0, record.Gone); // ainda aberto
                    Assert.NotNull(record.Handle);

                    Stop(ping);
                    Assert.True(ping.WaitForExit(30000));
                    long exited = ping.ExitTime.ToFileTimeUtc();
                    BackgroundCalmer.MarkExited(history);
                    Assert.Equal(exited, record.Gone);
                    Assert.Null(record.Handle); // fechado: o PID pode voltar a ser usado, e o Gone já separa os dois
                    Assert.Equal(0, withoutHandle.Gone);
                    // Quem nasceu depois da saída, com o mesmo PID, não é filho dele.
                    Assert.False(BackgroundCalmer.LaunchedByCalmed(history, ping.Id, exited + 1));
                    Assert.True(BackgroundCalmer.LaunchedByCalmed(history, ping.Id, exited - 1));
                }
                finally
                {
                    Stop(ping);
                }
            }
        }

        [Fact]
        public void Calm_ReleasesAProgramThatBecameAGame()
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

                    // A pessoa pôs o programa na lista de jogos e salvou com o Performance ligado.
                    calmer.Calm(new List<string> { "ping" }, null);
                    Assert.Equal(ProcessPriorityClass.Normal, PriorityOf(ping));
                    Assert.Empty(cfg.CalmBackup);
                }
                finally
                {
                    Stop(ping);
                }
            }
        }

        [Fact]
        public void ParentIds_KnowsWhoOpenedEachProcess()
        {
            using (Process ping = StartPing())
            {
                try
                {
                    using (Process me = Process.GetCurrentProcess())
                        Assert.Equal(me.Id, BackgroundCalmer.ParentIds()[ping.Id]);
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
            // O filho herda a prioridade de quem roda os testes (no CI do GitHub ela é abaixo do normal).
            proc.PriorityClass = ProcessPriorityClass.Normal;
            Assert.Equal(ProcessPriorityClass.Normal, PriorityOf(proc));
            return proc;
        }

        /// <summary>
        /// Faz o papel de um launcher: um cmd só deste teste que, depois de 3 s (tempo de ser acalmado), abre um ping.
        /// Fica aberto até o ping terminar. A espera é o waitfor (esperando um sinal que nunca vem), que não é um ping
        /// e é leve como o cmd: acalmado num PC ocupado, um powershell levava mais de 90 s só para começar.
        /// </summary>
        private static Process StartLauncher(string then = "ping -n 120 127.0.0.1 >nul")
        {
            var info = new ProcessStartInfo("cmd", "/c waitfor /t 3 MouseModesTesteNuncaChega >nul 2>&1 & " + then)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process proc = Process.Start(info);
            proc.PriorityClass = ProcessPriorityClass.Normal;
            return proc;
        }

        /// <summary>
        /// Um ping aberto por este teste (espera até LauncherWait): descendente de <paramref name="ancestor"/>, ou, sem ele,
        /// qualquer ping que tenha começado depois do que o filtro pedir.
        /// </summary>
        private static Process WaitForPing(Func<DateTime, bool> startedOk, int? ancestor)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < LauncherWait)
            {
                Dictionary<int, int> parents = BackgroundCalmer.ParentIds();
                foreach (Process p in Process.GetProcessesByName("PING"))
                {
                    try
                    {
                        if (startedOk(p.StartTime) && (ancestor == null || Descends(p.Id, ancestor.Value, parents))) return p;
                    }
                    catch (InvalidOperationException)
                    {
                        // já fechou
                    }
                    p.Dispose();
                }
                System.Threading.Thread.Sleep(100);
            }
            throw new TimeoutException("o launcher do teste não abriu o ping em " + LauncherWait.TotalSeconds + " s");
        }

        private static bool Descends(int pid, int ancestor, Dictionary<int, int> parents)
        {
            for (int level = 0; level < 4; level++)
            {
                int parent;
                if (!parents.TryGetValue(pid, out parent)) return false;
                if (parent == ancestor) return true;
                pid = parent;
            }
            return false;
        }

        /// <summary>O ping que o launcher abriu (espera até LauncherWait).</summary>
        private static Process WaitForChild(Process launcher)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < LauncherWait)
            {
                foreach (KeyValuePair<int, int> pair in BackgroundCalmer.ParentIds())
                {
                    if (pair.Value != launcher.Id) continue;
                    try
                    {
                        Process child = Process.GetProcessById(pair.Key);
                        if (string.Equals(child.ProcessName, "PING", StringComparison.OrdinalIgnoreCase)) return child;
                        child.Dispose();
                    }
                    catch (ArgumentException)
                    {
                        // já fechou
                    }
                    catch (InvalidOperationException)
                    {
                        // fechou entre achar e ler o nome (o waitfor do launcher fecha sozinho depois de 3 s)
                    }
                }
                System.Threading.Thread.Sleep(100);
            }
            throw new TimeoutException("o launcher do teste não abriu o ping em " + LauncherWait.TotalSeconds + " s");
        }

        /// <summary>Espera o processo que abriu este fechar (o cmd do meio fecha logo depois de abrir o ping).</summary>
        private static void WaitForOrphan(Process proc)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < LauncherWait)
            {
                Dictionary<int, int> parents = BackgroundCalmer.ParentIds();
                int parent;
                if (parents.TryGetValue(proc.Id, out parent) && !parents.ContainsKey(parent)) return;
                System.Threading.Thread.Sleep(100);
            }
            throw new TimeoutException("o cmd do meio não fechou em " + LauncherWait.TotalSeconds + " s");
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
