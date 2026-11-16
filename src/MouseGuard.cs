using System;
using System.Collections.Generic;

namespace MouseModes
{
    /// <summary>O que fazer depois de comparar o mouse do Windows com o do modo.</summary>
    public enum GuardAction
    {
        /// <summary>Está como o modo (ou o vigia já desistiu): nada a fazer.</summary>
        None,
        /// <summary>Outro programa mudou o mouse: aplicar de novo o do modo.</summary>
        Reapply,
        /// <summary>Mudaram de novo várias vezes seguidas: parar de brigar até a próxima troca de modo e avisar.</summary>
        GiveUp
    }

    /// <summary>
    /// Vigia do mouse no Performance: software de mouse, macros e sincronizações às vezes religam a aceleração ou mudam a
    /// velocidade no meio do jogo. A cada checagem (3 s) o app compara o mouse do Windows com o que o modo aplicou e, se
    /// mudou, aplica de novo. Se continuar mudando (MaxFixes vezes em FixWindow), desiste até a próxima troca de modo,
    /// para nunca ficar num cabo de guerra com outro programa. Só lê e decide: quem aplica é o ModeManager.
    /// </summary>
    public sealed class MouseGuard
    {
        internal const int MaxFixes = 3;
        internal static readonly TimeSpan FixWindow = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Programas de mouse que costumam mexer na velocidade ou na aceleração do Windows (começo do nome do processo) e
        /// o nome que a pessoa reconhece.
        /// </summary>
        internal static readonly string[,] MouseSoftware =
        {
            { "lghub", "Logitech G HUB" },
            { "logioptionsplus", "Logi Options+" },
            { "LogiOptions", "Logitech Options" },
            { "RazerAppEngine", "Razer Synapse" },
            { "Razer Synapse", "Razer Synapse" },
            { "RzSynapse", "Razer Synapse" },
            { "iCUE", "Corsair iCUE" },
            { "SteelSeriesGG", "SteelSeries GG" },
            { "SteelSeriesEngine", "SteelSeries Engine" },
            { "XMouseButtonControl", "X-Mouse Button Control" },
            { "NGENUITY", "HyperX NGENUITY" },
            { "Bloody7", "Bloody" },
            { "ArmouryCrate", "ASUS Armoury Crate" },
            { "AutoHotkey", "AutoHotkey" }
        };

        private readonly List<DateTime> fixes = new List<DateTime>();
        private bool gaveUp;

        /// <summary>Quantas vezes já aplicou de novo desde a última troca de modo (para avisar só na primeira).</summary>
        public int FixesSinceReset { get; private set; }

        /// <summary>Troca de modo (ou o modo aplicado de novo): começa do zero, inclusive depois de desistir.</summary>
        public void Reset()
        {
            fixes.Clear();
            gaveUp = false;
            FixesSinceReset = 0;
        }

        /// <param name="expected">O mouse que o modo aplicou (null fora do Performance: não vigia).</param>
        /// <param name="actual">O mouse do Windows agora.</param>
        public GuardAction Check(MouseProfile expected, MouseProfile actual, DateTime now)
        {
            if (expected == null || actual == null || Same(expected, actual) || gaveUp) return GuardAction.None;
            fixes.RemoveAll(t => now - t >= FixWindow || t > now);
            if (fixes.Count >= MaxFixes)
            {
                gaveUp = true;
                return GuardAction.GiveUp;
            }
            fixes.Add(now);
            FixesSinceReset++;
            return GuardAction.Reapply;
        }

        /// <summary>
        /// Velocidade, aceleração e rolagem do perfil (<paramref name="expected"/>, como o Windows guarda) iguais às do
        /// Windows (<paramref name="actual"/>). O resto do perfil não é do mouse do Windows.
        /// </summary>
        internal static bool Same(MouseProfile expected, MouseProfile actual)
        {
            return MouseSettings.ClampSpeed(expected.PointerSpeed) == actual.PointerSpeed &&
                   expected.EnhancePointerPrecision == actual.EnhancePointerPrecision &&
                   MouseSettings.NormalizeScroll(expected.ScrollLines) == actual.ScrollLines;
        }

        /// <summary>O que mudou: "aceleração ligada · velocidade 10 → 14".</summary>
        public static string Describe(MouseProfile expected, MouseProfile actual)
        {
            var parts = new List<string>();
            if (expected.EnhancePointerPrecision != actual.EnhancePointerPrecision)
                parts.Add(actual.EnhancePointerPrecision ? L.T("aceleração ligada", "acceleration turned on") : L.T("aceleração desligada", "acceleration turned off"));
            int speed = MouseSettings.ClampSpeed(expected.PointerSpeed), scroll = MouseSettings.NormalizeScroll(expected.ScrollLines);
            if (speed != actual.PointerSpeed)
                parts.Add(L.T("velocidade ", "speed ") + speed + " → " + actual.PointerSpeed);
            if (scroll != actual.ScrollLines)
                parts.Add(L.T("rolagem ", "scroll ") + MouseSettings.DescribeScroll(scroll) + " → " + MouseSettings.DescribeScroll(actual.ScrollLines));
            return string.Join(" · ", parts.ToArray());
        }

        /// <summary>Os programas de mouse abertos entre <paramref name="running"/> (nomes de processo), sem repetir.</summary>
        public static List<string> LikelyCulprits(IEnumerable<string> running)
        {
            var found = new List<string>();
            if (running == null) return found;
            foreach (string process in running)
            {
                if (string.IsNullOrEmpty(process)) continue;
                for (int i = 0; i < MouseSoftware.GetLength(0); i++)
                {
                    if (!process.StartsWith(MouseSoftware[i, 0], StringComparison.OrdinalIgnoreCase)) continue;
                    if (!found.Contains(MouseSoftware[i, 1])) found.Add(MouseSoftware[i, 1]);
                    break;
                }
            }
            return found;
        }

        /// <summary>O aviso depois de aplicar de novo: o que mudou, quem pode ter sido e como desligar.</summary>
        public static string FixedMessage(string what, List<string> culprits)
        {
            return L.T("Outro programa mudou o mouse no Performance (", "Another program changed the mouse in Performance (") + what +
                   L.T("). O app voltou ao do modo.", "). The app put the mode's back.") + Culprits(culprits) +
                   L.T(" Se foi você, desligue o vigia em Ajustes → Performance.", " If it was you, turn the watch off in Settings → Performance.");
        }

        /// <summary>O aviso ao desistir: continua mudando, e o app parou de brigar até a próxima troca de modo.</summary>
        public static string GaveUpMessage(string what, List<string> culprits)
        {
            return L.T("O mouse continua sendo mudado (", "The mouse keeps being changed (") + what +
                   L.T("): o app parou de aplicar de novo até a próxima troca de modo.", "): the app stopped reapplying until the next mode switch.") +
                   Culprits(culprits);
        }

        private static string Culprits(List<string> culprits)
        {
            if (culprits == null || culprits.Count == 0) return "";
            return L.T(" Pode ter sido: ", " It may have been: ") + string.Join(", ", culprits.ToArray()) + ".";
        }
    }
}
