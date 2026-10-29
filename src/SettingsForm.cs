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
        private readonly ToggleRow updateRow;
        private readonly HotkeyBox hotkeyBox;

        public event Action Saved;
        public event Action DiagnosticsRequested;
        public event Action RestoreDefaultsRequested;

        public SettingsForm(AppConfig cfg) : base(L.T("Configurações", "Settings"), false)
        {
            this.cfg = cfg;
            CloseOnEscape = true;
            ClientSize = new Size(660, 680);

            Color a = Theme.DailyA, b = Theme.DailyB;
            Color pa = Theme.PerfA, pb = Theme.PerfB;

            // --- Performance / Dia a dia ---
            perfPanel = new ProfilePanel(pa, pb,
                L.T("Dica: velocidade 10 sem aceleração = movimento 1:1. Ajuste a sensibilidade pelo DPI do mouse e dentro do jogo. " +
                    "Jogos com configurações próprias (aba Jogos) usam as deles.",
                    "Tip: speed 10 without acceleration = 1:1 movement. Tune sensitivity with the mouse DPI and inside the game. " +
                    "Games with their own settings (Games tab) use theirs."));
            perfPanel.LoadProfile(cfg.Performance);
            focusRow = MakeRow(L.T("Mouse do jogo só com o jogo na tela", "Game mouse only with the game on screen"), L.T("No alt-tab e no desktop, usa o mouse do Dia a dia", "On alt-tab and the desktop, uses the Daily mouse"), pa, pb, 474, 60);
            focusRow.Switch.SetChecked(cfg.MouseOnlyWhenGameFocused, false);
            perfPanel.Controls.Add(focusRow);

            dailyPanel = new ProfilePanel(a, b,
                L.T("Acompanha o mouse do Windows e volta ao fechar o jogo ou ao sair do app. " +
                    "Plano ANTERIOR: volta ao plano de energia que estava ativo antes do Performance.",
                    "Follows the Windows mouse and comes back when the game closes or the app quits. " +
                    "PREVIOUS plan: goes back to the power plan that was active before Performance."),
                L.T("ANTERIOR", "PREVIOUS"));
            dailyPanel.LoadProfile(cfg.Daily);

            // --- Jogos ---
            gamesPanel = new GamesPanel(cfg, this);

            // --- Turbo ---
            var turboPanel = new Panel { BackColor = Theme.Bg };
            boostRow = MakeRow(L.T("Prioridade alta para o jogo", "High priority for the game"), L.T("Enquanto o modo Performance estiver ativo", "While Performance mode is on"), pa, pb, 8, 52);
            boostRow.Switch.SetChecked(cfg.BoostGamePriority, false);
            winKeyRow = MakeRow(L.T("Bloquear a tecla Windows", "Block the Windows key"), L.T("Só quando um jogo da lista estiver em primeiro plano", "Only while a game from the list is in the foreground"), pa, pb, 66, 52);
            winKeyRow.Switch.SetChecked(cfg.BlockWinKey, false);
            stickyRow = MakeRow(L.T("Sem aviso das Teclas de Aderência", "No Sticky Keys prompt"), L.T("Desliga o atalho Shift 5x e outros de acessibilidade", "Turns off Shift 5x and other accessibility shortcuts"), pa, pb, 124, 52);
            stickyRow.Switch.SetChecked(cfg.DisableAccessibilityHotkeys, false);
            silenceRow = MakeRow(L.T("Silenciar notificações do Windows", "Silence Windows notifications"), L.T("Voltam ao normal quando sair do Performance", "Back to normal when leaving Performance"), pa, pb, 182, 52);
            silenceRow.Switch.SetChecked(cfg.SilenceNotifications, false);
            closeRow = MakeRow(L.T("Fechar programas ao entrar em Performance", "Close programs when entering Performance"), L.T("Os da lista abaixo. Eles não são reabertos depois.", "The ones listed below. They are not reopened afterwards."), pa, pb, 240, 52);
            closeRow.Switch.SetChecked(cfg.CloseAppsOnPerformance, false);

            var appsLabel = new SectionLabel { Text = L.T("Programas para fechar", "Programs to close"), Location = new Point(0, 306), Size = new Size(440, 20) };
            var appsNote = Note(L.T("Um por linha. Ex.: OneDrive, EpicGamesLauncher, Spotify, chrome", "One per line. E.g.: OneDrive, EpicGamesLauncher, Spotify, chrome"), 0, 326, 440, 18, Theme.Faint, 8f);
            appsToClose = NewListBox(cfg.AppsToClose);
            var appsHost = new FieldHost(appsToClose) { Location = new Point(0, 346), Size = new Size(440, 80), Accent = pb };
            var pickApp = new NeonButton { Text = L.T("Escolher programa aberto", "Pick an open program"), Glyph = GlyphKind.Mouse, Location = new Point(0, 434), Size = new Size(440, 38), AccentA = pa, AccentB = pb };
            pickApp.Click += (s, e) => AppendPicked(appsToClose);
            forceRow = MakeRow(L.T("Forçar se não fechar", "Force if it does not close"), L.T("Encerra à força: pode perder trabalho não salvo", "Kills it: unsaved work may be lost"), pa, pb, 482, 52);
            forceRow.Switch.SetChecked(cfg.ForceCloseApps, false);

            foreach (Control c in new Control[] { boostRow, winKeyRow, stickyRow, silenceRow, closeRow, appsLabel, appsNote, appsHost, pickApp, forceRow })
                turboPanel.Controls.Add(c);

            // --- Geral ---
            var generalPanel = new Panel { BackColor = Theme.Bg };
            // Linhas de 50 px a cada 56: com o aviso de versão nova, a página ainda cabe nos 554 px.
            startWithWindows = MakeRow(L.T("Iniciar com o Windows", "Start with Windows"), L.T("Abre direto na bandeja, sem mostrar a janela", "Opens straight in the tray, without showing the window"), a, b, 8, 50);
            try { startWithWindows.Switch.SetChecked(Startup.IsEnabled(), false); } catch { }
            restoreOnExit = MakeRow(L.T("Voltar ao Dia a dia ao sair", "Back to Daily on quit"), L.T("Desfaz o modo Performance quando o app fecha", "Undoes Performance mode when the app closes"), a, b, 64, 50);
            restoreOnExit.Switch.SetChecked(cfg.RestoreDailyOnExit, false);
            notifications = MakeRow(L.T("Avisos do MouseModes", "MouseModes notices"), L.T("Mostra perto do relógio quando o modo muda", "Shows next to the clock when the mode changes"), a, b, 120, 50);
            notifications.Switch.SetChecked(cfg.ShowNotifications, false);
            monitorCheckRow = MakeRow(L.T("Avisar sobre a taxa do monitor", "Warn about the monitor refresh rate"), L.T("Mostra na tela inicial se algum monitor está abaixo do máximo", "Shows on the main screen if a monitor is below its max"), a, b, 176, 50);
            monitorCheckRow.Switch.SetChecked(cfg.MonitorCheck, false);
            updateRow = MakeRow(L.T("Avisar sobre versão nova", "Notify about new versions"), L.T("Uma vez por dia consulta o GitHub; nunca durante o jogo", "Checks GitHub once a day; never during a game"), a, b, 232, 50);
            updateRow.Switch.SetChecked(cfg.CheckForUpdates, false);

            var hotkeyLabel = new SectionLabel { Text = L.T("Atalho para trocar de modo", "Hotkey to switch modes"), Location = new Point(0, 296), Size = new Size(440, 20) };
            // Clique no campo e aperte a combinação. Se outro programa já usar, o app cai nas opções fixas.
            hotkeyBox = new HotkeyBox();
            uint savedMods;
            Keys savedKey;
            if (!HotkeyWindow.TryParse(cfg.CustomHotkey, out savedMods, out savedKey))
                HotkeyWindow.Choice(cfg.Hotkey, out savedMods, out savedKey);
            hotkeyBox.SetValue(savedMods, savedKey);
            var hotkeyHost = new FieldHost(hotkeyBox) { Location = new Point(0, 320), Size = new Size(300, 40), Accent = b };
            var hotkeyDefault = new NeonButton { Text = L.T("Padrão", "Default"), Location = new Point(310, 320), Size = new Size(130, 40), AccentA = a, AccentB = b };
            hotkeyDefault.Click += (s, e) =>
            {
                uint mods;
                Keys key;
                HotkeyWindow.Choice(0, out mods, out key);
                hotkeyBox.SetValue(mods, key);
            };

            var maintenance = new SectionLabel { Text = L.T("Manutenção", "Maintenance"), Location = new Point(0, 372), Size = new Size(440, 20) };
            var diag = new NeonButton { Text = L.T("Diagnóstico", "Diagnostics"), Location = new Point(0, 398), Size = new Size(140, 42), AccentA = a, AccentB = b };
            diag.Click += (s, e) => { if (DiagnosticsRequested != null) DiagnosticsRequested(); };
            var export = new NeonButton { Text = L.T("Exportar", "Export"), Location = new Point(150, 398), Size = new Size(140, 42), AccentA = a, AccentB = b };
            export.Click += (s, e) => ExportConfig();
            var import = new NeonButton { Text = L.T("Importar", "Import"), Location = new Point(300, 398), Size = new Size(140, 42), AccentA = a, AccentB = b };
            import.Click += (s, e) => ImportConfig();
            var restore = new NeonButton { Text = L.T("Restaurar padrões do Windows", "Restore Windows defaults"), Glyph = GlyphKind.Power, Style = ButtonStyle.Danger, Location = new Point(0, 450), Size = new Size(440, 42) };
            restore.Click += (s, e) => ConfirmRestore();
            var restoreNote = Note(L.T("Restaurar volta o mouse e a energia ao padrão de fábrica: velocidade 10, aceleração ligada, " +
                                       "rolagem 3, plano Equilibrado e USB padrão. O modo Dia a dia passa a usar esses valores.",
                                       "Restore puts mouse and power back to factory defaults: speed 10, acceleration on, " +
                                       "scroll 3, Balanced plan and default USB. Daily mode starts using these values."),
                                   0, 500, 440, 54, Theme.Faint, 8.5f);

            foreach (Control c in new Control[] { startWithWindows, restoreOnExit, notifications, monitorCheckRow, updateRow, hotkeyLabel, hotkeyHost, hotkeyDefault, maintenance, diag, export, import, restore, restoreNote })
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
                new NavItem { Text = L.T("Dia a dia", "Daily"), Glyph = GlyphKind.Monitor, AccentA = a, AccentB = b },
                new NavItem { Text = L.T("Jogos", "Games"), Glyph = GlyphKind.Mouse, AccentA = pa, AccentB = pb },
                new NavItem { Text = "Turbo", Glyph = GlyphKind.Gauge, AccentA = pa, AccentB = pb },
                new NavItem { Text = L.T("Geral", "General"), Glyph = GlyphKind.Gear, AccentA = a, AccentB = b }
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
            var cancel = new NeonButton { Text = L.T("Cancelar", "Cancel"), Location = new Point(370, 626), Size = new Size(130, 42) };
            save = new NeonButton { Text = L.T("Salvar", "Save"), Style = ButtonStyle.Primary, Location = new Point(510, 626), Size = new Size(130, 42), AccentA = a, AccentB = b };
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
            bool yes = ConfirmDialog.Ask(this, L.T("Restaurar padrões do Windows?", "Restore Windows defaults?"),
                L.T("O mouse e a energia voltam ao padrão de fábrica e o modo muda para Dia a dia. " +
                    "Alterações não salvas nesta janela serão descartadas.",
                    "Mouse and power go back to factory defaults and the mode switches to Daily. " +
                    "Unsaved changes in this window will be discarded."), L.T("Restaurar", "Restore"));
            if (!yes) return;
            Close();
            if (RestoreDefaultsRequested != null) RestoreDefaultsRequested();
        }

        /// <summary>Grava a configuração salva (não as mudanças desta janela ainda não salvas) num arquivo.</summary>
        private void ExportConfig()
        {
            using (var dialog = new SaveFileDialog
            {
                Title = L.T("Exportar configuração do MouseModes", "Export MouseModes settings"),
                FileName = "MouseModes-config.xml",
                Filter = L.T("Configuração do MouseModes (*.xml)|*.xml", "MouseModes settings (*.xml)|*.xml"),
                DefaultExt = "xml"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    cfg.ForExport().SaveTo(dialog.FileName);
                    Log.Info(L.T("Configuração exportada para ", "Settings exported to ") + dialog.FileName);
                    ConfirmDialog.Inform(this, L.T("Configuração exportada", "Settings exported"),
                        L.T("Jogos, perfis, Turbo e ajustes gerais foram salvos no arquivo. Use \"Importar\" no outro PC.",
                            "Games, profiles, Turbo and general settings were saved to the file. Use \"Import\" on the other PC."));
                }
                catch (Exception ex)
                {
                    Log.Error(L.T("Exportar configuração", "Export settings"), ex);
                    ConfirmDialog.Inform(this, L.T("Não foi possível exportar", "Could not export"), ex.Message);
                }
            }
        }

        /// <summary>Troca as configurações pelas de um arquivo exportado, depois de mostrar o que vem nele.</summary>
        private void ImportConfig()
        {
            string path;
            using (var dialog = new OpenFileDialog
            {
                Title = L.T("Importar configuração do MouseModes", "Import MouseModes settings"),
                Filter = L.T("Configuração do MouseModes (*.xml)|*.xml|Todos os arquivos (*.*)|*.*", "MouseModes settings (*.xml)|*.xml|All files (*.*)|*.*")
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
                Log.Error(L.T("Importar configuração de ", "Import settings from ") + path, ex);
                ConfirmDialog.Inform(this, L.T("Arquivo não reconhecido", "File not recognized"), L.T("Este arquivo não é uma configuração do MouseModes.", "This file is not a MouseModes settings file."));
                return;
            }

            // O arquivo pode ter vindo de outra pessoa: mostra o que ele traz, inclusive os programas que seriam fechados.
            List<string> apps = imported.AppsToClose;
            string names = string.Join(", ", apps.GetRange(0, Math.Min(5, apps.Count)).ToArray()) + (apps.Count > 5 ? ", …" : "");
            string closing = apps.Count == 0 ? L.T("nenhum programa para fechar", "no programs to close")
                : apps.Count + L.T(" programa(s) para fechar (", " program(s) to close (") + names + ")" +
                  (imported.CloseAppsOnPerformance ? "" : L.T(", com o fechamento desligado", ", with closing turned off"));
            bool yes = ConfirmDialog.Ask(this, L.T("Importar configuração?", "Import settings?"),
                L.T("Traz ", "It brings ") + imported.Games.Count + L.T(" jogo(s), ", " game(s), ") + imported.GameProfiles.Count +
                L.T(" perfil(is) próprio(s) e ", " own profile(s) and ") + closing + ". " +
                L.T("As configurações atuais e as mudanças não salvas nesta janela serão substituídas.", "The current settings and the unsaved changes in this window will be replaced."),
                L.T("Importar", "Import"));
            if (!yes) return;

            cfg.CopySettingsFrom(imported);
            try
            {
                cfg.Save();
            }
            catch (Exception ex)
            {
                Log.Error(L.T("Salvar configuração importada", "Save imported settings"), ex);
                ConfirmDialog.Inform(this, L.T("Não foi possível salvar", "Could not save"), ex.Message);
                return;
            }
            Log.Info(L.T("Configuração importada de ", "Settings imported from ") + path + ": " + cfg.Games.Count + L.T(" jogo(s), ", " game(s), ") +
                cfg.AppsToClose.Count + L.T(" programa(s) para fechar", " program(s) to close"));

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
            cfg.CheckForUpdates = updateRow.Switch.Checked;
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
                Log.Error(L.T("Salvar configurações", "Save settings"), ex);
                ConfirmDialog.Inform(this, L.T("Não foi possível salvar", "Could not save"), ex.Message);
                return;
            }
            Log.Info(L.T("Configurações salvas", "Settings saved"));

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

            autoSwitch = new ToggleRow { Title = L.T("Troca automática", "Automatic switch"), Subtitle = L.T("Ativa Performance quando um jogo da lista abrir", "Turns on Performance when a game from the list opens"), Location = new Point(0, 8), Size = new Size(440, 56) };
            autoSwitch.SetAccents(a, b);
            autoSwitch.Switch.SetChecked(cfg.AutoSwitch, false);

            var listLabel = new SectionLabel { Text = L.T("Jogos (começo do nome do processo)", "Games (start of the process name)"), Location = new Point(0, 78), Size = new Size(440, 20) };
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
            var add = new NeonButton { Text = L.T("Incluir", "Add"), Location = new Point(204, 222), Size = new Size(74, 38), AccentA = a, AccentB = b };
            var pick = new NeonButton { Text = L.T("Buscar", "Find"), Location = new Point(284, 222), Size = new Size(78, 38), AccentA = a, AccentB = b };
            var remove = new NeonButton { Text = L.T("Tirar", "Remove"), Style = ButtonStyle.Danger, Location = new Point(368, 222), Size = new Size(72, 38) };
            add.Click += (s, e) => AddName(nameBox.Text);
            pick.Click += (s, e) =>
            {
                foreach (string p in owner.PickGames(entries.ConvertAll(x => x.Name))) AddName(p);
            };
            remove.Click += (s, e) => RemoveSelected();

            customRow = new ToggleRow { Location = new Point(0, 272), Size = new Size(440, 56), Subtitle = L.T("Desligado: usa as configurações do modo Performance", "Off: uses the Performance mode settings") };
            customRow.SetAccents(a, b);
            customRow.Switch.CheckedChanged += OnCustomToggled;

            editor = new Panel { BackColor = Theme.Bg, Location = new Point(0, 336), Size = new Size(440, 210) };
            speedLabel = new SectionLabel { Text = L.T("Velocidade do ponteiro", "Pointer speed"), Location = new Point(0, 0), Size = new Size(440, 20), RightColor = b };
            speed = new NeonSlider { Minimum = 1, Maximum = 20, Marker = 10, Location = new Point(-4, 22), Size = new Size(448, 36), AccentA = a, AccentB = b };
            speed.ValueChanged += () => { speedLabel.RightText = speed.Value + " / 20"; speedLabel.Invalidate(); };
            precision = new ToggleRow { Title = L.T("Aceleração do Windows", "Windows acceleration"), Subtitle = L.T("\"Aprimorar precisão do ponteiro\"", "\"Enhance pointer precision\""), Location = new Point(0, 66), Size = new Size(440, 52) };
            precision.SetAccents(a, b);
            var planLabel = new SectionLabel { Text = L.T("Plano de energia", "Power plan"), Location = new Point(0, 130), Size = new Size(440, 20) };
            plan = new Segmented { Options = new[] { L.T("MANTER", "KEEP"), L.T("EQUILIBRADO", "BALANCED"), L.T("ALTO", "HIGH"), L.T("MÁXIMO", "ULTIMATE") }, Location = new Point(0, 154), Size = new Size(440, 42), AccentA = a, AccentB = b };
            editor.Controls.Add(speedLabel);
            editor.Controls.Add(speed);
            editor.Controls.Add(precision);
            editor.Controls.Add(planLabel);
            editor.Controls.Add(plan);

            emptyNote = new Label
            {
                Text = L.T("Escolha um jogo na lista para dar a ele configurações próprias. Rolagem e USB seguem o modo Performance.", "Pick a game in the list to give it its own settings. Scroll and USB follow Performance mode."),
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
                customRow.Title = L.T("Configurações próprias para ", "Own settings for ") + current.Name;
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
            ListPainter.Draw(e, entry.Name, sel, entry.Custom != null ? L.T("PRÓPRIO", "OWN") : null, Theme.PerfA, Theme.PerfB);
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
                var cancel = new NeonButton { Text = L.T("Cancelar", "Cancel"), Location = new Point(132, 150), Size = new Size(126, 42) };
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
        public ProfilePanel(Color a, Color b, string hint, string keepPlanLabel = null)
        {
            BackColor = Theme.Bg;

            speedLabel = new SectionLabel { Text = L.T("Velocidade do ponteiro", "Pointer speed"), Location = new Point(0, 8), Size = new Size(440, 20), RightColor = b };
            speed = new NeonSlider { Minimum = 1, Maximum = 20, Marker = 10, Location = new Point(-4, 30), Size = new Size(448, 36), AccentA = a, AccentB = b };
            speedNote = new Label { Location = new Point(0, 68), Size = new Size(440, 20), ForeColor = Theme.Muted, Font = Theme.Ui(8.5f), BackColor = Color.Transparent };
            speed.ValueChanged += UpdateLabels;

            precision = new ToggleRow
            {
                Title = L.T("Aceleração do Windows", "Windows acceleration"),
                Subtitle = L.T("\"Aprimorar precisão do ponteiro\" (aceleração)", "\"Enhance pointer precision\" (acceleration)"),
                Location = new Point(0, 98),
                Size = new Size(440, 60)
            };
            precision.SetAccents(a, b);

            scrollLabel = new SectionLabel { Text = L.T("Linhas por giro da roda", "Lines per wheel notch"), Location = new Point(0, 176), Size = new Size(440, 20), RightColor = b };
            scroll = new NeonSlider { Minimum = 1, Maximum = 20, Location = new Point(-4, 198), Size = new Size(448, 36), AccentA = a, AccentB = b };
            scroll.ValueChanged += UpdateLabels;

            var planLabel = new SectionLabel { Text = L.T("Plano de energia", "Power plan"), Location = new Point(0, 252), Size = new Size(440, 20) };
            plan = new Segmented
            {
                Options = new[] { keepPlanLabel ?? L.T("MANTER", "KEEP"), L.T("EQUILIBRADO", "BALANCED"), L.T("ALTO", "HIGH"), L.T("MÁXIMO", "ULTIMATE") },
                Location = new Point(0, 276),
                Size = new Size(440, 42),
                AccentA = a,
                AccentB = b
            };

            usb = new ToggleRow
            {
                Title = L.T("USB sempre ativo", "USB always on"),
                Subtitle = L.T("Desativa a suspensão seletiva: o mouse nunca \"adormece\"", "Turns off selective suspend: the mouse never \"falls asleep\""),
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
                ? L.T("1:1 — o ponteiro anda exatamente o que o sensor lê (recomendado para jogos)", "1:1 — the pointer moves exactly what the sensor reads (recommended for games)")
                : v > 10 ? L.T("Multiplica o movimento: pode pular pixels", "Multiplies movement: may skip pixels") : L.T("Reduz o movimento do sensor", "Reduces the sensor movement");
            scrollLabel.RightText = scroll.Value + (scroll.Value == 1 ? L.T(" linha", " line") : L.T(" linhas", " lines"));
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

        public GamePickerForm(IEnumerable<string> alreadyListed) : base(L.T("Buscar jogos", "Find games"), false)
        {
            this.alreadyListed = new HashSet<string>(alreadyListed, StringComparer.OrdinalIgnoreCase);
            Selected = new List<string>();
            CloseOnEscape = true;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 560);

            Controls.Add(Note(L.T("Jogos da Steam e da Epic e programas abertos. Ctrl+clique escolhe vários.", "Steam and Epic games plus open programs. Ctrl+click picks several."), 20, 58, 420, 20, Theme.Muted));

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

            status = Note(L.T("Procurando jogos instalados...", "Looking for installed games..."), 20, 472, 420, 20, Theme.Faint, 8.5f);
            Controls.Add(status);

            var ok = new NeonButton { Text = L.T("Incluir na lista", "Add to the list"), Style = ButtonStyle.Primary, Location = new Point(20, 500), Size = new Size(420, 44), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
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
                list.Items.Add(new Item { Process = p, Label = p, Tag = L.T("ABERTO", "OPEN") });
            }
            list.EndUpdate();

            int listed = games.Count - installed;
            status.Text = games.Count == 0
                ? L.T("Nenhum jogo da Steam ou da Epic encontrado neste PC.", "No Steam or Epic game found on this PC.")
                : installed + (installed == 1 ? L.T(" jogo instalado", " installed game") : L.T(" jogos instalados", " installed games")) + L.T(" fora da lista", " not in the list") +
                  (listed > 0 ? " (" + listed + (listed == 1 ? L.T(" já está nela)", " already in it)") : L.T(" já estão nela)", " already in it)")) : "") + ".";
            Log.Info(L.T("Busca de jogos: ", "Find games: ") + games.Count + L.T(" instalado(s) na Steam/Epic, ", " installed on Steam/Epic, ") + installed + L.T(" fora da lista", " not in the list"));
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

        public ProcessPickerForm() : base(L.T("Escolher programa", "Pick a program"), false)
        {
            CloseOnEscape = true;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(400, 520);

            Controls.Add(Note(L.T("Deixe o programa ou jogo aberto e escolha o processo dele.", "Leave the program or game open and pick its process."), 20, 58, 360, 20, Theme.Muted));

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

            var ok = new NeonButton { Text = L.T("Adicionar", "Add"), Style = ButtonStyle.Primary, Location = new Point(20, 460), Size = new Size(360, 44) };
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
        private static string Waiting { get { return L.T("Aperte o atalho (Esc cancela)", "Press the hotkey (Esc cancels)"); } }

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
