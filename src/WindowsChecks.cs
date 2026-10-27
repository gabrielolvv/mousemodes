using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace MouseModes
{
    public class WindowsCheck
    {
        public string Name;        // "Modo de Jogo"
        public string State;       // "Ligado"
        public bool? Ok;           // true = como recomendado, false = vale mudar, null = depende / padrão do Windows
        public string Advice;      // o que fazer (vazio quando já está bom)
        public string SettingsUri; // tela das Configurações do Windows que muda isto
    }

    /// <summary>
    /// Ajustes do Windows que pesam no jogo. O app só mostra e abre a tela certa das Configurações:
    /// não muda nada no registro (ver README, "O que realmente tira delay").
    /// </summary>
    public static class WindowsChecks
    {
        private const string GraphicsUri = "ms-settings:display-advancedgraphics";

        /// <summary>Lê tudo agora. Nunca lança exceção: o que não der para ler vira "não foi possível ler".</summary>
        public static List<WindowsCheck> Read()
        {
            int build = WindowsBuild();
            var checks = new List<WindowsCheck>
            {
                Safe(L.T("Modo de Jogo", "Game Mode"), () => GameMode(
                    HkcuValue(@"Software\Microsoft\GameBar", "AutoGameModeEnabled"))),
                Safe(L.T("Gravação em segundo plano", "Background recording"), () => BackgroundRecording(
                    HkcuValue(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled"), build)),
                Safe(L.T("Agendamento de GPU acelerado", "Hardware-accelerated GPU scheduling"), () => HardwareScheduling(
                    HklmValue(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode")))
            };
            if (build >= 22000)
            {
                checks.Add(Safe(L.T("Otimizações para jogos em janela", "Optimizations for windowed games"), () => WindowedOptimizations(
                    HkcuValue(@"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings") as string)));
            }
            return checks;
        }

        /// <summary>AutoGameModeEnabled: sem valor = ligado (padrão desde o Windows 10 1703).</summary>
        internal static WindowsCheck GameMode(object value)
        {
            bool on = !(value is int) || (int)value != 0;
            return new WindowsCheck
            {
                Name = L.T("Modo de Jogo", "Game Mode"),
                State = on ? L.T("Ligado", "On") : L.T("Desligado", "Off"),
                Ok = on,
                Advice = on ? "" : L.T("Ligue: o Windows dá prioridade ao jogo e segura atualizações durante a partida.", "Turn it on: Windows gives the game priority and holds back updates during a match."),
                SettingsUri = "ms-settings:gaming-gamemode"
            };
        }

        /// <summary>HistoricalCaptureEnabled = 1: o Windows grava os últimos minutos do jogo o tempo todo.</summary>
        internal static WindowsCheck BackgroundRecording(object value, int build)
        {
            bool on = value is int && (int)value != 0;
            return new WindowsCheck
            {
                Name = L.T("Gravação em segundo plano", "Background recording"),
                State = on ? L.T("Ligada", "On") : L.T("Desligada", "Off"),
                Ok = !on,
                Advice = on ? L.T("Desligue se não usa: ela grava o jogo o tempo todo e custa FPS.", "Turn it off if you do not use it: it records the game all the time and costs FPS.") : "",
                SettingsUri = build >= 22000 ? "ms-settings:gaming-captures" : "ms-settings:gaming-gamedvr"
            };
        }

        /// <summary>HwSchMode: 2 = ligado, 1 = desligado, sem valor = placa ou driver sem suporte.</summary>
        internal static WindowsCheck HardwareScheduling(object value)
        {
            var check = new WindowsCheck { Name = L.T("Agendamento de GPU acelerado", "Hardware-accelerated GPU scheduling"), SettingsUri = GraphicsUri };
            if (!(value is int))
            {
                check.State = L.T("Sem suporte", "Not supported");
                check.Advice = L.T("A placa de vídeo ou o driver não oferecem esta opção.", "The graphics card or driver does not offer this option.");
                return check;
            }
            bool on = (int)value == 2;
            check.State = on ? L.T("Ligado", "On") : L.T("Desligado", "Off");
            check.Ok = on ? true : (bool?)null;
            check.Advice = on ? "" : L.T("Em placas recentes, ligar costuma reduzir o atraso. Vale testar nos seus jogos; exige reiniciar.", "On recent cards, turning it on usually reduces delay. Worth testing in your games; needs a restart.");
            return check;
        }

        /// <summary>DirectXUserGlobalSettings: "SwapEffectUpgradeEnable=1;" liga; sem a chave vale o padrão do Windows.</summary>
        internal static WindowsCheck WindowedOptimizations(string settings)
        {
            var check = new WindowsCheck { Name = L.T("Otimizações para jogos em janela", "Optimizations for windowed games"), SettingsUri = GraphicsUri };
            string flag = Flag(settings, "SwapEffectUpgradeEnable");
            if (flag == "1")
            {
                check.State = L.T("Ligadas", "On");
                check.Ok = true;
                check.Advice = "";
            }
            else
            {
                check.State = flag == "0" ? L.T("Desligadas", "Off") : L.T("Padrão do Windows", "Windows default");
                check.Ok = flag == "0" ? false : (bool?)null;
                check.Advice = L.T("Ligue: diminui o atraso em jogos em janela ou sem borda.", "Turn them on: less delay in windowed and borderless games.");
            }
            return check;
        }

        /// <summary>Abre a tela das Configurações do Windows. Retorna a mensagem de erro, ou null.</summary>
        public static string OpenSettings(WindowsCheck check)
        {
            try
            {
                Process.Start(new ProcessStartInfo(check.SettingsUri) { UseShellExecute = true });
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Abrir " + check.SettingsUri, ex);
                return ex.Message;
            }
        }

        /// <summary>Valor de "Chave=valor;" numa lista separada por ponto e vírgula, ou null.</summary>
        private static string Flag(string settings, string key)
        {
            if (string.IsNullOrEmpty(settings)) return null;
            foreach (string part in settings.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && string.Equals(part.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return part.Substring(eq + 1).Trim();
            }
            return null;
        }

        private static WindowsCheck Safe(string name, Func<WindowsCheck> read)
        {
            try { return read(); }
            catch (Exception ex)
            {
                Log.Error("Ler " + name, ex);
                return new WindowsCheck { Name = name, State = L.T("Não foi possível ler", "Could not read"), Advice = "", SettingsUri = GraphicsUri };
            }
        }

        private static object HkcuValue(string key, string name)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key, false))
                return k != null ? k.GetValue(name) : null;
        }

        private static object HklmValue(string key, string name)
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(key, false))
                return k != null ? k.GetValue(name) : null;
        }

        private static int WindowsBuild()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                {
                    int build;
                    return k != null && int.TryParse(k.GetValue("CurrentBuild") as string, out build) ? build : 0;
                }
            }
            catch { return 0; }
        }
    }
}
