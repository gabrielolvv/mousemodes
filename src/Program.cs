using System;
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
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            if (HasArg(args, "--quit")) return QuitRunningInstance() ? 0 : 1;
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
                    Application.Run(new TrayApp(startHidden, showSignal, quitSignal, commands, command));
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

        /// <summary>Usado pelo desinstalador depois do --quit.</summary>
        private static int UninstallCleanup()
        {
            try
            {
                AppConfig cfg = AppConfig.LoadOrCreate();
                // Mesma regra da abertura: se ficou no Performance, volta ao Dia a dia (com o plano anterior).
                var modes = new ModeManager(cfg);
                modes.Start();
                modes.WaitForPower(15000); // o processo termina logo depois: o powercfg precisa acabar antes
                NotificationSilencer.Restore(cfg); // só faz algo se ficaram silenciadas
                Startup.SetEnabled(false);
                Log.Info(L.T("Desinstalação: modo Dia a dia aplicado e removido do Iniciar com o Windows", "Uninstall: Daily mode applied and removed from Start with Windows"));
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error(L.T("Desinstalação", "Uninstall"), ex);
                return 1;
            }
        }
    }
}
