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

            const int width = 580;
            // Os três blocos em caixas: o resumo do sistema num cartão, os ajustes num grupo e o registro num campo.
            Controls.Add(new SectionLabel { Text = L.T("Sistema", "System"), Location = new Point(20, 56), Size = new Size(width, 20) });
            var systemCard = new Card { Location = new Point(20, 84), Size = new Size(width, 92) };
            systemInfo = Note(L.T("carregando…", "loading…"), SettingsGroup.Inset, 8, width - SettingsGroup.Inset * 2, 76, Theme.TextSecondary, 8.5f);
            systemInfo.Font = Theme.Mono(8.5f);
            systemInfo.BackColor = Theme.Surface;
            systemInfo.UseMnemonic = false;
            systemCard.Controls.Add(systemInfo);
            Controls.Add(systemCard);

            // Cada ajuste numa linha de um grupo: nome, o que fazer embaixo, o estado numa pílula e o botão Abrir.
            Controls.Add(new SectionLabel { Text = L.T("Windows para jogos", "Windows for games"), Location = new Point(20, systemCard.Bottom + 16), Size = new Size(width, 20) });
            var group = new SettingsGroup(width) { Location = new Point(20, systemCard.Bottom + 44) };
            foreach (WindowsCheck check in WindowsChecks.Read())
                group.AddRow(CheckRow(check, width));
            Controls.Add(group);
            // A seção tem 3 linhas no Windows 10 e 4 no 11: o resto vem logo abaixo dela.
            Label onlyShows = MeasuredNote(L.T("O MouseModes só mostra: \"Abrir\" leva à tela do Windows onde você muda.", "MouseModes only shows these: \"Open\" takes you to the Windows page where you change them."),
                                           20, group.Bottom + 8, width, Theme.TextSecondary, 8.5f);
            Controls.Add(onlyShows);
            int y = onlyShows.Bottom + 12;

            Controls.Add(new SectionLabel { Text = L.T("Registro", "Log"), Location = new Point(20, y), Size = new Size(width, 20) });
            text = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, // a barra nativa fica escondida; o FieldHost mostra a fina
                BorderStyle = BorderStyle.None,
                WordWrap = true, // linha comprida quebra dentro do campo, em vez de sumir na borda
                Font = Theme.Mono(8.5f)
            };
            // Registro baixo: com a seção dos ajustes, a janela ainda cabe numa tela de 768 px com a barra de tarefas.
            var logHost = new FieldHost(text) { Location = new Point(20, y + 28), Size = new Size(width, 124), Accent = Theme.DailyB };
            Controls.Add(logHost);
            int buttonsY = logHost.Bottom + 16;

            var copy = new NeonButton { Text = L.T("Copiar tudo", "Copy all"), Location = new Point(20, buttonsY), Size = new Size(140, 36) };
            var folder = new NeonButton { Text = L.T("Abrir pasta", "Open folder"), Location = new Point(168, buttonsY), Size = new Size(140, 36) };
            var clear = new NeonButton { Text = L.T("Limpar", "Clear"), Style = ButtonStyle.Danger, Location = new Point(20 + width - 120, buttonsY), Size = new Size(120, 36) };
            ClientSize = new Size(620, buttonsY + 36 + 20);
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

        /// <summary>
        /// Uma linha do grupo: nome, o conselho embaixo (altura medida), o estado numa pílula (verde = como recomendado,
        /// âmbar = vale mudar, neutro = depende) e o botão Abrir.
        /// </summary>
        private Control CheckRow(WindowsCheck check, int width)
        {
            const int pad = SettingsGroup.Inset, buttonW = 80;
            int buttonX = width - pad - buttonW;
            int pillW = Math.Min(170, Pill.WidthFor(check.State));
            int pillX = buttonX - 12 - pillW;
            var row = new GroupCell();

            // Título no mesmo estilo do título do ToggleRow dos Ajustes (UiSemibold 10, pintado).
            var name = new SectionLabel
            {
                Text = check.Name,
                RowTitle = true,
                Location = new Point(pad, 9),
                Size = new Size(pillX - 8 - pad, 22)
            };
            string adviceText = string.IsNullOrEmpty(check.Advice) ? L.T("Como recomendado.", "As recommended.") : check.Advice;
            int adviceW = buttonX - 12 - pad;
            var advice = new Label
            {
                Text = adviceText,
                Location = new Point(pad, 30),
                ForeColor = Theme.TextSecondary,
                BackColor = Theme.Surface,
                Font = Theme.Ui(8.5f),
                UseMnemonic = false
            };
            advice.Size = new Size(adviceW, Theme.TextHeight(adviceText, advice.Font, adviceW));
            int height = Math.Max(54, advice.Bottom + 8);

            var state = new Pill
            {
                Text = check.State,
                Tone = check.Ok == true ? Theme.Success : check.Ok == false ? Theme.Warning : Color.Empty,
                BackColor = Theme.Surface,
                Location = new Point(pillX, (height - 24) / 2), // no meio da linha, como o botão Abrir
                Size = new Size(pillW, 24)
            };
            var open = new NeonButton { Text = L.T("Abrir", "Open"), Location = new Point(buttonX, (height - 32) / 2), Size = new Size(buttonW, 32), AccentA = Theme.DailyA, AccentB = Theme.DailyB };
            open.Click += (s, e) =>
            {
                string error = WindowsChecks.OpenSettings(check);
                if (error != null) ConfirmDialog.Inform(this, L.T("Não foi possível abrir", "Could not open"), error);
            };

            row.Size = new Size(width, height);
            row.Controls.Add(name);
            row.Controls.Add(advice);
            row.Controls.Add(state);
            row.Controls.Add(open);
            return row;
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
                       (now.EnhancePointerPrecision ? L.T("ligada", "on") : L.T("desligada", "off")) + L.T(", rolagem ", ", scroll ") + MouseSettings.DescribeScroll(now.ScrollLines);
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
                Log.Info(L.T("Diagnóstico copiado para a área de transferência.", "Diagnostics copied to the clipboard."));
            }
            catch (Exception ex)
            {
                Log.Error(L.T("Copiar diagnóstico", "Copy diagnostics"), ex);
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
                Log.Error(L.T("Abrir pasta do registro", "Open log folder"), ex);
            }
        }
    }
}
