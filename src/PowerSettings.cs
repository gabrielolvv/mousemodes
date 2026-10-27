using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MouseModes
{
    /// <summary>Plano de energia e suspensão seletiva de USB, via powercfg (não precisa de admin).</summary>
    public static class PowerSettings
    {
        private const string BalancedGuid = "381b4222-f694-41f0-9685-aff5bb260d00";
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
        public static void Apply(MouseProfile p, List<string> warnings, string restorePlan = null)
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
                            Log.Info("Plano de energia anterior reativado (" + restorePlan + ")");
                        }
                        else if (!ActivateBalanced())
                        {
                            warnings.Add("O plano de energia anterior não existe mais e o Equilibrado também falhou (veja o Diagnóstico).");
                        }
                        else
                        {
                            Log.Warn("O plano de energia anterior (" + restorePlan + ") não existe mais; ativado o Equilibrado");
                        }
                        break;
                    case PowerPlanOption.Equilibrado:
                        if (!ActivateBalanced())
                            warnings.Add("Não foi possível ativar um plano Equilibrado neste PC (veja o Diagnóstico).");
                        break;
                    case PowerPlanOption.AltoDesempenho:
                        if (!ActivateFromTemplate(HighPerformanceGuid, HighCopyName, "Alto desempenho", "High performance"))
                            warnings.Add("Não foi possível ativar o plano Alto desempenho.");
                        break;
                    case PowerPlanOption.DesempenhoMaximo:
                        if (!ActivateFromTemplate(UltimateGuid, UltimateCopyName, "Desempenho M", "Ultimate Performance"))
                            warnings.Add("Este PC não aceitou o plano Desempenho máximo (comum em notebooks). Use Alto desempenho.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Plano de energia", ex);
                warnings.Add("Erro ao trocar o plano de energia: " + ex.Message);
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
                    warnings.Add("Não foi possível ajustar a suspensão seletiva de USB.");
            }
            catch (Exception ex)
            {
                Log.Error("Suspensão seletiva de USB", ex);
                warnings.Add("Erro ao ajustar o USB: " + ex.Message);
            }
        }

        /// <summary>
        /// Equilibrado: o padrão do Windows; se ele foi removido, um plano com esse nome;
        /// se não houver, recria a partir do modelo do Windows.
        /// </summary>
        private static bool ActivateBalanced()
        {
            if (ActivateFromTemplate(BalancedGuid, BalancedCopyName, "Equilibrado", "Balanced")) return true;

            string list;
            if (Run("/list", out list))
                Log.Warn("Planos de energia neste PC: " + list.Replace("\r", " ").Replace("\n", " ").Trim());
            return false;
        }

        /// <summary>
        /// Ativa um plano do Windows que pode estar oculto (Alto desempenho, Desempenho máximo):
        /// tenta direto, depois uma cópia criada antes pelo app, e por último cria a cópia.
        /// </summary>
        private static bool ActivateFromTemplate(string templateGuid, string copyName, params string[] knownNames)
        {
            string list;
            bool listed = Run("/list", out list);
            string[] lines = listed ? list.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];

            // 1) O plano original do Windows, se existir neste PC (evita o erro "parâmetros inválidos").
            if (!listed || ContainsGuid(lines, templateGuid))
            {
                if (Run("/setactive " + templateGuid)) return true;
            }

            // 2) Uma cópia criada antes pelo app, ou um plano com o nome conhecido (ex.: "Equilibrado").
            var names = new List<string>(knownNames);
            names.Insert(0, copyName);
            foreach (string name in names)
            {
                foreach (string line in lines)
                {
                    if (line.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Match existing = GuidRegex.Match(line);
                    if (existing.Success && Run("/setactive " + existing.Value))
                    {
                        Log.Info("Plano ativado pelo nome: " + line.Trim());
                        return true;
                    }
                }
            }

            string output;
            if (!Run("-duplicatescheme " + templateGuid, out output)) return false;
            Match created = GuidRegex.Match(output);
            if (!created.Success) return false;

            Run("/changename " + created.Value + " \"" + copyName + "\"");
            Log.Info("Plano \"" + copyName + "\" criado (" + created.Value + ")");
            return Run("/setactive " + created.Value);
        }

        private static bool ContainsGuid(string[] lines, string guid)
        {
            foreach (string line in lines)
                if (line.IndexOf(guid, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
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
            return Run("/getactivescheme", out output) ? output.Trim() : "(não foi possível ler)";
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
                        Log.Warn("powercfg " + args + " não respondeu em 10 s");
                        return false;
                    }
                    // Já saiu: as leituras terminam assim que o buffer for esvaziado.
                    if (!outRead.Wait(2000) || !errRead.Wait(2000))
                    {
                        Log.Warn("powercfg " + args + ": saída não terminou de chegar");
                        return false;
                    }
                    output = outRead.Result;
                    string error = errRead.Result;
                    if (proc.ExitCode != 0)
                    {
                        string detail = (error + " " + output).Trim().Replace("\r", " ").Replace("\n", " ");
                        if (detail.Length > 160) detail = detail.Substring(0, 160) + "…";
                        Log.Info("powercfg " + args + " → código " + proc.ExitCode + (detail.Length > 0 ? ": " + detail : ""));
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
