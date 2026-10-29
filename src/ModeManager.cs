using System;
using System.Collections.Generic;
using System.Threading;

namespace MouseModes
{
    public class ModeManager
    {
        private readonly AppConfig cfg;

        // Plano de energia e USB (powercfg, centenas de ms) rodam fora da thread da tela, um de cada vez.
        // Só o pedido mais recente espera na fila: trocar de modo várias vezes seguidas aplica só o último.
        private readonly object powerGate = new object();
        private readonly ManualResetEvent powerIdle = new ManualResetEvent(true);
        private Action pendingPower;
        private bool powerRunning;

        /// <summary>Quem aplica plano e USB. Interno e trocável só para testar a fila sem mexer no PC.</summary>
        internal Action<MouseProfile, List<string>, string> PowerApplier;

        public Mode Current { get; private set; }

        /// <summary>Perfil efetivamente aplicado (pode ser o perfil próprio de um jogo).</summary>
        public MouseProfile CurrentProfile { get; private set; }

        /// <summary>Nome do jogo cujo perfil próprio está em uso, ou null.</summary>
        public string CurrentGameProfile { get; private set; }

        /// <summary>Informa qual jogo da lista está aberto agora (ou null).</summary>
        public Func<string> RunningGame { get; set; }

        /// <summary>Performance aplicado, mas com o mouse do Dia a dia no momento (jogo fora da tela).</summary>
        public bool DailyMouseInUse { get; private set; }

        private bool recovering; // Start() desfazendo um Performance que ficou aplicado

        /// <summary>(modo aplicado, motivo, avisos de coisas que falharam)</summary>
        public event Action<Mode, string, List<string>> ModeChanged;

        /// <summary>O plano de energia ou o USB falharam. Chega depois do ModeChanged, numa thread de fundo.</summary>
        public event Action<List<string>> PowerWarnings;

        public ModeManager(AppConfig cfg)
        {
            this.cfg = cfg;
            Current = Mode.DiaADia;
            CurrentProfile = cfg.Daily;
            // O config guarda as cópias de planos que o app cria (vão para o disco no próximo Save).
            PowerApplier = (p, warnings, restorePlan) => PowerSettings.Apply(p, warnings, restorePlan, cfg);
        }

        /// <summary>
        /// Abertura do app. Se da última vez ele saiu (ou caiu) com o Performance aplicado, volta ao Dia a dia.
        /// Senão o Windows já está no Dia a dia: não mexe em nada, só traz para o perfil o mouse de agora.
        /// </summary>
        public void Start()
        {
            if (cfg.LastMode != Mode.DiaADia)
            {
                bool crashed = cfg.LastMode == Mode.Performance;
                if (crashed) Log.Warn(L.T("O app saiu com o modo Performance aplicado da última vez; voltando ao Dia a dia", "The app quit with Performance mode applied last time; going back to Daily"));
                recovering = crashed; // o Windows ainda está como Performance: o plano anterior também volta
                try { Apply(Mode.DiaADia, crashed ? L.T("Recuperado ao iniciar", "Recovered at startup") : L.T("Iniciado", "Started")); }
                finally { recovering = false; }
                return;
            }

            SyncDailyFromWindows();
            Current = Mode.DiaADia;
            CurrentProfile = cfg.Daily;
            CurrentGameProfile = null;
            Log.Info(L.T("Modo Dia a dia — Iniciado (configurações do Windows mantidas)", "Daily mode — Started (Windows settings kept)"));

            var handler = ModeChanged;
            if (handler != null) handler(Mode.DiaADia, L.T("Iniciado", "Started"), new List<string>());
        }

