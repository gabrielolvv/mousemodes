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

        public DiagnosticsForm() : base(L.T("Diagnóstico", "Diagnostics"), true)
        {
            CloseOnEscape = true;

            Controls.Add(new SectionLabel { Text = L.T("Sistema", "System"), Location = new Point(20, 58), Size = new Size(580, 20) });
            systemInfo = Note(L.T("carregando...", "loading..."), 20, 80, 580, 92, Theme.Muted, 8.5f);
            systemInfo.Font = new Font("Consolas", 8.5f);
            Controls.Add(systemInfo);

            Controls.Add(new SectionLabel { Text = L.T("Windows para jogos", "Windows for games"), Location = new Point(20, 178), Size = new Size(580, 20) });
            int y = 202;
            foreach (WindowsCheck check in WindowsChecks.Read())
            {
                AddCheckRow(check, y);
                y += 46;
            }
            Controls.Add(Note(L.T("O MouseModes só mostra: \"Abrir\" leva à tela do Windows onde você muda.", "MouseModes only shows these: \"Open\" takes you to the Windows page where you change them."), 20, y, 580, 18, Theme.Faint, 8f));
            y += 26; // a seção tem 3 linhas no Windows 10 e 4 no 11: o resto vem logo abaixo dela

            Controls.Add(new SectionLabel { Text = L.T("Registro", "Log"), Location = new Point(20, y), Size = new Size(580, 20) });
            text = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                WordWrap = false,
                Font = new Font("Consolas", 8.5f)
            };
            // Registro mais baixo que antes: com a seção nova, a janela ainda cabe numa tela de 768 px com a barra de tarefas.
            Controls.Add(new FieldHost(text) { Location = new Point(20, y + 24), Size = new Size(580, 210), Accent = Theme.DailyB });
            int buttonsY = y + 24 + 210 + 14;

            var copy = new NeonButton { Text = L.T("Copiar tudo", "Copy all"), Location = new Point(20, buttonsY), Size = new Size(184, 42) };
            var folder = new NeonButton { Text = L.T("Abrir pasta", "Open folder"), Location = new Point(218, buttonsY), Size = new Size(184, 42) };
            var clear = new NeonButton { Text = L.T("Limpar", "Clear"), Style = ButtonStyle.Danger, Location = new Point(416, buttonsY), Size = new Size(184, 42) };
            ClientSize = new Size(620, buttonsY + 42 + 16);
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

        /// <summary>Uma linha: nome, estado (ciano = bom, laranja = vale mudar, cinza = depende) e o botão Abrir.</summary>
        private void AddCheckRow(WindowsCheck check, int y)
        {
            Color stateColor = check.Ok == true ? Theme.DailyB : check.Ok == false ? Theme.PerfB : Theme.Muted;

            var name = Note(check.Name, 20, y, 250, 20, Theme.Text, 9.5f);
            name.Font = Theme.Ui(9.5f, FontStyle.Bold);
            var state = Note(check.State, 280, y, 220, 20, stateColor, 9.5f);
            state.Font = Theme.Ui(9.5f, FontStyle.Bold);
            var advice = Note(string.IsNullOrEmpty(check.Advice) ? L.T("Como recomendado.", "As recommended.") : check.Advice, 20, y + 20, 480, 18, Theme.Faint, 8f);

            var open = new NeonButton { Text = L.T("Abrir", "Open"), Location = new Point(510, y + 4), Size = new Size(90, 32), AccentA = Theme.DailyA, AccentB = Theme.DailyB };
            open.Click += (s, e) =>
            {
                string error = WindowsChecks.OpenSettings(check);
                if (error != null) ConfirmDialog.Inform(this, L.T("Não foi possível abrir", "Could not open"), error);
            };

            Controls.Add(name);
            Controls.Add(state);
            Controls.Add(advice);
            Controls.Add(open);
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
                return L.T("Mouse agora: velocidade ", "Mouse now: speed ") + now.PointerSpeed + L.T("/20, aceleração ", "/20, acceleration ") +
                       (now.EnhancePointerPrecision ? L.T("ligada", "on") : L.T("desligada", "off")) + L.T(", rolagem ", ", scroll ") + now.ScrollLines;
            }, "Mouse"));
            sb.AppendLine(Safe(() =>
            {
                string plan = PowerSettings.ActivePlanDescription();
                int colon = plan.IndexOf(':');
                return L.T("Plano: ", "Plan: ") + (colon >= 0 ? plan.Substring(colon + 1).Trim() : plan);
            }, L.T("Plano", "Plan")));
            sb.Append(Safe(() =>
            {
                var monitors = Displays.Scan();
                return L.T("Monitores: ", "Monitors: ") + (monitors.Count == 0 ? L.T("não lidos", "not read") :
                    string.Join("  ·  ", monitors.ConvertAll(m => m.Label + " " + m.Width + "x" + m.Height + " " + m.CurrentHz + "/" + m.MaxHz + " Hz").ToArray()));
            }, L.T("Monitores", "Monitors")));
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
            catch (Exception ex) { return label + L.T(": não foi possível ler (", ": could not read (") + ex.GetType().Name + ")"; }
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
