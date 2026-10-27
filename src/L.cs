using System;
using System.Globalization;

namespace MouseModes
{
    /// <summary>
    /// Idioma da interface: português ou inglês. Cada texto fica no código com as duas versões lado a lado,
    /// L.T("Ajustes", "Settings"): nenhum texto fica sem tradução e não precisa de arquivo de recursos (o build.sh
    /// compila só os .cs). As mensagens do Diagnóstico continuam em português.
    /// </summary>
    public static class L
    {
        public const string Portuguese = "pt";
        public const string English = "en";

        /// <summary>true = textos em inglês. Até Init ser chamado, fica em português.</summary>
        public static bool IsEnglish { get; private set; }

        /// <param name="configured">"pt", "en" ou null/vazio = seguir o idioma do Windows.</param>
        public static void Init(string configured)
        {
            IsEnglish = UseEnglish(configured, CultureInfo.CurrentUICulture);
        }

        /// <summary>Português se o Windows estiver em português (qualquer variante); senão, inglês.</summary>
        internal static bool UseEnglish(string configured, CultureInfo windows)
        {
            if (string.Equals(configured, Portuguese, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(configured, English, StringComparison.OrdinalIgnoreCase)) return true;
            return windows == null || !string.Equals(windows.TwoLetterISOLanguageName, "pt", StringComparison.OrdinalIgnoreCase);
        }

        public static string T(string portuguese, string english)
        {
            return IsEnglish ? english : portuguese;
        }
    }
}
