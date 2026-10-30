using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MouseModes
{
    /// <summary>Plano de energia e suspensão seletiva de USB, via powercfg (não precisa de admin).</summary>
    public static class PowerSettings
    {
        private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
        private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private const string UsbSubgroupGuid = "2a737441-1930-4402-8d77-b2bebba308a3";
        private const string UsbSelectiveSuspendGuid = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
        private const string UltimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        // Nomes ASCII de propósito: o powercfg escreve na página de código do console e acentos voltam trocados.
        private const string HighCopyName = "MouseModes - Alto Desempenho";
        private const string UltimateCopyName = "MouseModes - Desempenho Maximo";
        private const string BalancedCopyName = "MouseModes - Equilibrado";

        private static readonly Regex GuidRegex =
            new Regex(@"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");

        /// <param name="restorePlan">
        /// Com "MANTER/ANTERIOR": GUID de um plano para reativar (o que estava antes do Performance), ou null para não mexer.
        /// </param>
        public static void Apply(MouseProfile p, List<string> warnings, string restorePlan = null, AppConfig store = null)
        {
            // Cada parte isolada: se o plano falhar, o USB ainda é ajustado (e vice-versa).
            try
            {
                switch (p.PowerPlan)
                {
                    case PowerPlanOption.NaoAlterar:
                        if (restorePlan == null) break;
                        if (Run("/setactive " + restorePlan))
                        {
                            Log.Info(L.T("Plano de energia anterior reativado (", "Previous power plan reactivated (") + restorePlan + ")");
                        }
                        else if (!ActivateBalanced(store))
                        {
                            warnings.Add(L.T("O plano de energia anterior não existe mais e o Equilibrado também falhou (veja o Diagnóstico).", "The previous power plan no longer exists and Balanced also failed (see Diagnostics)."));
                        }
                        else
                        {
                            Log.Warn(L.T("O plano de energia anterior (", "The previous power plan (") + restorePlan + L.T(") não existe mais; ativado o Equilibrado", ") no longer exists; Balanced turned on"));
                        }
                        break;
                    case PowerPlanOption.Equilibrado:
                        if (!ActivateBalanced(store))
                            warnings.Add(L.T("Não foi possível ativar um plano Equilibrado neste PC (veja o Diagnóstico).", "Could not turn on a Balanced plan on this PC (see Diagnostics)."));
                        break;
                    // Sem o plano pedido (comum em notebooks novos, que só têm o Equilibrado), fica o Equilibrado: no
                    // Performance o modo de energia dele vai para Melhor desempenho (PowerModeKeeper).
                    case PowerPlanOption.AltoDesempenho:
                        if (!ActivateFromTemplate(HighPerformanceGuid, HighCopyName, store, "Alto desempenho", "High performance"))
                            warnings.Add(ActivateBalanced(store)
                                ? L.T("Este PC não aceitou o plano Alto desempenho; ficou o Equilibrado com o modo de energia em Melhor desempenho.", "This PC did not accept the High performance plan; Balanced is on, with the Best performance power mode.")
                                : L.T("Não foi possível ativar o plano Alto desempenho.", "Could not turn on the High performance plan."));
                        break;
                    case PowerPlanOption.DesempenhoMaximo:
                        if (!ActivateFromTemplate(UltimateGuid, UltimateCopyName, store, "Desempenho M", "Ultimate Performance"))
                            warnings.Add(ActivateBalanced(store)
                                ? L.T("Este PC não aceitou o plano Desempenho máximo (comum em notebooks); ficou o Equilibrado com o modo de energia em Melhor desempenho.", "This PC did not accept the Ultimate Performance plan (common on laptops); Balanced is on, with the Best performance power mode.")
                                : L.T("Este PC não aceitou o plano Desempenho máximo (comum em notebooks). Use Alto desempenho.", "This PC did not accept the Ultimate Performance plan (common on laptops). Use High performance."));
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(L.T("Plano de energia", "Power plan"), ex);
                warnings.Add(L.T("Erro ao trocar o plano de energia: ", "Error changing the power plan: ") + ex.Message);
            }

            try
            {
                // 0 = desativada (USB sempre acordado), 1 = ativada (padrão do Windows).
                string value = p.DisableUsbSelectiveSuspend ? "0" : "1";
                string setting = " SCHEME_CURRENT " + UsbSubgroupGuid + " " + UsbSelectiveSuspendGuid + " " + value;
                bool ok = Run("/setacvalueindex" + setting)
                          & Run("/setdcvalueindex" + setting)
                          & Run("/setactive SCHEME_CURRENT");
                if (!ok)
                    warnings.Add(L.T("Não foi possível ajustar a suspensão seletiva de USB.", "Could not set USB selective suspend."));
            }
            catch (Exception ex)
            {
                Log.Error(L.T("Suspensão seletiva de USB", "USB selective suspend"), ex);
                warnings.Add(L.T("Erro ao ajustar o USB: ", "Error setting USB: ") + ex.Message);
            }
        }

        /// <summary>
        /// Equilibrado: o padrão do Windows; se ele foi removido, um plano com esse nome;
        /// se não houver, recria a partir do modelo do Windows.
        /// </summary>
        private static bool ActivateBalanced(AppConfig store)
        {
            if (ActivateFromTemplate(BalancedGuid, BalancedCopyName, store, "Equilibrado", "Balanced")) return true;

            var described = new List<string>();
            foreach (KeyValuePair<string, string> plan in Plans()) described.Add(plan.Value + " (" + plan.Key + ")");
            Log.Warn(L.T("Planos de energia neste PC: ", "Power plans on this PC: ") + string.Join(", ", described.ToArray()));
            return false;
        }

        /// <summary>
        /// Ativa um plano do Windows que pode estar oculto (Alto desempenho, Desempenho máximo): tenta o original,
        /// depois a cópia guardada no config, depois um plano com o nome conhecido, e por último cria a cópia.
        /// </summary>
        /// <param name="store">Onde guardar o GUID da cópia criada (null = não guarda).</param>
        private static bool ActivateFromTemplate(string templateGuid, string copyName, AppConfig store, params string[] knownNames)
        {
            Dictionary<string, string> plans = Plans();

            // 1) O plano original do Windows, se existir neste PC (evita o erro "parâmetros inválidos").
            if (plans.Count == 0 || plans.ContainsKey(templateGuid))
            {
                if (Run("/setactive " + templateGuid)) return true;
            }

            // 2) A cópia que o app criou antes.
            string saved = store != null ? store.FindPlanCopy(templateGuid) : null;
            if (saved != null)
            {
                if (Run("/setactive " + saved)) return true;
                Log.Info(L.T("A cópia guardada do plano (", "The saved plan copy (") + saved + L.T(") não existe mais", ") no longer exists"));
                store.RememberPlanCopy(templateGuid, null);
            }

            // 3) Um plano com o nome da cópia ou do original (ex.: "Equilibrado").
            var names = new List<string>(knownNames);
            names.Insert(0, copyName);
            foreach (string name in names)
            {
                foreach (KeyValuePair<string, string> plan in plans)
                {
                    if (plan.Value.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!Run("/setactive " + plan.Key)) continue;
                    Log.Info(L.T("Plano ativado pelo nome: ", "Plan turned on by name: ") + plan.Value + " (" + plan.Key + ")");
                    if (store != null && name == copyName) store.RememberPlanCopy(templateGuid, plan.Key);
                    return true;
                }
            }

            // 4) Cria a cópia e guarda o GUID: em alguns PCs o /list não mostra a cópia, e sem isso ela seria recriada.
            string output;
            if (!Run("-duplicatescheme " + templateGuid, out output)) return false;
            Match created = GuidRegex.Match(output);
            if (!created.Success) return false;

            string guid = created.Value.ToLowerInvariant();
            Run("/changename " + guid + " \"" + copyName + "\"");
            Log.Info(L.T("Plano ", "Plan ") + "\"" + copyName + "\"" + L.T(" criado (", " created (") + guid + ")");
            if (store != null) store.RememberPlanCopy(templateGuid, guid);
            return Run("/setactive " + guid);
        }

        /// <summary>
        /// Planos de energia deste PC (GUID → nome). O registro mostra também os que o "powercfg /list" esconde
        /// em alguns PCs (inclusive os modelos do Windows e as cópias do app). Se não der para ler, usa o /list.
        /// </summary>
        internal static Dictionary<string, string> Plans()
        {
            var plans = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", false))
                {
                    if (root != null)
                    {
                        foreach (string sub in root.GetSubKeyNames())
                        {
                            Guid parsed;
                            if (!Guid.TryParse(sub, out parsed)) continue;
                            string name;
                            using (RegistryKey scheme = root.OpenSubKey(sub, false))
                                name = scheme != null ? scheme.GetValue("FriendlyName") as string ?? "" : "";
                            // Planos do Windows: "@...powrprof.dll,-15,Balanced". O texto depois da última vírgula é o nome.
                            int comma = name.LastIndexOf(',');
                            if (name.StartsWith("@") && comma >= 0) name = name.Substring(comma + 1);
                            // "Overlays" são os modos de energia do Windows, não planos para ativar.
                            if (name.IndexOf("Overlay", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            plans[parsed.ToString()] = name;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(L.T("Ler planos de energia do registro", "Read power plans from the registry"), ex);
            }
            if (plans.Count > 0) return plans;

            string list;
            if (Run("/list", out list))
            {
                foreach (string line in list.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Match m = GuidRegex.Match(line);
                    if (m.Success) plans[m.Value.ToLowerInvariant()] = line.Trim();
                }
            }
            return plans;
        }

        /// <summary>GUID do plano de energia ativo agora, ou null se não deu para ler.</summary>
        public static string ActivePlanGuid()
        {
            string output;
            if (!Run("/getactivescheme", out output)) return null;
            Match m = GuidRegex.Match(output);
            return m.Success ? m.Value.ToLowerInvariant() : null;
        }

        /// <summary>Nome do plano de energia ativo agora (para o Diagnóstico).</summary>
        public static string ActivePlanDescription()
        {
            string output;
            return Run("/getactivescheme", out output) ? output.Trim() : L.T("(não foi possível ler)", "(could not read)");
        }

        /// <summary>Padrão do Windows: Equilibrado + suspensão seletiva de USB ligada.</summary>
        public static void RestoreWindowsDefaults(List<string> warnings)
        {
            Apply(new MouseProfile { PowerPlan = PowerPlanOption.Equilibrado, DisableUsbSelectiveSuspend = false }, warnings);
        }

        private static bool Run(string args)
        {
            string ignored;
            return Run(args, out ignored);
        }

        private static bool Run(string args, out string output)
        {
            output = string.Empty;
            try
            {
                var psi = new ProcessStartInfo("powercfg.exe", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                // powercfg escreve na página de código do console (850 no Brasil): sem isto os acentos viram lixo.
                try
                {
                    var oem = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                    psi.StandardOutputEncoding = oem;
                    psi.StandardErrorEncoding = oem;
                }
                catch { }
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return false;
                    // Lê as duas saídas em paralelo: um ReadToEnd síncrono só volta quando o processo termina
                    // (o timeout abaixo nunca valeria) e pode travar se a outra saída encher o buffer.
                    var outRead = proc.StandardOutput.ReadToEndAsync();
                    var errRead = proc.StandardError.ReadToEndAsync();
                    if (!proc.WaitForExit(10000))
                    {
                        try { proc.Kill(); } catch { }
                        Log.Warn("powercfg " + args + L.T(" não respondeu em 10 s", " did not respond within 10 s"));
                        return false;
                    }
                    // Já saiu: as leituras terminam assim que o buffer for esvaziado.
                    if (!outRead.Wait(2000) || !errRead.Wait(2000))
                    {
                        Log.Warn("powercfg " + args + L.T(": saída não terminou de chegar", ": output did not finish arriving"));
                        return false;
                    }
                    output = outRead.Result;
                    string error = errRead.Result;
                    if (proc.ExitCode != 0)
                    {
                        string detail = (error + " " + output).Trim().Replace("\r", " ").Replace("\n", " ");
                        if (detail.Length > 160) detail = detail.Substring(0, 160) + "…";
                        Log.Info("powercfg " + args + L.T(" → código ", " → code ") + proc.ExitCode + (detail.Length > 0 ? ": " + detail : ""));
                    }
                    return proc.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error("powercfg " + args, ex);
                return false;
            }
        }
    }
}
