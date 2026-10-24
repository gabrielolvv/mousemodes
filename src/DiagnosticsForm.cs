using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>Mostra o registro do app e um resumo do sistema, para entender o que falhou.</summary>
    public class DiagnosticsForm : GamerWindow
    {
        private readonly TextBox text;
        private readonly Label systemInfo;
        private readonly Action onLogChanged;

        public DiagnosticsForm() : base("Diagnóstico", true)
        {
            CloseOnEscape = true;
            ClientSize = new Size(620, 600);

            Controls.Add(new SectionLabel { Text = "Sistema", Location = new Point(20, 58), Size = new Size(580, 20) });
            systemInfo = Note("carregando...", 20, 80, 580, 92, Theme.Muted, 8.5f);
            systemInfo.Font = new Font("Consolas", 8.5f);
            Controls.Add(systemInfo);

            Controls.Add(new SectionLabel { Text = "Registro", Location = new Point(20, 178), Size = new Size(580, 20) });
            text = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                WordWrap = false,
                Font = new Font("Consolas", 8.5f)
            };
            Controls.Add(new FieldHost(text) { Location = new Point(20, 202), Size = new Size(580, 326), Accent = Theme.DailyB });

            var copy = new NeonButton { Text = "Copiar tudo", Location = new Point(20, 542), Size = new Size(184, 42) };
            var folder = new NeonButton { Text = "Abrir pasta", Location = new Point(218, 542), Size = new Size(184, 42) };
            var clear = new NeonButton { Text = "Limpar", Style = ButtonStyle.Danger, Location = new Point(416, 542), Size = new Size(184, 42) };
            copy.Click += (s, e) => CopyAll();
            folder.Click += (s, e) => OpenFolder();
            clear.Click += (s, e) => { Log.Clear(); Reload(); };
            Controls.Add(copy);
            Controls.Add(folder);
            Controls.Add(clear);

            onLogChanged = () =>
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    try { BeginInvoke((Action)Reload); } catch { }
                }
            };
            Log.Changed += onLogChanged;

            FinishLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Reload();
            // O resumo chama o powercfg; faz depois de mostrar a janela para não travar a abertura.
            BeginInvoke((Action)(() => systemInfo.Text = BuildSystemInfo()));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Log.Changed -= onLogChanged;
            base.OnFormClosed(e);
        }

        private void Reload()
        {
            text.Text = Log.ReadAll();
            text.SelectionStart = text.TextLength;
            text.ScrollToCaret();
        }

        private static string BuildSystemInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Windows: " + WindowsVersion() + (Environment.Is64BitOperatingSystem ? " (64 bits)" : ""));
            sb.AppendLine("MouseModes " + typeof(DiagnosticsForm).Assembly.GetName().Version + "  ·  .NET " + Environment.Version);
            sb.AppendLine(Safe(() =>
            {
                MouseProfile now = MouseSettings.Read();
                return "Mouse agora: velocidade " + now.PointerSpeed + "/20, aceleração " +
                       (now.EnhancePointerPrecision ? "ligada" : "desligada") + ", rolagem " + now.ScrollLines;
            }, "Mouse"));
            sb.AppendLine(Safe(() =>
            {
                string plan = PowerSettings.ActivePlanDescription();
                int colon = plan.IndexOf(':');
                return "Plano: " + (colon >= 0 ? plan.Substring(colon + 1).Trim() : plan);
            }, "Plano"));
            sb.Append(Safe(() =>
            {
                var monitors = Displays.Scan();
                return "Monitores: " + (monitors.Count == 0 ? "não lidos" :
                    string.Join("  ·  ", monitors.ConvertAll(m => m.Label + " " + m.Width + "x" + m.Height + " " + m.CurrentHz + "/" + m.MaxHz + " Hz").ToArray()));
            }, "Monitores"));
            return sb.ToString();
        }

        /// <summary>Environment.OSVersion mente ("6.2") sem manifesto; o registro diz a versão real.</summary>
        private static string WindowsVersion()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        string name = k.GetValue("ProductName") as string ?? "Windows";
                        string build = k.GetValue("CurrentBuild") as string ?? "";
                        string display = k.GetValue("DisplayVersion") as string ?? "";
                        int b;
                        // Windows 11 continua com "Windows 10" no ProductName; o build 22000+ é Windows 11.
                        if (int.TryParse(build, out b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
                        return name + (display.Length > 0 ? " " + display : "") + (build.Length > 0 ? " (build " + build + ")" : "");
                    }
                }
            }
            catch { }
            return Environment.OSVersion.VersionString;
        }

        private static string Safe(Func<string> read, string label)
        {
            try { return read(); }
            catch (Exception ex) { return label + ": não foi possível ler (" + ex.GetType().Name + ")"; }
        }

        private void CopyAll()
        {
            try
            {
                Clipboard.SetText(systemInfo.Text + Environment.NewLine + Environment.NewLine + text.Text);
                Log.Info("Diagnóstico copiado para a área de transferência.");
            }
            catch (Exception ex)
            {
                Log.Error("Copiar diagnóstico", ex);
            }
        }

        private static void OpenFolder()
        {
            try
            {
                string dir = Path.GetDirectoryName(Log.FilePath);
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Abrir pasta do registro", ex);
            }
        }
    }
}
