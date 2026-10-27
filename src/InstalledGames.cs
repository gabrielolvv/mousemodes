using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MouseModes
{
    public class InstalledGame
    {
        public string Name;     // "Counter-Strike 2"
        public string Process;  // "cs2" (começo do nome do processo, como na lista de jogos)
        public string Store;    // "Steam" ou "Epic"
    }

    /// <summary>
    /// Acha jogos instalados pela Steam e pela Epic, para incluir na lista sem digitar o nome do processo.
    /// As lojas não dizem qual .exe é o jogo; o app escolhe o maior da pasta, sem desinstaladores, launchers etc.
    /// </summary>
    public static class InstalledGames
    {
        private const int MaxDepth = 5;
        private const int MaxFilesPerGame = 4000;

        // Executáveis que nunca são o jogo. Palavras curtas só no começo do nome ("crash" sozinho pegaria o Crash Bandicoot).
        private static readonly Regex NotTheGame = new Regex(
            @"^(unins|setup|install|dxsetup|dxwebsetup|dotnet|oalinst|physx|eac|be(service|launcher))|" +
            @"crashhandler|crashreport|crashpad|crashsender|crash_?reporter|bugreport|launcher|updater|uploader|helper|" +
            @"easyanticheat|battleye|prereq|redist|cefsharp|qtwebengine|touchup|cleanup",
            RegexOptions.IgnoreCase);

        // "chave" "valor" (VDF da Steam) e "chave": "valor" (JSON da Epic).
        private static readonly Regex VdfPair = new Regex("\"(\\w+)\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"");
        private static readonly Regex JsonPair = new Regex("\"(\\w+)\"\\s*:\\s*(?:\"((?:[^\"\\\\]|\\\\.)*)\"|(true|false))");

        /// <summary>Jogos da Steam e da Epic, sem repetir processo. Nunca lança exceção.</summary>
        public static List<InstalledGame> Find()
        {
            var all = new List<InstalledGame>();
            try
            {
                string steam = SteamPath();
                if (steam != null) all.AddRange(FromSteam(steam));
            }
            catch (Exception ex) { Log.Error("Procurar jogos da Steam", ex); }

            try
            {
                string epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Epic\EpicGamesLauncher\Data\Manifests");
                all.AddRange(FromEpic(epic));
            }
            catch (Exception ex) { Log.Error("Procurar jogos da Epic", ex); }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            all.RemoveAll(g => !seen.Add(g.Process));
            all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return all;
        }

        private static string SteamPath()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", false))
            {
                string path = k != null ? k.GetValue("SteamPath") as string : null; // vem com "/"
                return string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
            }
        }

        internal static List<InstalledGame> FromSteam(string steamPath)
        {
            var libraries = new List<string> { steamPath };
            string vdf = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in VdfPair.Matches(File.ReadAllText(vdf)))
                {
                    // Formato novo: "path" "D:\\SteamLibrary". Antigo: "1" "D:\\SteamLibrary".
                    string key = m.Groups[1].Value;
                    if (key == "path" || Regex.IsMatch(key, @"^\d+$"))
                    {
                        string lib = Unescape(m.Groups[2].Value);
                        if (Directory.Exists(lib) && !libraries.Exists(l => SamePath(l, lib))) libraries.Add(lib);
                    }
                }
            }

            var games = new List<InstalledGame>();
            foreach (string lib in libraries)
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (string acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    var values = Pairs(VdfPair, File.ReadAllText(acf));
                    string name, dir;
                    if (!values.TryGetValue("name", out name) || !values.TryGetValue("installdir", out dir)) continue;
                    // Ferramentas da própria Steam (Redistributables, Linux Runtime, SteamVR) não são jogos.
                    if (name.StartsWith("Steam", StringComparison.OrdinalIgnoreCase) || name.IndexOf("Redistributable", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    string exe = MainExecutable(Path.Combine(apps, "common", dir));
                    if (exe != null) games.Add(new InstalledGame { Name = name, Process = exe, Store = "Steam" });
                }
            }
            return games;
        }

        internal static List<InstalledGame> FromEpic(string manifestsDir)
        {
            var games = new List<InstalledGame>();
            if (!Directory.Exists(manifestsDir)) return games;
            foreach (string item in Directory.GetFiles(manifestsDir, "*.item"))
            {
                var values = Pairs(JsonPair, File.ReadAllText(item));
                string name, location, launch, incomplete;
                if (!values.TryGetValue("DisplayName", out name) || !values.TryGetValue("InstallLocation", out location)) continue;
                if (values.TryGetValue("bIsIncompleteInstall", out incomplete) && incomplete == "true") continue;

                // O LaunchExecutable muitas vezes é um launcher (Fortnite: FortniteLauncher); o maior .exe é o jogo.
                string exe = MainExecutable(location);
                if (exe == null && values.TryGetValue("LaunchExecutable", out launch) && launch.Length > 0)
                    exe = GameDetector.Normalize(Path.GetFileName(launch));
                if (!string.IsNullOrEmpty(exe)) games.Add(new InstalledGame { Name = name, Process = exe, Store = "Epic" });
            }
            return games;
        }

        /// <summary>Nome (sem .exe) do maior executável da pasta que não seja instalador, launcher etc., ou null.</summary>
        internal static string MainExecutable(string folder)
        {
            if (!Directory.Exists(folder)) return null;
            string best = null;
            long bestSize = -1;
            int seen = 0;
            var pending = new Stack<KeyValuePair<string, int>>();
            pending.Push(new KeyValuePair<string, int>(folder, 0));
            while (pending.Count > 0 && seen < MaxFilesPerGame)
            {
                KeyValuePair<string, int> dir = pending.Pop();
                try
                {
                    foreach (string exe in Directory.GetFiles(dir.Key, "*.exe"))
                    {
                        seen++;
                        string name = Path.GetFileNameWithoutExtension(exe);
                        if (NotTheGame.IsMatch(name)) continue;
                        long size = new FileInfo(exe).Length;
                        if (size > bestSize) { best = name; bestSize = size; }
                    }
                    if (dir.Value < MaxDepth)
                        foreach (string sub in Directory.GetDirectories(dir.Key))
                            pending.Push(new KeyValuePair<string, int>(sub, dir.Value + 1));
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            return best;
        }

        private static Dictionary<string, string> Pairs(Regex pattern, string text)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in pattern.Matches(text))
            {
                string key = m.Groups[1].Value;
                if (values.ContainsKey(key)) continue; // a primeira ocorrência é a do topo do arquivo
                values[key] = m.Groups[3].Success ? m.Groups[3].Value : Unescape(m.Groups[2].Value);
            }
            return values;
        }

        private static string Unescape(string s)
        {
            try { return Regex.Unescape(s); } catch { return s; }
        }

        private static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
