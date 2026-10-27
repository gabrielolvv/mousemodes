using System;
using System.IO;

namespace MouseModes.Tests
{
    /// <summary>Pasta temporária só deste teste, apagada no fim. O log do app também vai para ela.</summary>
    public sealed class TempFolder : IDisposable
    {
        public string Path { get; private set; }

        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MouseModes.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Log.PathOverride = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MouseModes.Tests", "log.txt");
        }

        public string File(string relative)
        {
            return System.IO.Path.Combine(Path, relative);
        }

        /// <summary>Cria um arquivo com o tamanho pedido (simula um .exe).</summary>
        public string Exe(string relative, int kb)
        {
            string full = File(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            System.IO.File.WriteAllBytes(full, new byte[kb * 1024]);
            return full;
        }

        public string Text(string relative, string content)
        {
            string full = File(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            System.IO.File.WriteAllText(full, content);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }
}
