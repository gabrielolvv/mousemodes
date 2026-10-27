using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MouseModes
{
    public class ReleaseInfo
    {
        public string Tag;      // "v1.2.0"
        public Version Version; // 1.2.0
        public string Url;      // página da release no GitHub
    }

    /// <summary>
    /// Procura a versão mais nova na página de releases do GitHub. Só faz o pedido (GET, sem dado nenhum do PC)
    /// à API pública; o link do aviso é montado aqui, nunca copiado da resposta.
    /// </summary>
    public static class UpdateChecker
    {
        private const string LatestApi = "https://api.github.com/repos/gabrielolvv/mousemodes/releases/latest";
        internal const string ReleasesPage = "https://github.com/gabrielolvv/mousemodes/releases/";

        private static readonly Regex TagName = new Regex("\"tag_name\"\\s*:\\s*\"(v?[0-9]+(?:\\.[0-9]+){1,3})\"");

        /// <summary>Versão deste exe, com três partes (1.1.1).</summary>
        public static Version Current
        {
            get { return Normalize(typeof(UpdateChecker).Assembly.GetName().Version); }
        }

        /// <summary>A release mais recente, ou null sem internet, com o GitHub fora ou resposta estranha.</summary>
        public static ReleaseInfo FetchLatest()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; // GitHub não aceita TLS antigo
                var request = (HttpWebRequest)WebRequest.Create(LatestApi);
                request.UserAgent = "MouseModes/" + Current; // a API do GitHub exige User-Agent
                request.Accept = "application/vnd.github+json";
                request.Timeout = 10000;
                request.ReadWriteTimeout = 10000;
                using (WebResponse response = request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    ReleaseInfo release = Parse(reader.ReadToEnd());
                    if (release == null) Log.Warn("Resposta do GitHub sem versão reconhecível");
                    return release;
                }
            }
            catch (Exception ex)
            {
                Log.Info("Não foi possível procurar versão nova: " + ex.Message);
                return null;
            }
        }

        /// <summary>Lê "tag_name" da resposta da API ("v1.2.0"). Null se não houver uma versão válida.</summary>
        internal static ReleaseInfo Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            Match m = TagName.Match(json);
            Version version;
            if (!m.Success || !Version.TryParse(m.Groups[1].Value.TrimStart('v'), out version)) return null;
            string tag = m.Groups[1].Value;
            return new ReleaseInfo { Tag = tag, Version = Normalize(version), Url = ReleaseUrl(tag) };
        }

        internal static string ReleaseUrl(string tag)
        {
            return ReleasesPage + "tag/" + Uri.EscapeDataString(tag);
        }

        public static bool IsNewer(Version latest, Version current)
        {
            return latest != null && current != null && Normalize(latest) > Normalize(current);
        }

        /// <summary>1.2 vira 1.2.0 e 1.1.1.0 vira 1.1.1: o exe tem quatro partes e as tags, três.</summary>
        internal static Version Normalize(Version v)
        {
            if (v == null) return null;
            return new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
        }

        /// <summary>Só abre links da página de releases do próprio app.</summary>
        internal static bool IsReleaseLink(string url)
        {
            return url != null && url.StartsWith(ReleasesPage, StringComparison.Ordinal) && url.IndexOfAny(new[] { ' ', '"', '\r', '\n' }) < 0;
        }
    }
}
