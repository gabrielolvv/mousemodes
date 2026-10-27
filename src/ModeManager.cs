using System;
using System.Collections.Generic;

namespace MouseModes
{
    public class ModeManager
    {
        private readonly AppConfig cfg;

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

        public ModeManager(AppConfig cfg)
        {
            this.cfg = cfg;
            Current = Mode.DiaADia;
            CurrentProfile = cfg.Daily;
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
                if (crashed) Log.Warn("O app saiu com o modo Performance aplicado da última vez; voltando ao Dia a dia");
                recovering = crashed; // o Windows ainda está como Performance: o plano anterior também volta
                try { Apply(Mode.DiaADia, crashed ? "Recuperado ao iniciar" : "Iniciado"); }
                finally { recovering = false; }
                return;
            }

            SyncDailyFromWindows();
            Current = Mode.DiaADia;
            CurrentProfile = cfg.Daily;
            CurrentGameProfile = null;
            Log.Info("Modo Dia a dia — Iniciado (configurações do Windows mantidas)");

            var handler = ModeChanged;
            if (handler != null) handler(Mode.DiaADia, "Iniciado", new List<string>());
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
                if (restorePlan == null) Log.Info("Plano de energia anterior desconhecido; o atual fica");
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

            try { PowerSettings.Apply(profile, warnings, restorePlan); }
            catch (Exception ex) { warnings.Add(ex.Message); }

            Current = mode;
            CurrentProfile = profile;
            CurrentGameProfile = gameProfile;
            DailyMouseInUse = false; // o perfil acabou de ser aplicado inteiro
            cfg.LastMode = mode;
            SaveQuietly(); // grava já: se o app cair no Performance, a próxima abertura desfaz

            Log.Info("Modo " + (mode == Mode.Performance ? "Performance" : "Dia a dia") +
                     (gameProfile != null ? " (perfil de " + gameProfile + ")" : "") + " — " + reason);
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
                Log.Info(wantDaily ? "Jogo fora da tela: mouse do Dia a dia" : "Jogo na tela: mouse do Performance");
            }
            catch (Exception ex)
            {
                Log.Error("Trocar o mouse pela janela em foco", ex);
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

            Log.Info("Dia a dia atualizado com o mouse do Windows: velocidade " + d.PointerSpeed + " → " + now.PointerSpeed +
                     ", aceleração " + OnOff(d.EnhancePointerPrecision) + " → " + OnOff(now.EnhancePointerPrecision) +
                     ", rolagem " + d.ScrollLines + " → " + now.ScrollLines);
            d.PointerSpeed = now.PointerSpeed;
            d.EnhancePointerPrecision = now.EnhancePointerPrecision;
            d.ScrollLines = now.ScrollLines;
        }

        /// <summary>Com o Dia a dia em "ANTERIOR", guarda o plano de energia ativo antes de o Performance trocar.</summary>
        private void RememberDailyPlan()
        {
            if (cfg.Daily.PowerPlan != PowerPlanOption.NaoAlterar) return;
            string guid = PowerSettings.ActivePlanGuid();
            if (guid == null || guid == cfg.DailyPlanGuid) return;
            cfg.DailyPlanGuid = guid;
            Log.Info("Plano de energia do Dia a dia guardado: " + guid);
        }

        /// <summary>O config é editável à mão: só passa adiante (para o powercfg) um GUID de verdade.</summary>
        private static string ValidGuid(string value)
        {
            Guid parsed;
            return value != null && Guid.TryParse(value, out parsed) ? parsed.ToString() : null;
        }

        private static string OnOff(bool on)
        {
            return on ? "ligada" : "desligada";
        }

        private void SaveQuietly()
        {
            try { cfg.Save(); } catch (Exception ex) { Log.Error("Salvar configuração", ex); }
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
            Log.Info("Restaurando padrões do Windows (velocidade 10, aceleração ligada, rolagem 3, Equilibrado, USB padrão)");
            Apply(Mode.DiaADia, "Padrões do Windows restaurados");
        }
    }
}