        public void Apply(Mode mode, string reason)
        {
            // Saindo do Dia a dia: o perfil passa a ser o mouse como está agora no Windows,
            // inclusive o que a pessoa mudou fora do app. É isso que volta depois.
            if (mode == Mode.Performance && Current == Mode.DiaADia)
            {
                SyncDailyFromWindows();
                RememberDailyPlan();
            }

            // Plano "ANTERIOR" só volta ao sair do Performance; salvar os Ajustes no Dia a dia não mexe no plano.
            string restorePlan = null;
            bool leavingPerformance = mode == Mode.DiaADia && (Current == Mode.Performance || recovering);
            if (leavingPerformance && cfg.Daily.PowerPlan == PowerPlanOption.NaoAlterar)
            {
                restorePlan = ValidGuid(cfg.DailyPlanGuid);
                if (restorePlan == null) Log.Info(L.T("Plano de energia anterior desconhecido; o atual fica", "Previous power plan unknown; keeping the current one"));
            }

            MouseProfile profile;
            string gameProfile = null;
            if (mode == Mode.Performance)
            {
                string game = RunningGame != null ? RunningGame() : null;
                profile = cfg.PerformanceFor(game);
                if (profile != cfg.Performance) gameProfile = game;
            }
            else
            {
                profile = cfg.Daily;
            }

            var warnings = new List<string>();

            try { MouseSettings.Apply(profile); }
            catch (Exception ex) { warnings.Add(ex.Message); }

            QueuePower(profile, restorePlan);

            Current = mode;
            CurrentProfile = profile;
            CurrentGameProfile = gameProfile;
            DailyMouseInUse = false; // o perfil acabou de ser aplicado inteiro
            cfg.LastMode = mode;
            SaveQuietly(); // grava já: se o app cair no Performance, a próxima abertura desfaz

            Log.Info(L.T("Modo ", "Mode ") + (mode == Mode.Performance ? "Performance" : L.T("Dia a dia", "Daily")) +
                     (gameProfile != null ? L.T(" (perfil de ", " (profile of ") + gameProfile + ")" : "") + " — " + reason);
            foreach (string w in warnings) Log.Warn(w);

            var handler = ModeChanged;
            if (handler != null) handler(mode, reason, warnings);
        }

        /// <summary>
        /// Opção "mouse do jogo só com o jogo na tela": no Performance, troca só o mouse conforme o jogo
        /// está ou não em primeiro plano. Energia, USB e Turbo continuam como estão.
        /// </summary>
        public void SetGameFocused(bool focused)
        {
            bool wantDaily = Current == Mode.Performance && cfg.MouseOnlyWhenGameFocused && !focused;
            if (wantDaily == DailyMouseInUse) return;
            try
            {
                MouseSettings.Apply(wantDaily ? cfg.Daily : CurrentProfile, false);
                DailyMouseInUse = wantDaily;
                Log.Info(wantDaily ? L.T("Jogo fora da tela: mouse do Dia a dia", "Game off screen: Daily mouse") : L.T("Jogo na tela: mouse do Performance", "Game on screen: Performance mouse"));
            }
            catch (Exception ex)
            {
                Log.Error(L.T("Trocar o mouse pela janela em foco", "Switch the mouse by the window in focus"), ex);
            }
        }

        /// <summary>Copia velocidade, aceleração e rolagem do Windows para o perfil Dia a dia (energia e USB ficam).</summary>
        private void SyncDailyFromWindows()
        {
            MouseProfile now = MouseSettings.Read();
            MouseProfile d = cfg.Daily;
            if (now.PointerSpeed == d.PointerSpeed &&
                now.EnhancePointerPrecision == d.EnhancePointerPrecision &&
                now.ScrollLines == d.ScrollLines) return;

            Log.Info(L.T("Dia a dia atualizado com o mouse do Windows: velocidade ", "Daily updated from the Windows mouse: speed ") + d.PointerSpeed + " → " + now.PointerSpeed +
                     L.T(", aceleração ", ", acceleration ") + OnOff(d.EnhancePointerPrecision) + " → " + OnOff(now.EnhancePointerPrecision) +
                     L.T(", rolagem ", ", scroll ") + d.ScrollLines + " → " + now.ScrollLines);
            d.PointerSpeed = now.PointerSpeed;
            d.EnhancePointerPrecision = now.EnhancePointerPrecision;
            d.ScrollLines = now.ScrollLines;
        }

