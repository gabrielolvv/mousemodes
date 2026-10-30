using System;
using System.Runtime.InteropServices;

namespace MouseModes
{
    /// <summary>Quem lê e troca o modo de energia do Windows de verdade. Os testes usam um de mentira.</summary>
    public interface IPowerMode
    {
        /// <summary>O modo de energia em uso agora (para a fonte atual, tomada ou bateria). null se não deu para ler.</summary>
        Guid? Get();

        bool Set(Guid mode);

        /// <summary>Ligado na tomada (ou sem bateria).</summary>
        bool OnAC { get; }
    }

    /// <summary>
    /// Modo de energia do Windows (Configurações → Sistema → Energia, o controle do ícone da bateria): um ajuste por cima
    /// do plano Equilibrado. Com o plano Equilibrado no Performance (escolhido, ou porque o Alto/Máximo falhou), ele vai
    /// para Melhor desempenho na tomada e volta ao de antes ao sair. Com outro plano ativo o Windows ignora o modo, e na
    /// bateria o app não mexe (gastaria a carga). Se o app cair, volta ao abrir de novo, na próxima vez na tomada.
    /// </summary>
    public class PowerModeKeeper
    {
        // Os modos do controle (learn.microsoft.com, "Customize the Windows performance power slider").
        public static readonly Guid BestEfficiency = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid Balanced = Guid.Empty;
        public static readonly Guid BetterPerformance = new Guid("3af9b8d9-7c97-431d-ad78-34a8bfea439f");
        public static readonly Guid BestPerformance = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

        private readonly AppConfig cfg;
        private readonly Action save;
        private readonly IPowerMode windows;
        private bool failed; // o Windows não deixou: não tenta de novo a cada 3 s até sair do Performance

        public PowerModeKeeper(AppConfig cfg, Action save = null, IPowerMode windows = null)
        {
            this.cfg = cfg;
            this.save = save ?? cfg.Save;
            this.windows = windows ?? new WindowsPowerMode();
        }

        public bool OnAC { get { return windows.OnAC; } }

        public static string Name(Guid mode)
        {
            if (mode == BestEfficiency) return L.T("Melhor eficiência de energia", "Best power efficiency");
            if (mode == BestPerformance) return L.T("Melhor desempenho", "Best performance");
            if (mode == Balanced || mode == BetterPerformance) return L.T("Equilibrado", "Balanced");
            return mode.ToString();
        }

        /// <summary>
        /// Chamado a cada checagem de jogos e ao trocar de modo. wantBest: Performance com um plano escolhido (não
        /// MANTER). Só mexe no Windows quando o pedido muda.
        /// </summary>
        public void Ensure(bool wantBest)
        {
            bool onAC = windows.OnAC;
            if (wantBest && onAC)
            {
                if (cfg.PowerModeBackup != null || failed) return;
                Guid? now = windows.Get();
                if (now == null)
                {
                    failed = true;
                    return;
                }
                if (now.Value == BestPerformance) return; // já está: nada a devolver depois
                if (!windows.Set(BestPerformance))
                {
                    failed = true;
                    Log.Warn(L.T("O Windows não aceitou o modo de energia Melhor desempenho", "Windows did not accept the Best performance power mode"));
                    return;
                }
                cfg.PowerModeBackup = now.Value.ToString();
                Save();
                Log.Info(L.T("Modo de energia: Melhor desempenho (era ", "Power mode: Best performance (was ") + Name(now.Value) + ")");
                return;
            }

            if (!wantBest) failed = false;
            // Na bateria o Windows usa o modo da bateria: o da tomada volta quando a tomada voltar.
            if (cfg.PowerModeBackup == null || !onAC) return;
            Restore();
        }

        /// <summary>Devolve o modo de energia de antes, se o app trocou e o PC está na tomada.</summary>
        public void Restore()
        {
            if (cfg.PowerModeBackup == null || !windows.OnAC) return;
            Guid previous;
            if (Guid.TryParse(cfg.PowerModeBackup, out previous))
            {
                // Se a pessoa mudou o modo durante o jogo, fica o que ela escolheu.
                Guid? now = windows.Get();
                if (now == BestPerformance && windows.Set(previous))
                    Log.Info(L.T("Modo de energia de volta: ", "Power mode back to ") + Name(previous));
            }
            cfg.PowerModeBackup = null;
            Save();
        }

        private void Save()
        {
            try { save(); } catch (Exception ex) { Log.Error(L.T("Salvar configuração", "Save settings"), ex); }
        }
    }

    /// <summary>O modo de energia de verdade, pelo powrprof (as mesmas funções que a tela de Energia usa).</summary>
    internal sealed class WindowsPowerMode : IPowerMode
    {
        [DllImport("powrprof.dll")]
        private static extern uint PowerGetEffectiveOverlayScheme(out Guid mode);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid mode);

        public Guid? Get()
        {
            try
            {
                Guid mode;
                return PowerGetEffectiveOverlayScheme(out mode) == 0 ? mode : (Guid?)null;
            }
            catch (EntryPointNotFoundException) { return null; } // Windows antigo, sem o controle
            catch (DllNotFoundException) { return null; }
        }

        public bool Set(Guid mode)
        {
            try { return PowerSetActiveOverlayScheme(mode) == 0; }
            catch (EntryPointNotFoundException) { return false; }
            catch (DllNotFoundException) { return false; }
        }

        public bool OnAC { get { return PowerSource.OnAC; } }
    }

    /// <summary>Tomada ou bateria, pelo GetSystemPowerStatus.</summary>
    public static class PowerSource
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;      // 0 = bateria, 1 = tomada, 255 = desconhecido
            public byte BatteryFlag;       // 128 = sem bateria
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

        /// <summary>Na tomada, sem bateria ou sem saber: só "não" quando o Windows diz que está na bateria.</summary>
        public static bool OnAC
        {
            get
            {
                SystemPowerStatus status;
                return !GetSystemPowerStatus(out status) || status.ACLineStatus != 0;
            }
        }

        /// <summary>Carga da bateria (0–100), ou -1 sem bateria ou sem saber.</summary>
        public static int BatteryPercent
        {
            get
            {
                SystemPowerStatus status;
                if (!GetSystemPowerStatus(out status) || status.BatteryFlag == 128 || status.BatteryLifePercent > 100) return -1;
                return status.BatteryLifePercent;
            }
        }
    }
}
