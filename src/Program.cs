using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace MouseModes
{
    internal static class Program
    {
        internal const string MutexName = "Local\\MouseModes_SingleInstance";
        internal const string ShowEventName = "Local\\MouseModes_Show";
        internal const string QuitEventName = "Local\\MouseModes_Quit";

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        /// <summary>
        /// Argumentos:
        ///   --tray               inicia só na bandeja (usado ao iniciar com o Windows)
        ///   --quit               encerra a cópia aberta, restaurando tudo (usado pelo instalador)
        ///   --uninstall-cleanup  volta o mouse/energia ao modo Dia a dia e tira do "Iniciar com o Windows"
        ///   --performance, --daily, --toggle
        ///                        troca o modo da cópia aberta; sem cópia aberta, abre na bandeja já nesse modo
        ///   --updated            aberto pelo instalador depois de uma atualização sozinha (avisa e limpa o download)
        ///   --fps-caps jogo.exe=141 ...
        ///                        cópia aberta como administrador pelo próprio app: grava os limites de FPS no driver e sai
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            if (HasArg(args, FpsCap.HelperArg)) return FpsCap.RunHelper(args);
            if (HasArg(args, "--quit")) return QuitRunningInstance() ? 0 : 1;
            // Daqui em diante pode haver pedido de administrador (limite de FPS): o SHA-256 do .exe é lido já, antes de
            // qualquer outra coisa, para o pedido conferir que o .exe no disco ainda é este.
            FpsCap.RememberRunningImage();
            if (HasArg(args, "--uninstall-cleanup")) return UninstallCleanup();

            string command = CommandSignals.Find(args);
            bool startHidden = HasArg(args, "--tray") || command != null;

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
            using (var quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName))
            {
                if (!createdNew)
                {
                    // Já está rodando: pede para a cópia aberta trocar o modo ou mostrar a janela, e sai.
                    if (command != null) return CommandSignals.Send(command) ? 0 : 1; // 1: versão antiga, sem o comando
                    // --tray (início com o Windows, instalador): já está na bandeja, não há o que mostrar.
                    if (startHidden) return 0;
                    showSignal.Set();
                    return 0;
                }

#if NET5_0_OR_GREATER
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
#else
                // .NET Framework: avisa o Windows que o app sabe lidar com escala (texto nítido em 125%/150%).
                try { SetProcessDPIAware(); } catch { }
#endif
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) =>
                {
                    Log.Error(L.T("Erro inesperado", "Unexpected error"), e.Exception);
                    MessageBox.Show("Erro inesperado: " + e.Exception.Message, "MouseModes",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                };

                using (var commands = new CommandSignals())
                    Application.Run(new TrayApp(startHidden, showSignal, quitSignal, commands, command, HasArg(args, "--updated")));
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        private static bool HasArg(string[] args, string name)
        {
            return Array.Exists(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Pede para a cópia aberta sair (ela restaura tudo) e espera até 20 s.</summary>
        private static bool QuitRunningInstance()
        {
            Mutex mutex;
            if (!Mutex.TryOpenExisting(MutexName, out mutex)) return true; // nada aberto

            using (mutex)
            {
                EventWaitHandle quit;
                if (EventWaitHandle.TryOpenExisting(QuitEventName, out quit))
                {
                    using (quit) quit.Set();
                }
                else
                {
                    return false; // versão antiga, sem suporte a --quit
                }

                // Espera a outra cópia liberar o mutex (= terminou de sair).
                try
                {
                    if (mutex.WaitOne(TimeSpan.FromSeconds(20)))
                    {
                        mutex.ReleaseMutex();
                        return true;
                    }
                }
                catch (AbandonedMutexException)
                {
                    return true; // saiu sem liberar: mesmo assim encerrou
                }
                return false;
            }
        }

        /// <summary>
        /// Usado pelo desinstalador depois do --quit (a cópia aberta já saiu e restaurou o que era dela). Desfaz o que
        /// pode ter ficado no Windows. Cada parte isolada: se uma falhar, as outras ainda rodam.
        /// Saída: 0 = tudo certo; 1 = alguma parte falhou (está no log); PowerModeLeftChanged = o modo de energia da
        /// tomada pode ter ficado em Melhor desempenho (o desinstalador avisa na tela: o log pode ser apagado junto).
        /// </summary>
        private static int UninstallCleanup()
        {
            AppConfig cfg;
            try { cfg = AppConfig.LoadOrCreate(); }
            catch (Exception ex)
            {
                Log.Error(L.T("Desinstalação", "Uninstall"), ex);
                return 1;
            }

            bool ok = true;
            bool planSettled = false;
            bool powerModeLeft = false;
            // Mesma regra da abertura: se ficou no Performance, volta ao Dia a dia (com o plano anterior).
            ok &= Step(L.T("Desinstalação: modo Dia a dia", "Uninstall: Daily mode"), () =>
            {
                var modes = new ModeManager(cfg);
                modes.Start();
                planSettled = modes.WaitForPower(15000); // o processo termina logo depois: o powercfg precisa acabar antes
            });
            ok &= Step(L.T("Desinstalação: notificações", "Uninstall: notifications"), () => NotificationSilencer.Restore(cfg)); // só se ficaram silenciadas
            ok &= Step(L.T("Desinstalação: segundo plano calmo", "Uninstall: calm background"), () => BackgroundCalmer.Recover(cfg));
            // Na bateria não mexe: o Windows trocaria o modo da bateria, e o guardado é o da tomada.
            ok &= Step(L.T("Desinstalação: modo de energia", "Uninstall: power mode"), () =>
            {
                var keeper = new PowerModeKeeper(cfg);
                if (cfg.PowerModeBackup != null && !keeper.OnAC)
                {
                    powerModeLeft = true;
                    Log.Warn(L.T("Desinstalação na bateria: o modo de energia da tomada pode ter ficado em Melhor desempenho; ajuste em Configurações → Energia com o PC na tomada",
                                 "Uninstall on battery: the plugged-in power mode may have stayed on Best performance; set it in Settings → Power while plugged in"));
                }
                else if (!keeper.Restore())
                {
                    powerModeLeft = true;
                    Log.Warn(L.T("Desinstalação: o modo de energia do Windows não voltou; ajuste em Configurações → Energia",
                                 "Uninstall: the Windows power mode did not go back; set it in Settings → Power"));
                }
            });
            // A sessão de leitura dos quadros desta conta, se o app caiu no meio de uma partida.
            ok &= Step(L.T("Desinstalação: leitura dos quadros", "Uninstall: frame reading"), () => FrameProbe.StopLeftover());
            ok &= Step(L.T("Desinstalação: permissão do Caça-travadinhas", "Uninstall: stutter finder permission"), () => RemoveFramePermission(cfg));
            // Depois do Dia a dia: o plano que ele deixou ativo nunca é apagado. Com a troca de plano ainda rodando, o ativo
            // pode mudar a qualquer momento: as cópias ficam.
            ok &= Step(L.T("Desinstalação: cópias de planos de energia", "Uninstall: power plan copies"), () =>
            {
                if (!planSettled)
                {
                    Log.Warn(L.T("Desinstalação: plano de energia ainda trocando; cópias de planos do app ficam", "Uninstall: power plan still changing; the app's plan copies stay"));
                    return;
                }
                PowerSettings.DeletePlanCopies(cfg);
                cfg.Save();
            });
            ok &= Step(L.T("Desinstalação: resolução", "Uninstall: resolution"), () => GameResolution.Recover(cfg));
            ok &= Step(L.T("Desinstalação: limites de FPS", "Uninstall: FPS limits"), () => RemoveFpsCaps(cfg));
            ok &= Step(L.T("Desinstalação: Iniciar com o Windows", "Uninstall: Start with Windows"), () => Startup.SetEnabled(false));
            Log.Info(L.T("Desinstalação: modo Dia a dia aplicado e removido do Iniciar com o Windows", "Uninstall: Daily mode applied and removed from Start with Windows"));
            if (powerModeLeft) return PowerModeLeftChanged; // o aviso que importa para a pessoa; o resto está no log
            return ok ? 0 : 1;
        }

        /// <summary>Saída do --uninstall-cleanup quando o modo de energia não voltou (o MouseModes.nsi confere este número).</summary>
        internal const int PowerModeLeftChanged = 3;

        private static bool Step(string what, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(what, ex);
                return false;
            }
        }

        /// <summary>
        /// Tira a conta do grupo "Performance Log Users" só se foi o app que pôs (FramePermissionGranted). Abre o pedido de
        /// administrador do Windows; a espera tem limite para a desinstalação nunca ficar parada nele.
        /// </summary>
        private static void RemoveFramePermission(AppConfig cfg)
        {
            if (!cfg.FramePermissionGranted) return;
            string error = L.T("o Windows não respondeu em 90 s", "Windows did not answer within 90 s");
            var worker = new Thread(() =>
            {
                bool alreadyMember; // só vale ao pôr
                try { error = FramePermission.Change(false, out alreadyMember); }
                catch (Exception ex) { error = ex.Message; }
            }) { IsBackground = true, Name = "MouseModes permissão dos quadros" };
            worker.Start();
            worker.Join(90000);
            if (error != null)
            {
                Log.Warn(L.T("Desinstalação: a conta continua no grupo Performance Log Users (", "Uninstall: the account is still in Performance Log Users (") + error + ")");
                return;
            }
            cfg.FramePermissionGranted = false;
            cfg.FramePermissionAsked = false;
            cfg.Save();
            Log.Info(L.T("Desinstalação: conta tirada do grupo Performance Log Users", "Uninstall: account removed from Performance Log Users"));
        }

        /// <summary>
        /// Tira do driver os limites de FPS que o app gravou (eles valeriam mesmo sem o app). O driver pode pedir
        /// administrador; a espera tem limite para a desinstalação nunca ficar parada no pedido.
        /// </summary>
        private static void RemoveFpsCaps(AppConfig cfg)
        {
            if (cfg.FpsCapsApplied == null || cfg.FpsCapsApplied.Count == 0) return;
            List<FpsCapChange> changes = FpsCap.Plan(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), cfg.FpsCapsApplied);
            if (changes.Count == 0) return;

            var applied = new List<string>(cfg.FpsCapsApplied);
            List<string> problems = null;
            IFpsLimiter limiter = null;
            Exception failure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    limiter = FpsCap.Detect();
                    if (limiter != null) problems = FpsCap.Apply(changes, limiter, applied, FpsCap.RunElevated);
                }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true, Name = "MouseModes limites de FPS" };
            worker.Start();
            if (!worker.Join(90000))
            {
                Log.Warn(L.T("Desinstalação: o driver não terminou de tirar os limites de FPS em 90 s", "Uninstall: the driver did not finish removing the FPS limits within 90 s"));
                return;
            }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            if (limiter == null)
            {
                Log.Warn(L.T("Desinstalação: nenhuma placa com limitador de FPS encontrada; limites gravados antes ficam no driver: ",
                             "Uninstall: no video card with an FPS limiter found; limits saved before stay in the driver: ") + string.Join(", ", cfg.FpsCapsApplied.ToArray()));
                return;
            }

            cfg.FpsCapsApplied = applied;
            cfg.Save();
            foreach (string p in problems) Log.Warn(L.T("Desinstalação: limite de FPS não tirado: ", "Uninstall: FPS limit not removed: ") + p);
            if (problems.Count == 0) Log.Info(L.T("Desinstalação: limites de FPS tirados do driver ", "Uninstall: FPS limits removed from the driver ") + limiter.Name);
        }
    }
}
