using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MouseModes
{
    /// <summary>
    /// Atualização sozinha: baixa o instalador da Release do GitHub, confere e roda em silêncio.
    /// O instalador fecha o app (que restaura tudo), instala por cima e abre o app de novo na bandeja.
    ///
    /// Segurança: os links são montados aqui a partir da tag, só por HTTPS e só deste repositório; cada
    /// redirecionamento é conferido (github.com e os servidores de arquivos do GitHub). Antes de rodar, o arquivo
    /// precisa bater com o SHA-256 publicado na Release, ser um executável e dizer que é o instalador do MouseModes
    /// da versão esperada. A confiança é a mesma de baixar à mão pela página da Release.
    /// </summary>
    public static class Updater
    {
        internal const string Installer = "MouseModes-Setup.exe";
        internal const string DownloadBase = "https://github.com/gabrielolvv/mousemodes/releases/download/";
        internal const long MaxInstallerBytes = 20L * 1024 * 1024;
        private const int MaxRedirects = 5;

        /// <summary>Hosts aceitos no download (o GitHub redireciona os arquivos da Release para os próprios servidores).</summary>
        internal static readonly string[] AllowedHosts =
        {
            "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"
        };

        private static readonly Regex Sha256Line = new Regex(@"^\s*([0-9a-fA-F]{64})\s+\*?(\S+)\s*$", RegexOptions.Multiline);

        /// <summary>Só os testes mudam: a pasta do download vai para um lugar temporário.</summary>
        internal static string FolderOverride;

        /// <summary>Pasta do download (só deste PC). Apagada depois de atualizar.</summary>
        public static string Folder
        {
            get
            {
                return FolderOverride ??
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseModes", "update");
            }
        }

        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseModes";

        /// <summary>
        /// true se este exe é o instalado (a pasta que o instalador grava). Uma cópia solta (ex.: em Downloads) não se
        /// atualiza sozinha: o instalador gravaria na pasta instalada, e a cópia solta continuaria velha.
        /// </summary>
        public static bool IsInstalledCopy(string exePath)
        {
            string installed = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    if (key != null) installed = key.GetValue("InstallLocation") as string;
            }
            catch { }
            if (string.IsNullOrEmpty(installed))
                installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MouseModes");
            return SameFolder(Path.GetDirectoryName(exePath), installed);
        }

        internal static bool SameFolder(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static string AssetUrl(string tag, string file)
        {
            return DownloadBase + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(file);
        }

        internal static bool IsAllowed(Uri uri)
        {
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort) return false;
            foreach (string host in AllowedHosts)
                if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>O hash do arquivo pedido no formato do sha256sum ("hash  nome" ou "hash *nome"). null se não houver.</summary>
        internal static string ParseSha256(string content, string file)
        {
            if (content == null) return null;
            foreach (Match m in Sha256Line.Matches(content.Replace("\r", "")))
                if (string.Equals(m.Groups[2].Value, file, StringComparison.OrdinalIgnoreCase))
                    return m.Groups[1].Value.ToLowerInvariant();
            return null;
        }

        internal static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                var sb = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(stream)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Confere o instalador baixado. null se estiver tudo certo; senão, o motivo (vai para o registro).
        /// </summary>
        internal static string Verify(string path, string expectedSha256, Version expectedVersion)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 1024 || info.Length > MaxInstallerBytes)
                return L.T("tamanho inesperado", "unexpected size");

            var header = new byte[2];
            using (FileStream stream = File.OpenRead(path))
                if (stream.Read(header, 0, 2) != 2 || header[0] != 'M' || header[1] != 'Z')
                    return L.T("não é um executável", "not an executable");

            if (expectedSha256 == null || !string.Equals(Sha256Of(path), expectedSha256, StringComparison.OrdinalIgnoreCase))
                return L.T("SHA-256 diferente do publicado na Release", "SHA-256 differs from the one published in the Release");

            FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
            if (!string.Equals(version.ProductName, "MouseModes", StringComparison.Ordinal))
                return L.T("não é o instalador do MouseModes", "not the MouseModes installer");
            // O NSIS grava "1.5.0"; um exe do dotnet build grava "1.5.0+commit". Vale o número.
            string product = (version.ProductVersion ?? "").Split('+', ' ')[0];
            Version found;
            if (!Version.TryParse(product, out found) ||
                UpdateChecker.Normalize(found) != UpdateChecker.Normalize(expectedVersion))
                return L.T("versão do instalador diferente da Release", "installer version differs from the Release");
            return null;
        }

        /// <summary>
        /// Baixa e confere o instalador da release. Devolve o caminho dele, ou null (com o motivo no registro).
        /// Demora alguns segundos: chamar fora da thread da tela.
        /// </summary>
        public static string Download(ReleaseInfo release)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string folder = Folder;
                Clean();
                Directory.CreateDirectory(folder);

                string sums = Encoding.UTF8.GetString(Fetch(AssetUrl(release.Tag, Installer + ".sha256"), 4096));
                string expected = ParseSha256(sums, Installer);
                if (expected == null) return Refuse(release, L.T("a Release não tem o SHA-256 do instalador", "the Release has no installer SHA-256"));

                string path = Path.Combine(folder, "MouseModes-Setup-" + release.Version + ".exe");
                File.WriteAllBytes(path, Fetch(AssetUrl(release.Tag, Installer), MaxInstallerBytes));

                string problem = Verify(path, expected, release.Version);
                if (problem != null)
                {
                    try { File.Delete(path); } catch { }
                    return Refuse(release, problem);
                }
                Log.Info(L.T("Versão ", "Version ") + release.Tag + L.T(" baixada e conferida (SHA-256 ", " downloaded and checked (SHA-256 ") + expected + ")");
                return path;
            }
            catch (Exception ex)
            {
                return Refuse(release, ex.Message);
            }
        }

        /// <summary>Roda o instalador em silêncio. Ele mesmo fecha este app e abre a versão nova no fim.</summary>
        public static bool Install(string installer)
        {
            try
            {
                Process.Start(new ProcessStartInfo(installer, "/S /RELAUNCH") { UseShellExecute = false });
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(L.T("Rodar o instalador da versão nova", "Run the new version's installer"), ex);
                return false;
            }
        }

        /// <summary>Apaga o que sobrou de um download (depois de atualizar ou de uma tentativa que falhou).</summary>
        public static void Clean()
        {
            try { if (Directory.Exists(Folder)) Directory.Delete(Folder, true); } catch { }
        }

        private static string Refuse(ReleaseInfo release, string reason)
        {
            Log.Warn(L.T("Atualização para ", "Update to ") + release.Tag + L.T(" não instalada: ", " not installed: ") + reason.TrimEnd('.', ' ') +
                     L.T(". Dá para baixar à mão pela página da Release.", ". You can download it by hand from the Release page."));
            return null;
        }

        /// <summary>GET com os redirecionamentos conferidos um a um e um limite de tamanho.</summary>
        private static byte[] Fetch(string url, long maxBytes)
        {
            var uri = new Uri(url);
            for (int hop = 0; hop <= MaxRedirects; hop++)
            {
                if (!IsAllowed(uri)) throw new WebException(L.T("endereço fora do GitHub: ", "address outside GitHub: ") + uri.Host);
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.AllowAutoRedirect = false;
                request.UserAgent = "MouseModes/" + UpdateChecker.Current;
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;
                HttpWebResponse response;
                try { response = (HttpWebResponse)request.GetResponse(); }
                catch (WebException ex) when (ex.Response is HttpWebResponse) { response = (HttpWebResponse)ex.Response; }

                using (response)
                {
                    int code = (int)response.StatusCode;
                    if (code >= 300 && code < 400)
                    {
                        string location = response.Headers["Location"];
                        if (string.IsNullOrEmpty(location)) throw new WebException("HTTP " + code);
                        uri = new Uri(uri, location);
                        continue;
                    }
                    if (code != 200) throw new WebException("HTTP " + code);
                    if (response.ContentLength > maxBytes) throw new WebException(L.T("arquivo grande demais", "file too large"));

                    using (Stream input = response.GetResponseStream())
                    using (var buffer = new MemoryStream())
                    {
                        var chunk = new byte[81920];
                        int read;
                        while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            buffer.Write(chunk, 0, read);
                            if (buffer.Length > maxBytes) throw new WebException(L.T("arquivo grande demais", "file too large"));
                        }
                        return buffer.ToArray();
                    }
                }
            }
            throw new WebException(L.T("redirecionamentos demais", "too many redirects"));
        }
    }
}
