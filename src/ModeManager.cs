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

        /// <summary>(modo aplicado, motivo, avisos de coisas que falharam)</summary>
        public event Action<Mode, string, List<string>> ModeChanged;

        public ModeManager(AppConfig cfg)
        {
            this.cfg = cfg;
            Current = Mode.DiaADia;
            CurrentProfile = cfg.Daily;
        }

        public void Apply(Mode mode, string reason)
        {
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

            try { PowerSettings.Apply(profile, warnings); }
            catch (Exception ex) { warnings.Add(ex.Message); }

            if (Features.DpiControl)
            {
                try { DpiController.Apply(profile, warnings); }
                catch (Exception ex) { warnings.Add("DPI: " + ex.Message); }
            }

            Current = mode;
            CurrentProfile = profile;
            CurrentGameProfile = gameProfile;

            Log.Info("Modo " + (mode == Mode.Performance ? "Performance" : "Dia a dia") +
                     (gameProfile != null ? " (perfil de " + gameProfile + ")" : "") + " — " + reason);
            foreach (string w in warnings) Log.Warn(w);

            var handler = ModeChanged;
            if (handler != null) handler(mode, reason, warnings);
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