        /// <summary>
        /// Espera o plano de energia terminar de ser aplicado. Para quem vai encerrar o processo logo depois
        /// (sair do app, desinstalar): a thread de fundo morreria no meio do powercfg.
        /// </summary>
        public bool WaitForPower(int timeoutMs)
        {
            bool done = powerIdle.WaitOne(timeoutMs);
            if (!done) Log.Warn(L.T("Plano de energia ainda sendo aplicado depois de ", "Power plan still being applied after ") + timeoutMs / 1000 + " s");
            return done;
        }

        internal void QueuePower(MouseProfile profile, string restorePlan)
        {
            MouseProfile snapshot = profile.Clone(); // os Ajustes podem mudar o perfil enquanto isto roda
            lock (powerGate)
            {
                pendingPower = () => RunPower(snapshot, restorePlan);
                powerIdle.Reset();
                if (powerRunning) return; // a thread que já roda pega este pedido em seguida
                powerRunning = true;
            }
            var worker = new Thread(PowerLoop) { IsBackground = true, Name = "MouseModes energia" };
            worker.Start();
        }

        private void PowerLoop()
        {
            while (true)
            {
                Action job;
                lock (powerGate)
                {
                    job = pendingPower;
                    pendingPower = null;
                    if (job == null)
                    {
                        powerRunning = false;
                        powerIdle.Set();
                        return;
                    }
                }
                job();
            }
        }

        private void RunPower(MouseProfile profile, string restorePlan)
        {
            var warnings = new List<string>();
            try { PowerApplier(profile, warnings, restorePlan); }
            catch (Exception ex) { warnings.Add(ex.Message); }
            if (warnings.Count == 0) return;

            foreach (string w in warnings) Log.Warn(w);
            var handler = PowerWarnings;
            if (handler != null)
            {
                try { handler(warnings); } catch (Exception ex) { Log.Error(L.T("Avisar falha do plano de energia", "Report power plan failure"), ex); }
            }
        }

        /// <summary>Com o Dia a dia em "ANTERIOR", guarda o plano de energia ativo antes de o Performance trocar.</summary>
        private void RememberDailyPlan()
        {
            if (cfg.Daily.PowerPlan != PowerPlanOption.NaoAlterar) return;
            // Com uma troca de plano em andamento (ex.: voltando ao anterior), o ativo agora não é o "de antes".
            // Espera ela acabar; só acontece quando os modos trocam em sequência rápida.
            if (!powerIdle.WaitOne(3000))
            {
                Log.Info(L.T("Plano de energia ainda trocando; mantido o plano do Dia a dia guardado antes", "Power plan still changing; keeping the Daily plan saved before"));
                return;
            }
            string guid = PowerSettings.ActivePlanGuid();
            if (guid == null || guid == cfg.DailyPlanGuid) return;
            cfg.DailyPlanGuid = guid;
            Log.Info(L.T("Plano de energia do Dia a dia guardado: ", "Daily power plan saved: ") + guid);
        }

        /// <summary>O config é editável à mão: só passa adiante (para o powercfg) um GUID de verdade.</summary>
        internal static string ValidGuid(string value)
        {
            Guid parsed;
            return value != null && Guid.TryParse(value, out parsed) ? parsed.ToString() : null;
        }

        private static string OnOff(bool on)
        {
            return on ? L.T("ligada", "on") : L.T("desligada", "off");
        }

        private void SaveQuietly()
        {
            try { cfg.Save(); } catch (Exception ex) { Log.Error(L.T("Salvar configuração", "Save settings"), ex); }
        }

        /// <summary>Volta tudo ao padrão de fábrica do Windows e passa a usar isso como Dia a dia.</summary>
        public void RestoreWindowsDefaults()
        {
            var defaults = new MouseProfile
            {
                PointerSpeed = 10,
                EnhancePointerPrecision = true,
                ScrollLines = 3,
                PowerPlan = PowerPlanOption.Equilibrado,
                DisableUsbSelectiveSuspend = false
            };
            cfg.Daily = defaults;
            Log.Info(L.T("Restaurando padrões do Windows (velocidade 10, aceleração ligada, rolagem 3, Equilibrado, USB padrão)", "Restoring Windows defaults (speed 10, acceleration on, scroll 3, Balanced, default USB)"));
            Apply(Mode.DiaADia, L.T("Padrões do Windows restaurados", "Windows defaults restored"));
        }
    }
}
