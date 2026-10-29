using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MouseModes
{
    /// <summary>Registro do que o app fez e do que falhou. Fica em memória e em %APPDATA%\MouseModes\log.txt.</summary>
    public static class Log
    {
        private const int MaxMemory = 400;
        private const long MaxFileBytes = 256 * 1024;
        private static readonly object Gate = new object();
        private static readonly List<string> Lines = new List<string>();

        public static event Action Changed;

        /// <summary>Outro arquivo no lugar do log.txt (só os testes usam, para não escrever no log de verdade).</summary>
        internal static string PathOverride;

        public static string FilePath
        {
            get
            {
                return PathOverride ?? Path.Combine(Path.GetDirectoryName(AppConfig.FilePath), "log.txt");
            }
        }

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write(L.T("AVISO", "WARN"), message); }
        public static void Error(string message) { Write(L.T("ERRO", "ERROR"), message); }

        public static void Error(string context, Exception ex)
        {
            Write(L.T("ERRO", "ERROR"), context + ": " + ex.GetType().Name + " — " + ex.Message);
        }

        private static void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("dd/MM HH:mm:ss") + "  " + level.PadRight(5) + "  " + message;
            lock (Gate)
            {
                Lines.Add(line);
                if (Lines.Count > MaxMemory) Lines.RemoveRange(0, Lines.Count - MaxMemory);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxFileBytes)
                    {
                        // Guarda só a metade mais recente.
                        string[] old = File.ReadAllLines(FilePath, Encoding.UTF8);
                        var keep = new string[old.Length / 2];
                        Array.Copy(old, old.Length - keep.Length, keep, 0, keep.Length);
                        File.WriteAllLines(FilePath, keep, Encoding.UTF8);
                    }
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Sem disco ou sem permissão: segue só em memória.
                }
            }
            var handler = Changed;
            if (handler != null)
            {
                try { handler(); } catch { }
            }
        }

        /// <summary>Linhas desta sessão + o fim do arquivo de sessões anteriores.</summary>
        public static string ReadAll()
        {
            lock (Gate)
            {
                try
                {
                    if (File.Exists(FilePath))
                    {
                        string[] all = File.ReadAllLines(FilePath, Encoding.UTF8);
                        int start = Math.Max(0, all.Length - 600);
                        var sb = new StringBuilder();
                        for (int i = start; i < all.Length; i++) sb.AppendLine(all[i]);
                        return sb.ToString();
                    }
                }
                catch { }
                return string.Join(Environment.NewLine, Lines.ToArray());
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                Lines.Clear();
                try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
            }
            Info(L.T("Registro limpo.", "Log cleared."));
        }
    }
}
