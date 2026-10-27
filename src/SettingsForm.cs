using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MouseModes
{
    public class SettingsForm : GamerWindow
    {
        private const int PageX = 200, PageY = 60, PageW = 440, PageH = 554;

        private readonly AppConfig cfg;
        private readonly ProfilePanel perfPanel;
        private readonly ProfilePanel dailyPanel;
        private readonly GamesPanel gamesPanel;
        private readonly ToggleRow focusRow;
        private readonly NavItem[] nav;
        private readonly Panel[] pages;
        private readonly NeonButton save;

        // Turbo
        private readonly ToggleRow boostRow;
        private readonly ToggleRow winKeyRow;
        private readonly ToggleRow stickyRow;
        private readonly ToggleRow silenceRow;
        private readonly ToggleRow closeRow;
        private readonly TextBox appsToClose;
        private readonly ToggleRow forceRow;

        // Geral
        private readonly ToggleRow startWithWindows;
        private readonly ToggleRow restoreOnExit;
        private readonly ToggleRow notifications;
        private readonly ToggleRow monitorCheckRow;
        private readonly HotkeyBox hotkeyBox;

        public event Action Saved;
        public event Action DiagnosticsRequested;
        public event Action RestoreDefaultsRequested;

        public SettingsForm(AppConfig cfg) : base("Configurações", false)
        {
            this.cfg = cfg;
            CloseOnEscape = true;
            ClientSize = new Size(660, 680);

            Color a = Theme.DailyA, b = Theme.DailyB;
            Color pa = Theme.PerfA, pb = Theme.PerfB;

            // --- Performance / Dia a dia ---
            perfPanel = new ProfilePanel(pa, pb,
                "Dica: velocidade 10 sem aceleração = movimento 1:1. Ajuste a sensibilidade pelo DPI do mouse e dentro do jogo. " +
                "Jogos com configurações próprias (aba Jogos) usam as deles.");
            perfPanel.LoadProfile(cfg.Performance);
            focusRow = MakeRow("Mouse do jogo só com o jogo na tela", "No alt-tab e no desktop, usa o mouse do Dia a dia", pa, pb, 474, 60);
            focusRow.Switch.SetChecked(cfg.MouseOnlyWhenGameFocused, false);
            perfPanel.Controls.Add(focusRow);

            dailyPanel = new ProfilePanel(a, b,
                "Acompanha o mouse do Windows e volta ao fechar o jogo ou ao sair do app. " +
                "Plano ANTERIOR: volta ao plano de energia que estava ativo antes do Performance.",
                "ANTERIOR");
            dailyPanel.LoadProfile(cfg.Daily);

            // --- Jogos ---
            gamesPanel = new GamesPanel(cfg, this);

            // --- Turbo ---
            var turboPanel = new Panel { BackColor = Theme.Bg };
            boostRow = MakeRow("Prioridade alta para o jogo", "Enquanto o modo Performance estiver ativo", pa, pb, 8, 52);
            boostRow.Switch.SetChecked(cfg.BoostGamePriority, false);
            winKeyRow = MakeRow("Bloquear a tecla Windows", "Só quando um jogo da lista estiver em primeiro plano", pa, pb, 66, 52);
            winKeyRow.Switch.SetChecked(cfg.BlockWinKey, false);
            stickyRow = MakeRow("Sem aviso das Teclas de Aderência", "Desliga o atalho Shift 5x e outros de acessibilidade", pa, pb, 124, 52);
            stickyRow.Switch.SetChecked(cfg.DisableAccessibilityHotkeys, false);
            silenceRow = MakeRow("Silenciar notificações do Windows", "Voltam ao normal quando sair do Performance", pa, pb, 182, 52);
            silenceRow.Switch.SetChecked(cfg.SilenceNotifications, false);
            closeRow = MakeRow("Fechar programas ao entrar em Performance", "Os da lista abaixo. Eles não são reabertos depois.", pa, pb, 240, 52);
            closeRow.Switch.SetChecked(cfg.CloseAppsOnPerformance, false);

            var appsLabel = new SectionLabel { Text = "Programas para fechar", Location = new Point(0, 306), Size = new Size(440, 20) };
            var appsNote = Note("Um por linha. Ex.: OneDrive, EpicGamesLauncher, Spotify, chrome", 0, 326, 440, 18, Theme.Faint, 8f);
            appsToClose = NewListBox(cfg.AppsToClose);
            var appsHost = new FieldHost(appsToClose) { Location = new Point(0, 346), Size = new Size(440, 80), Accent = pb };
            var pickApp = new NeonButton { Text = "Escolher programa aberto", Glyph = GlyphKind.Mouse, Location = new Point(0, 434), Size = new Size(440, 38), AccentA = pa, AccentB = pb };
            pickApp.Click += (s, e) => AppendPicked(appsToClose);
            forceRow = MakeRow("Forçar se não fechar", "Encerra à força: pode perder trabalho não salvo", pa, pb, 482, 52);
            forceRow.Switch.SetChecked(cfg.ForceCloseApps, false);

            foreach (Control c in new Control[] { boostRow, winKeyRow, stickyRow, silenceRow, closeRow, appsLabel, appsNote, appsHost, pickApp, forceRow })
                turboPanel.Controls.Add(c);

            // --- Geral ---
            var generalPanel = new Panel { BackColor = Theme.Bg };
            startWithWindows = MakeRow("Iniciar com o Windows", "Abre direto na bandeja, sem mostrar a janela", a, b, 8, 54);
            try { startWithWindows.Switch.SetChecked(Startup.IsEnabled(), false); } catch { }
            restoreOnExit = MakeRow("Voltar ao Dia a dia ao sair", "Desfaz o modo Performance quando o app fecha", a, b, 68, 54);
            restoreOnExit.Switch.SetChecked(cfg.RestoreDailyOnExit, false);
            notifications = MakeRow("Avisos do MouseModes", "Mostra perto do relógio quando o modo muda", a, b, 128, 54);
            notifications.Switch.SetChecked(cfg.ShowNotifications, false);
            monitorCheckRow = MakeRow("Avisar sobre a taxa do monitor", "Mostra na tela inicial se algum monitor está abaixo do máximo", a, b, 188, 54);
            monitorCheckRow.Switch.SetChecked(cfg.MonitorCheck, false);

            var hotkeyLabel = new SectionLabel { Text = "Atalho para trocar de modo", Location = new Point(0, 256), Size = new Size(440, 20) };
            // Clique no campo e aperte a combinação. Se outro programa já usar, o app cai nas opções fixas.
            hotkeyBox = new HotkeyBox();
            uint savedMods;
            Keys savedKey;
            if (!HotkeyWindow.TryParse(cfg.CustomHotkey, out savedMods, out savedKey))
                HotkeyWindow.Choice(cfg.Hotkey, out savedMods, out savedKey);
            hotkeyBox.SetValue(savedMods, savedKey);
            var hotkeyHost = new FieldHost(hotkeyBox) { Location = new Point(0, 280), Size = new Size(300, 40), Accent = b };
            var hotkeyDefault = new NeonButton { Text = "Padrão", Location = new Point(310, 280), Size = new Size(130, 40), AccentA = a, AccentB = b };
            hotkeyDefault.Click += (s, e) =>
            {
                uint mods;
                Keys key;
                HotkeyWindow.Choice(0, out mods, out key);
                hotkeyBox.SetValue(mods, key);
            };

            var maintenance = new SectionLabel { Text = "Manutenção", Location = new Point(0, 340), Size = new Size(440, 20) };
            var diag = new NeonButton { Text = "Diagnóstico", Location = new Point(0, 366), Size = new Size(140, 42), AccentA = a, AccentB = b };
            diag.Click += (s, e) => { if (DiagnosticsRequested != null) DiagnosticsRequested(); };
            var export = new NeonButton { Text = "Exportar", Location = new Point(150, 366), Size = new Size(140, 42), AccentA = a, AccentB = b };
            export.Click += (s, e) => ExportConfig();
            var import = new NeonButton { Text = "Importar", Location = new Point(300, 366), Size = new Size(140, 42), AccentA = a, AccentB = b };
            import.Click += (s, e) => ImportConfig();
            var restore = new NeonButton { Text = "Restaurar padrões do Windows", Glyph = GlyphKind.Power, Style = ButtonStyle.Danger, Location = new Point(0, 418), Size = new Size(440, 42) };
            restore.Click += (s, e) => ConfirmRestore();
            var restoreNote = Note("Restaurar volta o mouse e a energia ao padrão de fábrica: velocidade 10, aceleração ligada, " +
                                   "rolagem 3, plano Equilibrado e USB padrão. O modo Dia a dia passa a usar esses valores.",
                                   0, 470, 440, 54, Theme.Faint, 8.5f);

            foreach (Control c in new Control[] { startWithWindows, restoreOnExit, notifications, monitorCheckRow, hotkeyLabel, hotkeyHost, hotkeyDefault, maintenance, diag, export, import, restore, restoreNote })
                generalPanel.Controls.Add(c);

            pages = new Panel[] { perfPanel, dailyPanel, gamesPanel, turboPanel, generalPanel };
            foreach (Panel p in pages)
            {
                p.Location = new Point(PageX, PageY);
                p.Size = new Size(PageW, PageH);
                Controls.Add(p);
            }

            // --- Navegação lateral ---
            nav = new[]
            {
                new NavItem { Text = "Performance", Glyph = GlyphKind.Bolt, AccentA = pa, AccentB = pb },
                new NavItem { Text = "Dia a dia", Glyph = GlyphKind.Monitor, AccentA = a, AccentB = b },
                new NavItem { Text = "Jogos", Glyph = GlyphKind.Mouse, AccentA = pa, AccentB = pb },
                new NavItem { Text = "Turbo", Glyph = GlyphKind.Gauge, AccentA = pa, AccentB = pb },
                new NavItem { Text = "Geral", Glyph = GlyphKind.Gear, AccentA = a, AccentB = b }
            };
            for (int i = 0; i < nav.Length; i++)
            {
                int index = i;
                nav[i].Location = new Point(18, PageY + i * 50);
                nav[i].Size = new Size(166, 44);
                nav[i].Click += (s, e) => SelectPage(index);
                Controls.Add(nav[i]);
            }
            Controls.Add(new SideDivider { Location = new Point(190, PageY), Size = new Size(1, PageH) });

            // --- Botões ---
            var cancel = new NeonButton { Text = "Cancelar", Location = new Point(370, 626), Size = new Size(130, 42) };
            save = new NeonButton { Text = "Salvar", Style = ButtonStyle.Primary, Location = new Point(510, 626), Size = new Size(130, 42), AccentA = a, AccentB = b };
            cancel.Click += (s, e) => Close();
            save.Click += (s, e) => SaveAndClose();
            Controls.Add(cancel);
            Controls.Add(save);

            SelectPage(0);
            FinishLayout();
        }

        internal static TextBox NewListBox(List<string> lines)
        {
            return new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.None, // a roda do mouse ainda rola; a barra clara do Windows destoava
                BorderStyle = BorderStyle.None,
                Font = Theme.Ui(10f),
                Text = string.Join(Environment.NewLine, lines.ToArray())
            };
        }

        private ToggleRow MakeRow(string title, string subtitle, Color a, Color b, int y, int h)
        {
            var row = new ToggleRow { Title = title, Subtitle = subtitle, Location = new Point(0, y), Size = new Size(440, h) };
            row.SetAccents(a, b);
            return row;
        }

        private void SelectPage(int index)
        {
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visible = i == index;
                nav[i].Selected = i == index;
            }
            TitleBar.AccentA = nav[index].AccentA;
            TitleBar.AccentB = nav[index].AccentB;
            TitleBar.Invalidate();
            save.AccentA = nav[index].AccentA;
            save.AccentB = nav[index].AccentB;
            save.Invalidate();
        }

        /// <summary>Abre a lista de programas abertos; retorna o escolhido ou null.</summary>
        internal string PickProcess()
        {
            using (var picker = new ProcessPickerForm())
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return null;
                return picker.Selected;
            }
        }

        /// <summary>Abre a busca de jogos (instalados e abertos); retorna os escolhidos (pode ser vazia).</summary>
        internal List<string> PickGames(List<string> alreadyListed)
        {
            using (var picker = new GamePickerForm(alreadyListed))
            {
                return picker.ShowDialog(this) == DialogResult.OK ? picker.Selected : new List<string>();
            }
        }

        private void AppendPicked(TextBox target)
        {
            string picked = PickProcess();
            if (picked == null) return;
            string current = target.Text.TrimEnd();
            target.Text = current.Length == 0 ? picked : current + Environment.NewLine + picked;
        }

        private static List<string> ReadList(TextBox box)
        {
            var list = new List<string>();
            foreach (string line in box.Lines)
            {
                string name = GameDetector.Normalize(line);
                if (name.Length > 0 && !list.Exists(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                    list.Add(name);
            }
            return list;
        }

        private void ConfirmRestore()
        {
            bool yes = ConfirmDialog.Ask(this, "Restaurar padrões do Windows?",
                "O mouse e a energia voltam ao padrão de fábrica e o modo muda para Dia a dia. " +
                "Alterações não salvas nesta janela serão descartadas.", "Restaurar");
            if (!yes) return;
            Close();
            if (RestoreDefaultsRequested != null) RestoreDefaultsRequested();
        }

        /// <summary>Grava a configuração salva (não as mudanças desta janela ainda não salvas) num arquivo.</summary>
        private void ExportConfig()
        {
            using (var dialog = new SaveFileDialog
            {
                Title = "Exportar configuração do MouseModes",
                FileName = "MouseModes-config.xml",
                Filter = "Configuração do MouseModes (*.xml)|*.xml",
                DefaultExt = "xml"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    cfg.ForExport().SaveTo(dialog.FileName);
                    Log.Info("Configuração exportada para " + dialog.FileName);
                    ConfirmDialog.Inform(this, "Configuração exportada",
                        "Jogos, perfis, Turbo e ajustes gerais foram salvos no arquivo. Use \"Importar\" no outro PC.");
                }
                catch (Exception ex)
                {
                    Log.Error("Exportar configuração", ex);
                    ConfirmDialog.Inform(this, "Não foi possível exportar", ex.Message);
                }
            }
        }

        /// <summary>Troca as configurações pelas de um arquivo exportado, depois de mostrar o que vem nele.</summary>
        private void ImportConfig()
        {
            string path;
            using (var dialog = new OpenFileDialog
            {
                Title = "Importar configuração do MouseModes",
                Filter = "Configuração do MouseModes (*.xml)|*.xml|Todos os arquivos (*.*)|*.*"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                path = dialog.FileName;
            }

            AppConfig imported;
            try
            {
                imported = AppConfig.Import(path);
            }
            catch (Exception ex)
            {
                Log.Error("Importar configuração de " + path, ex);
                ConfirmDialog.Inform(this, "Arquivo não reconhecido", "Este arquivo não é uma configuração do MouseModes.");
                return;
            }

            // O arquivo pode ter vindo de outra pessoa: mostra o que ele traz, inclusive os programas que seriam fechados.
            List<string> apps = imported.AppsToClose;
            string names = string.Join(", ", apps.GetRange(0, Math.Min(5, apps.Count)).ToArray()) + (apps.Count > 5 ? ", …" : "");
            string closing = apps.Count == 0 ? "nenhum programa para fechar"
                : apps.Count + " programa(s) para fechar (" + names + ")" +
                  (imported.CloseAppsOnPerformance ? "" : ", com o fechamento desligado");
            bool yes = ConfirmDialog.Ask(this, "Importar configuração?",
                "Traz " + imported.Games.Count + " jogo(s), " + imported.GameProfiles.Count + " perfil(is) próprio(s) e " + closing + ". " +
                "As configurações atuais e as mudanças não salvas nesta janela serão substituídas.", "Importar");
            if (!yes) return;

            cfg.CopySettingsFrom(imported);
            try
            {
                cfg.Save();
            }
            catch (Exception ex)
            {
                Log.Error("Salvar configuração importada", ex);
                ConfirmDialog.Inform(this, "Não foi possível salvar", ex.Message);
                return;
            }
            Log.Info("Configuração importada de " + path + ": " + cfg.Games.Count + " jogo(s), " + cfg.AppsToClose.Count + " programa(s) para fechar");

            var handler = Saved;
            if (handler != null) handler();
            Close();
        }

        private void SaveAndClose()
        {
            perfPanel.SaveTo(cfg.Performance);
            dailyPanel.SaveTo(cfg.Daily);
            gamesPanel.SaveTo(cfg);

            cfg.MouseOnlyWhenGameFocused = focusRow.Switch.Checked;
            cfg.AppsToClose = ReadList(appsToClose);
            cfg.BoostGamePriority = boostRow.Switch.Checked;
            cfg.BlockWinKey = winKeyRow.Switch.Checked;
            cfg.DisableAccessibilityHotkeys = stickyRow.Switch.Checked;
            cfg.SilenceNotifications = silenceRow.Switch.Checked;
            cfg.CloseAppsOnPerformance = closeRow.Switch.Checked;
            cfg.ForceCloseApps = forceRow.Switch.Checked;
            cfg.MonitorCheck = monitorCheckRow.Switch.Checked;
            // Uma das opções fixas fica como índice; qualquer outra vira atalho livre (as fixas seguem de reserva).
            int choice = HotkeyWindow.ChoiceIndex(hotkeyBox.Mods, hotkeyBox.Key);
            if (choice >= 0)
            {
                cfg.Hotkey = choice;
                cfg.CustomHotkey = null;
            }
            else
            {
                cfg.CustomHotkey = HotkeyWindow.Format(hotkeyBox.Mods, hotkeyBox.Key);
            }
            cfg.RestoreDailyOnExit = restoreOnExit.Switch.Checked;
            cfg.ShowNotifications = notifications.Switch.Checked;

            try
            {
                cfg.Save();
                Startup.SetEnabled(startWithWindows.Switch.Checked);
            }
            catch (Exception ex)
            {
                Log.Error("Salvar configurações", ex);
                ConfirmDialog.Inform(this, "Não foi possível salvar", ex.Message);
                return;
            }
            Log.Info("Configurações salvas");

            var handler = Saved;
            if (handler != null) handler();
            Close();
        }

        private class SideDivider : Control
        {
            public SideDivider() { BackColor = Theme.Border; }
        }
    }

    /// <summary>Página Jogos: lista de jogos e, para cada um, configurações próprias opcionais.</summary>
    public class GamesPanel : Panel
    {
        private class Entry
        {
            public string Name;
            public MouseProfile Custom; // null = usa o perfil Performance
        }

        private readonly AppConfig cfg;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly ToggleRow autoSwitch;
        private readonly ListBox list;
        private readonly TextBox nameBox;
        private readonly ToggleRow customRow;
        private readonly Panel editor;
        private readonly NeonSlider speed;
        private readonly SectionLabel speedLabel;
        private readonly ToggleRow precision;
        private readonly Segmented plan;
        private readonly Label emptyNote;
        private Entry current;

        public GamesPanel(AppConfig cfg, SettingsForm owner)
        {
            this.cfg = cfg;
            BackColor = Theme.Bg;
            Color a = Theme.PerfA, b = Theme.PerfB;

            foreach (string g in cfg.Games)
            {
                GameProfile gp = cfg.FindGameProfile(g);
                entries.Add(new Entry { Name = g, Custom = gp != null ? gp.Profile.Clone() : null });
            }

            autoSwitch = new ToggleRow { Title = "Troca automática", Subtitle = "Ativa Performance quando um jogo da lista abrir", Location = new Point(0, 8), Size = new Size(440, 56) };
            autoSwitch.SetAccents(a, b);
            autoSwitch.Switch.SetChecked(cfg.AutoSwitch, false);

            var listLabel = new SectionLabel { Text = "Jogos (começo do nome do processo)", Location = new Point(0, 78), Size = new Size(440, 20) };
            list = new ListBox
            {
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 26,
                Font = Theme.Ui(10f)
            };
            list.DrawItem += DrawItem;
            list.SelectedIndexChanged += (s, e) => { list.Invalidate(); Select(list.SelectedIndex); };
            var listHost = new FieldHost(list) { Location = new Point(0, 102), Size = new Size(440, 112), Accent = b };

            nameBox = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Ui(10f) };
            nameBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddName(nameBox.Text); } };
            var nameHost = new FieldHost(nameBox) { Location = new Point(0, 222), Size = new Size(196, 38), Accent = b };
            var add = new NeonButton { Text = "Incluir", Location = new Point(204, 222), Size = new Size(74, 38), AccentA = a, AccentB = b };
            var pick = new NeonButton { Text = "Buscar", Location = new Point(284, 222), Size = new Size(78, 38), AccentA = a, AccentB = b };
            var remove = new NeonButton { Text = "Tirar", Style = ButtonStyle.Danger, Location = new Point(368, 222), Size = new Size(72, 38) };
            add.Click += (s, e) => AddName(nameBox.Text);
            pick.Click += (s, e) =>
            {
                foreach (string p in owner.PickGames(entries.ConvertAll(x => x.Name))) AddName(p);
            };
            remove.Click += (s, e) => RemoveSelected();

            customRow = new ToggleRow { Location = new Point(0, 272), Size = new Size(440, 56), Subtitle = "Desligado: usa as configurações do modo Performance" };
            customRow.SetAccents(a, b);
            customRow.Switch.CheckedChanged += OnCustomToggled;

            editor = new Panel { BackColor = Theme.Bg, Location = new Point(0, 336), Size = new Size(440, 210) };
            speedLabel = new SectionLabel { Text = "Velocidade do ponteiro", Location = new Point(0, 0), Size = new Size(440, 20), RightColor = b };
            speed = new NeonSlider { Minimum = 1, Maximum = 20, Marker = 10, Location = new Point(-4, 22), Size = new Size(448, 36), AccentA = a, AccentB = b };
            speed.ValueChanged += () => { speedLabel.RightText = speed.Value + " / 20"; speedLabel.Invalidate(); };
            precision = new ToggleRow { Title = "Aceleração do Windows", Subtitle = "\"Aprimorar precisão do ponteiro\"", Location = new Point(0, 66), Size = new Size(440, 52) };
            precision.SetAccents(a, b);
            var planLabel = new SectionLabel { Text = "Plano de energia", Location = new Point(0, 130), Size = new Size(440, 20) };
            plan = new Segmented { Options = new[] { "MANTER", "EQUILIBRADO", "ALTO", "MÁXIMO" }, Location = new Point(0, 154), Size = new Size(440, 42), AccentA = a, AccentB = b };
            editor.Controls.Add(speedLabel);
            editor.Controls.Add(speed);
            editor.Controls.Add(precision);
            editor.Controls.Add(planLabel);
            editor.Controls.Add(plan);

            emptyNote = new Label
            {
                Text = "Escolha um jogo na lista para dar a ele configurações próprias. Rolagem e USB seguem o modo Performance.",
                Location = new Point(0, 280),
                Size = new Size(440, 60),
                ForeColor = Theme.Faint,
                Font = Theme.Ui(8.5f),
                BackColor = Color.Transparent
            };

            foreach (Control c in new Control[] { autoSwitch, listLabel, listHost, nameHost, add, pick, remove, customRow, editor, emptyNote })
                Controls.Add(c);

            RefreshList(-1);
        }

        private void RefreshList(int select)
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (Entry e in entries) list.Items.Add(e.Name);
            list.EndUpdate();
            if (select >= 0 && select < entries.Count) list.SelectedIndex = select;
            else Select(-1);
        }

        private void Select(int index)
        {
            CommitEditor();
            current = index >= 0 && index < entries.Count ? entries[index] : null;
            bool has = current != null;
            customRow.Visible = has;
            emptyNote.Visible = !has;
            if (has)
            {
                customRow.Title = "Configurações próprias para " + current.Name;
                customRow.Switch.SetChecked(current.Custom != null, false);
                customRow.Invalidate();
                LoadEditor();
            }
            editor.Visible = has && current.Custom != null;
        }

        private void OnCustomToggled()
        {
            if (current == null) return;
            if (customRow.Switch.Checked)
            {
                // Começa a partir do perfil Performance atual.
                current.Custom = cfg.Performance.Clone();
                LoadEditor();
            }
            else
            {
                current.Custom = null;
            }
            editor.Visible = current.Custom != null;
            list.Invalidate();
        }

        private void LoadEditor()
        {
            if (current == null || current.Custom == null) return;
            speed.Value = Math.Max(1, Math.Min(20, current.Custom.PointerSpeed));
            speedLabel.RightText = speed.Value + " / 20";
            speedLabel.Invalidate();
            precision.Switch.SetChecked(current.Custom.EnhancePointerPrecision, false);
            plan.SelectedIndex = (int)current.Custom.PowerPlan;
        }

        private void CommitEditor()
        {
            if (current == null || current.Custom == null) return;
            current.Custom.PointerSpeed = speed.Value;
            current.Custom.EnhancePointerPrecision = precision.Switch.Checked;
            current.Custom.PowerPlan = (PowerPlanOption)Math.Max(0, plan.SelectedIndex);
        }

        private void AddName(string raw)
        {
            string name = GameDetector.Normalize(raw);
            if (name.Length == 0) return;
            int existing = entries.FindIndex(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing < 0)
            {
                entries.Add(new Entry { Name = name });
                existing = entries.Count - 1;
            }
            nameBox.Text = "";
            RefreshList(existing);
        }

        private void RemoveSelected()
        {
            int i = list.SelectedIndex;
            if (i < 0) return;
            current = null; // não grava o editor no item removido
            entries.RemoveAt(i);
            RefreshList(Math.Min(i, entries.Count - 1));
        }

        public void SaveTo(AppConfig target)
        {
            CommitEditor();
            target.AutoSwitch = autoSwitch.Switch.Checked;
            target.Games = entries.ConvertAll(e => e.Name);
            target.GameProfiles = new List<GameProfile>();
            foreach (Entry e in entries)
            {
                if (e.Custom != null) target.GameProfiles.Add(new GameProfile { Process = e.Name, Profile = e.Custom });
            }
        }

        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= entries.Count) return;
            Entry entry = entries[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            ListPainter.Draw(e, entry.Name, sel, entry.Custom != null ? "PRÓPRIO" : null, Theme.PerfA, Theme.PerfB);
        }
    }

    /// <summary>Desenho dos itens das listas no tema escuro.</summary>
    public static class ListPainter
    {
        public static void Draw(DrawItemEventArgs e, string text, bool selected, string tag, Color a, Color b)
        {
            Graphics g = e.Graphics;
            Theme.HighQuality(g);
            using (var bg = new SolidBrush(Theme.Panel2)) g.FillRectangle(bg, e.Bounds);
            var r = new RectangleF(e.Bounds.X, e.Bounds.Y + 2, e.Bounds.Width - 2, e.Bounds.Height - 4);
            if (selected)
            {
                using (var lb = Theme.Gradient(r, a, b))
                    Theme.FillRounded(g, r, 7, lb);
            }
            float tagW = 0;
            if (tag != null)
            {
                SizeF ts = Theme.MeasureSpaced(g, tag, Theme.Ui(6.5f, FontStyle.Bold), 1f);
                tagW = ts.Width + 14;
                var badge = new RectangleF(r.Right - tagW - 6, r.Y + (r.Height - 16) / 2f, tagW, 16);
                Theme.FillRounded(g, badge, 8, selected ? Theme.Alpha(Color.Black, 70) : Theme.Alpha(a, 50));
                using (var tb = new SolidBrush(selected ? Color.White : b))
                    Theme.DrawSpacedCentered(g, tag, Theme.Ui(6.5f, FontStyle.Bold), tb, badge, 1f);
            }
            Theme.DrawText(g, text, Theme.Ui(10f), selected ? Color.White : Theme.Text,
                new RectangleF(r.X + 10, r.Y, r.Width - 22 - tagW, r.Height), StringAlignment.Near, StringAlignment.Center, false);
        }
    }

    /// <summary>Caixa de confirmação no tema do app.</summary>
    public class ConfirmDialog : GamerWindow
    {
        private ConfirmDialog(string title, string message, string confirmText, bool danger) : base(title, false)
        {
            CloseOnEscape = true;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 210);

            Controls.Add(Note(message, 22, 60, 376, 80, Theme.Muted, 9.5f));
            if (confirmText != null)
            {
                var cancel = new NeonButton { Text = "Cancelar", Location = new Point(132, 150), Size = new Size(126, 42) };
                cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(cancel);
            }
            var ok = new NeonButton
            {
                Text = confirmText ?? "Ok",
                Style = danger ? ButtonStyle.Danger : ButtonStyle.Primary,
                Location = new Point(272, 150),
                Size = new Size(126, 42)
            };
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);
            FinishLayout();
        }

        public static bool Ask(IWin32Window owner, string title, string message, string confirmText)
        {
            using (var d = new ConfirmDialog(title, message, confirmText, true))
                return d.ShowDialog(owner) == DialogResult.OK;
        }

        public static void Inform(IWin32Window owner, string title, string message)
        {
            using (var d = new ConfirmDialog(title, message, null, false))
                d.ShowDialog(owner);
        }
    }

    /// <summary>Página com os ajustes de um modo.</summary>
    public class ProfilePanel : Panel
    {
        private readonly NeonSlider speed;
        private readonly SectionLabel speedLabel;
        private readonly Label speedNote;
        private readonly ToggleRow precision;
        private readonly NeonSlider scroll;
        private readonly SectionLabel scrollLabel;
        private readonly Segmented plan;
        private readonly ToggleRow usb;

        /// <param name="keepPlanLabel">
        /// Nome da 1ª opção de plano (não trocar). No Dia a dia é "ANTERIOR": volta ao plano de antes do Performance.
        /// </param>
        public ProfilePanel(Color a, Color b, string hint, string keepPlanLabel = "MANTER")
        {
            BackColor = Theme.Bg;

            speedLabel = new SectionLabel { Text = "Velocidade do ponteiro", Location = new Point(0, 8), Size = new Size(440, 20), RightColor = b };
            speed = new NeonSlider { Minimum = 1, Maximum = 20, Marker = 10, Location = new Point(-4, 30), Size = new Size(448, 36), AccentA = a, AccentB = b };
            speedNote = new Label { Location = new Point(0, 68), Size = new Size(440, 20), ForeColor = Theme.Muted, Font = Theme.Ui(8.5f), BackColor = Color.Transparent };
            speed.ValueChanged += UpdateLabels;

            precision = new ToggleRow
            {
                Title = "Aceleração do Windows",
                Subtitle = "\"Aprimorar precisão do ponteiro\" (aceleração)",
                Location = new Point(0, 98),
                Size = new Size(440, 60)
            };
            precision.SetAccents(a, b);

            scrollLabel = new SectionLabel { Text = "Linhas por giro da roda", Location = new Point(0, 176), Size = new Size(440, 20), RightColor = b };
            scroll = new NeonSlider { Minimum = 1, Maximum = 20, Location = new Point(-4, 198), Size = new Size(448, 36), AccentA = a, AccentB = b };
            scroll.ValueChanged += UpdateLabels;

            var planLabel = new SectionLabel { Text = "Plano de energia", Location = new Point(0, 252), Size = new Size(440, 20) };
            plan = new Segmented
            {
                Options = new[] { keepPlanLabel, "EQUILIBRADO", "ALTO", "MÁXIMO" },
                Location = new Point(0, 276),
                Size = new Size(440, 42),
                AccentA = a,
                AccentB = b
            };

            usb = new ToggleRow
            {
                Title = "USB sempre ativo",
                Subtitle = "Desativa a suspensão seletiva: o mouse nunca \"adormece\"",
                Location = new Point(0, 332),
                Size = new Size(440, 60)
            };
            usb.SetAccents(a, b);

            var hintLabel = new Label
            {
                Text = hint,
                Location = new Point(0, 408),
                Size = new Size(440, 54),
                ForeColor = Theme.Faint,
                Font = Theme.Ui(8.5f),
                BackColor = Color.Transparent
            };

            Controls.Add(speedLabel);
            Controls.Add(speed);
            Controls.Add(speedNote);
            Controls.Add(precision);
            Controls.Add(scrollLabel);
            Controls.Add(scroll);
            Controls.Add(planLabel);
            Controls.Add(plan);
            Controls.Add(usb);
            Controls.Add(hintLabel);
        }

        private void UpdateLabels()
        {
            int v = speed.Value;
            speedLabel.RightText = v + " / 20";
            speedNote.Text = v == 10
                ? "1:1 — o ponteiro anda exatamente o que o sensor lê (recomendado para jogos)"
                : v > 10 ? "Multiplica o movimento: pode pular pixels" : "Reduz o movimento do sensor";
            scrollLabel.RightText = scroll.Value + (scroll.Value == 1 ? " linha" : " linhas");
            speedLabel.Invalidate();
            scrollLabel.Invalidate();
        }

        public void LoadProfile(MouseProfile p)
        {
            speed.Value = Math.Max(1, Math.Min(20, p.PointerSpeed));
            precision.Switch.SetChecked(p.EnhancePointerPrecision, false);
            scroll.Maximum = Math.Max(20, p.ScrollLines);
            scroll.Value = Math.Max(1, p.ScrollLines);
            plan.SelectedIndex = (int)p.PowerPlan;
            usb.Switch.SetChecked(p.DisableUsbSelectiveSuspend, false);
            UpdateLabels();
        }

        public void SaveTo(MouseProfile p)
        {
            p.PointerSpeed = speed.Value;
            p.EnhancePointerPrecision = precision.Switch.Checked;
            p.ScrollLines = scroll.Value;
            p.PowerPlan = (PowerPlanOption)Math.Max(0, plan.SelectedIndex);
            p.DisableUsbSelectiveSuspend = usb.Switch.Checked;
        }
    }

    /// <summary>Jogos instalados (Steam, Epic) e programas abertos, para incluir vários de uma vez na lista de jogos.</summary>
    public class GamePickerForm : GamerWindow
    {
        private class Item
        {
            public string Process;
            public string Label;
            public string Tag;
        }

        private readonly ListBox list;
        private readonly Label status;
        private readonly HashSet<string> alreadyListed;

        public List<string> Selected { get; private set; }

        public GamePickerForm(IEnumerable<string> alreadyListed) : base("Buscar jogos", false)
        {
            this.alreadyListed = new HashSet<string>(alreadyListed, StringComparer.OrdinalIgnoreCase);
            Selected = new List<string>();
            CloseOnEscape = true;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 560);

            Controls.Add(Note("Jogos da Steam e da Epic e programas abertos. Ctrl+clique escolhe vários.", 20, 58, 420, 20, Theme.Muted));

            list = new ListBox
            {
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 30,
                Font = Theme.Ui(10f),
                SelectionMode = SelectionMode.MultiExtended
            };
            list.DrawItem += DrawItem;
            list.DoubleClick += (s, e) => Confirm();
            list.SelectedIndexChanged += (s, e) => list.Invalidate();
            Controls.Add(new FieldHost(list) { Location = new Point(20, 86), Size = new Size(420, 380), Accent = Theme.PerfB });

            status = Note("Procurando jogos instalados...", 20, 472, 420, 20, Theme.Faint, 8.5f);
            Controls.Add(status);

            var ok = new NeonButton { Text = "Incluir na lista", Style = ButtonStyle.Primary, Location = new Point(20, 500), Size = new Size(420, 44), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            ok.Click += (s, e) => Confirm();
            Controls.Add(ok);

            FinishLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Varrer as pastas dos jogos pode levar alguns segundos: fora da thread da tela.
            var worker = new System.Threading.Thread(() =>
            {
                List<InstalledGame> games = InstalledGames.Find();
                List<string> open = GameDetector.ListWindowedProcesses();
                try
                {
                    if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => Fill(games, open)));
                }
                catch (InvalidOperationException) { } // janela fechada no meio da busca
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void Fill(List<InstalledGame> games, List<string> open)
        {
            var shown = new HashSet<string>(alreadyListed, StringComparer.OrdinalIgnoreCase);
            list.BeginUpdate();
            foreach (InstalledGame g in games)
            {
                if (!shown.Add(g.Process)) continue;
                list.Items.Add(new Item { Process = g.Process, Label = g.Name + "  ·  " + g.Process, Tag = g.Store.ToUpperInvariant() });
            }
            int installed = list.Items.Count;
            foreach (string p in open)
            {
                if (!shown.Add(p)) continue;
                list.Items.Add(new Item { Process = p, Label = p, Tag = "ABERTO" });
            }
            list.EndUpdate();

            int listed = games.Count - installed;
            status.Text = games.Count == 0
                ? "Nenhum jogo da Steam ou da Epic encontrado neste PC."
                : installed + (installed == 1 ? " jogo instalado" : " jogos instalados") + " fora da lista" +
                  (listed > 0 ? " (" + listed + (listed == 1 ? " já está nela)" : " já estão nela)") : "") + ".";
            Log.Info("Busca de jogos: " + games.Count + " instalado(s) na Steam/Epic, " + installed + " fora da lista");
        }

        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var item = (Item)list.Items[e.Index];
            ListPainter.Draw(e, item.Label, (e.State & DrawItemState.Selected) != 0, item.Tag, Theme.PerfA, Theme.PerfB);
        }

        private void Confirm()
        {
            if (list.SelectedItems.Count == 0) return;
            foreach (object o in list.SelectedItems) Selected.Add(((Item)o).Process);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Lista os programas abertos para achar o nome do processo do jogo.</summary>
    public class ProcessPickerForm : GamerWindow
    {
        private readonly ListBox list;

        public string Selected { get; private set; }

        public ProcessPickerForm() : base("Escolher programa", false)
        {
            CloseOnEscape = true;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(400, 520);

            Controls.Add(Note("Deixe o programa ou jogo aberto e escolha o processo dele.", 20, 58, 360, 20, Theme.Muted));

            list = new ListBox
            {
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 30,
                Font = Theme.Ui(10f)
            };
            list.Items.AddRange(GameDetector.ListWindowedProcesses().ToArray());
            list.DrawItem += DrawItem;
            list.DoubleClick += (s, e) => Confirm();
            list.SelectedIndexChanged += (s, e) => list.Invalidate();
            Controls.Add(new FieldHost(list) { Location = new Point(20, 86), Size = new Size(360, 360), Accent = Theme.DailyB });

            var ok = new NeonButton { Text = "Adicionar", Style = ButtonStyle.Primary, Location = new Point(20, 460), Size = new Size(360, 44) };
            ok.Click += (s, e) => Confirm();
            Controls.Add(ok);

            FinishLayout();
        }

        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            ListPainter.Draw(e, (string)list.Items[e.Index], (e.State & DrawItemState.Selected) != 0, null, Theme.DailyA, Theme.DailyB);
        }

        private void Confirm()
        {
            if (list.SelectedItem == null) return;
            Selected = (string)list.SelectedItem;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Campo que grava um atalho: clique e aperte a combinação. Esc cancela; Ctrl ou Alt é obrigatório.</summary>
    public class HotkeyBox : TextBox
    {
        private const string Waiting = "Aperte o atalho (Esc cancela)";

        public uint Mods { get; private set; }
        public Keys Key { get; private set; }

        public HotkeyBox()
        {
            ReadOnly = true;
            BorderStyle = BorderStyle.None;
            Font = Theme.Ui(10f);
            ShortcutsEnabled = false;
            Cursor = Cursors.Hand;
        }

        public void SetValue(uint mods, Keys key)
        {
            Mods = mods;
            Key = key;
            Text = HotkeyWindow.Describe(mods, key);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Text = Waiting;
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Text = HotkeyWindow.Describe(Mods, Key);
        }

        // ProcessCmdKey recebe também Alt+tecla, que nunca chega ao KeyDown de uma caixa de texto.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            uint mods = HotkeyWindow.ModsFrom(keyData & Keys.Modifiers);

            if (key == Keys.Escape && mods == 0)
            {
                Text = HotkeyWindow.Describe(Mods, Key);
                if (Parent != null) Parent.Focus();
                return true;
            }
            if (key == Keys.Tab && mods == 0) return base.ProcessCmdKey(ref msg, keyData); // Tab segue para o próximo campo
            if (HotkeyWindow.IsModifier(key))
            {
                // Ainda segurando Ctrl/Alt/Shift: mostra o começo da combinação ("CTRL + ALT + …").
                string[] caps = HotkeyWindow.Caps(mods, Keys.None); // a última é a tecla, que ainda não veio
                Text = caps.Length == 1 ? Waiting : string.Join(" + ", caps, 0, caps.Length - 1) + " + …";
                return true;
            }

            string problem = HotkeyWindow.Validate(mods, key);
            if (problem != null)
            {
                Text = problem;
                return true;
            }
            SetValue(mods, key);
            return true;
        }
    }
}
