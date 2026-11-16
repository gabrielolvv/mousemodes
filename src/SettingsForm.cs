using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MouseModes
{
    public class SettingsForm : GamerWindow
    {
        private const int PageX = 200, PageY = 60, PageW = SettingsLayout.Width, PageH = SettingsLayout.PageHeight;

        private readonly AppConfig cfg;
        private readonly ProfilePanel perfPanel;
        private readonly ProfilePanel dailyPanel;
        private readonly GamesPanel gamesPanel;
        private readonly ToggleRow focusRow;
        private readonly ToggleRow guardRow;
        private readonly NavItem[] nav;
        private readonly Panel[] pages;
        private readonly NeonButton save;
        private readonly ThinScrollBar pageBar;

        // Turbo
        private readonly ToggleRow boostRow;
        private readonly ToggleRow calmRow;
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
        private readonly ToggleRow autoUpdateRow;
        private readonly ToggleRow networkRow;
        private readonly ToggleRow historyRow;
        private Label framesStatus;
        private NeonButton framesButton;
        private readonly HotkeyBox hotkeyBox;

        public event Action Saved;
        /// <summary>Salvar apertado, antes de as páginas gravarem no config.</summary>
        public event Action Saving;
        public event Action DiagnosticsRequested;
        public event Action RestoreDefaultsRequested;

        public SettingsForm(AppConfig cfg) : base(L.T("Configurações", "Settings"), false)
        {
            this.cfg = cfg;
            CloseOnEscape = true;
            ClientSize = new Size(660, 680);

            Color a = Theme.DailyA, b = Theme.DailyB;
            Color pa = Theme.PerfA, pb = Theme.PerfB;
            const int inner = SettingsLayout.Width - SettingsGroup.Inset * 2;

            // --- Performance / Dia a dia ---
            perfPanel = new ProfilePanel(pa, pb,
                L.T("Dica: velocidade 10 sem aceleração = movimento 1:1. Ajuste a sensibilidade pelo DPI do mouse e dentro do jogo. " +
                    "Jogos com configurações próprias (aba Jogos) usam as deles.",
                    "Tip: speed 10 without acceleration = 1:1 movement. Tune sensitivity with the mouse DPI and inside the game. " +
                    "Games with their own settings (Games tab) use theirs."));
            perfPanel.LoadProfile(cfg.Performance);
            focusRow = MakeRow(L.T("Mouse do jogo só com o jogo na tela", "Game mouse only with the game on screen"), L.T("No alt-tab e no desktop, usa o mouse do Dia a dia", "On alt-tab and the desktop, uses the Daily mouse"), pa, pb);
            focusRow.Switch.SetChecked(cfg.MouseOnlyWhenGameFocused, false);
            guardRow = MakeRow(L.T("Vigiar o mouse", "Watch the mouse"),
                L.T("Desfaz se outro programa mexer no mouse no Performance", "Undoes it if another program changes the mouse in Performance"), pa, pb);
            guardRow.Switch.SetChecked(cfg.GuardMouse, false);
            perfPanel.AddGroup(L.T("Durante o jogo", "While gaming"), focusRow, guardRow);

            dailyPanel = new ProfilePanel(a, b,
                L.T("Acompanha o mouse do Windows e volta ao fechar o jogo ou ao sair do app. " +
                    "Plano \"Anterior\": volta ao plano de energia que estava ativo antes do Performance.",
                    "Follows the Windows mouse and comes back when the game closes or the app quits. " +
                    "\"Previous\" plan: goes back to the power plan that was active before Performance."),
                L.T("Anterior", "Previous"));
            dailyPanel.LoadProfile(cfg.Daily);

            // --- Jogos ---
            gamesPanel = new GamesPanel(cfg, this);

            // --- Turbo ---
            var turboPanel = SettingsLayout.NewPage();
            boostRow = MakeRow(L.T("Prioridade alta para o jogo", "High priority for the game"), L.T("Enquanto o modo Performance estiver ativo", "While Performance mode is on"), pa, pb);
            boostRow.Switch.SetChecked(cfg.BoostGamePriority, false);
            winKeyRow = MakeRow(L.T("Bloquear a tecla Windows", "Block the Windows key"), L.T("Só quando um jogo da lista estiver em primeiro plano", "Only while a game from the list is in the foreground"), pa, pb);
            winKeyRow.Switch.SetChecked(cfg.BlockWinKey, false);
            stickyRow = MakeRow(L.T("Sem aviso das Teclas de Aderência", "No Sticky Keys prompt"), L.T("Desliga o atalho Shift 5x e outros de acessibilidade", "Turns off Shift 5x and other accessibility shortcuts"), pa, pb);
            stickyRow.Switch.SetChecked(cfg.DisableAccessibilityHotkeys, false);
            silenceRow = MakeRow(L.T("Silenciar notificações do Windows", "Silence Windows notifications"), L.T("Voltam ao normal quando sair do Performance", "Back to normal when leaving Performance"), pa, pb);
            silenceRow.Switch.SetChecked(cfg.SilenceNotifications, false);
            // Processador híbrido: os outros programas também vão para os núcleos de eficiência.
            calmRow = MakeRow(L.T("Segundo plano calmo", "Calm background"),
                CpuSets.Background != null
                    ? L.T("Outros programas: prioridade baixa, eficiência e núcleos E", "Other programs: low priority, efficiency mode and E-cores")
                    : L.T("Outros programas com prioridade baixa e em modo de eficiência", "Other programs at low priority and in efficiency mode"),
                pa, pb);
            calmRow.Switch.SetChecked(cfg.CalmBackground, false);
            closeRow = MakeRow(L.T("Fechar programas ao entrar em Performance", "Close programs when entering Performance"), L.T("Os da lista abaixo. Eles não são reabertos depois.", "The ones listed below. They are not reopened afterwards."), pa, pb);
            closeRow.Switch.SetChecked(cfg.CloseAppsOnPerformance, false);
            forceRow = MakeRow(L.T("Forçar se não fechar", "Force if it does not close"), L.T("Encerra à força: pode perder trabalho não salvo", "Kills it: unsaved work may be lost"), pa, pb);
            forceRow.Switch.SetChecked(cfg.ForceCloseApps, false);

            // A lista de programas: título com o botão de escolher ao lado, a legenda e a caixa (um por linha).
            const int pickW = 204;
            var appsLabel = SettingsLayout.RowTitle(L.T("Programas para fechar", "Programs to close"));
            appsLabel.Location = new Point(SettingsGroup.Inset, 18);
            appsLabel.Width = inner - pickW - 8;
            var pickApp = new NeonButton { Text = L.T("Escolher programa aberto", "Pick an open program"), Glyph = GlyphKind.Mouse, Location = new Point(SettingsLayout.Width - SettingsGroup.Inset - pickW, 12), Size = new Size(pickW, 32), AccentA = pa, AccentB = pb };
            pickApp.Click += (s, e) => AppendPicked(appsToClose);
            var appsNote = SettingsLayout.Caption(L.T("Um por linha. Ex.: OneDrive, EpicGamesLauncher, Spotify, chrome", "One per line. E.g.: OneDrive, EpicGamesLauncher, Spotify, chrome"), inner);
            appsNote.Location = new Point(SettingsGroup.Inset, 50);
            appsToClose = NewListBox(cfg.AppsToClose);
            var appsHost = new FieldHost(appsToClose) { Location = new Point(SettingsGroup.Inset, appsNote.Bottom + 6), Size = new Size(inner, 72), Accent = pb };
            var appsCell = SettingsLayout.Cell(appsHost.Bottom + 14, appsLabel, pickApp, appsNote, appsHost);

            var during = SettingsLayout.Group(boostRow, winKeyRow, stickyRow, silenceRow, calmRow);
            var closing = SettingsLayout.Group(closeRow, appsCell, forceRow);
            int y = SettingsLayout.Place(turboPanel, 0, SettingsLayout.Header(L.T("No modo Performance", "In Performance mode")), during);
            y = SettingsLayout.Place(turboPanel, y, SettingsLayout.Header(L.T("Fechar programas", "Close programs")), closing);
            SettingsLayout.FinishPage(turboPanel, y);

            // --- Geral ---
            var generalPanel = SettingsLayout.NewPage();
            // As linhas de versão nova ficam na página Privacidade, com o resto do que sai do PC.
            startWithWindows = MakeRow(L.T("Iniciar com o Windows", "Start with Windows"), L.T("Abre direto na bandeja, sem mostrar a janela", "Opens straight in the tray, without showing the window"), a, b);
            try { startWithWindows.Switch.SetChecked(Startup.IsEnabled(), false); } catch { }
            restoreOnExit = MakeRow(L.T("Voltar ao Dia a dia ao sair", "Back to Daily on quit"), L.T("Desfaz o modo Performance quando o app fecha", "Undoes Performance mode when the app closes"), a, b);
            restoreOnExit.Switch.SetChecked(cfg.RestoreDailyOnExit, false);
            notifications = MakeRow(L.T("Avisos do MouseModes", "MouseModes notices"), L.T("Mostra perto do relógio quando o modo muda", "Shows next to the clock when the mode changes"), a, b);
            notifications.Switch.SetChecked(cfg.ShowNotifications, false);
            monitorCheckRow = MakeRow(L.T("Avisar sobre a taxa do monitor", "Warn about the monitor refresh rate"), L.T("Mostra na tela inicial se algum monitor está abaixo do máximo", "Shows on the main screen if a monitor is below its max"), a, b);
            monitorCheckRow.Switch.SetChecked(cfg.MonitorCheck, false);

            var hotkeyLabel = SettingsLayout.Header(L.T("Atalho para trocar de modo", "Hotkey to switch modes"));
            // Clique no campo e aperte a combinação. Se outro programa já usar, o app cai nas opções fixas.
            hotkeyBox = new HotkeyBox();
            uint savedMods;
            Keys savedKey;
            if (!HotkeyWindow.TryParse(cfg.CustomHotkey, out savedMods, out savedKey))
                HotkeyWindow.Choice(cfg.Hotkey, out savedMods, out savedKey);
            hotkeyBox.SetValue(savedMods, savedKey);
            const int sideW = 120;
            var hotkeyHost = new FieldHost(hotkeyBox) { Location = new Point(SettingsGroup.Inset, 12), Size = new Size(inner - sideW - 8, 36), Accent = b };
            var hotkeyDefault = new NeonButton { Text = L.T("Padrão", "Default"), Location = new Point(SettingsLayout.Width - SettingsGroup.Inset - sideW, 12), Size = new Size(sideW, 36), AccentA = a, AccentB = b };
            hotkeyDefault.Click += (s, e) =>
            {
                uint mods;
                Keys key;
                HotkeyWindow.Choice(0, out mods, out key);
                hotkeyBox.SetValue(mods, key);
            };

            // Caça-travadinhas: o Windows só deixa ler os quadros do jogo com a permissão do grupo "Performance Log Users".
            var framesLabel = SettingsLayout.Header(L.T("Caça-travadinhas (quadros do jogo)", "Stutter finder (game frames)"));
            const int framesTextW = inner - sideW - 16;
            framesStatus = SettingsLayout.Caption(9f, "", framesTextW, FrameStatusTexts()); // alto o bastante para os três textos
            int framesH = Math.Max(56, framesStatus.Height + 24);
            framesStatus.Location = new Point(SettingsGroup.Inset, (framesH - framesStatus.Height) / 2);
            framesButton = new NeonButton { Location = new Point(SettingsLayout.Width - SettingsGroup.Inset - sideW, (framesH - 36) / 2), Size = new Size(sideW, 36), AccentA = a, AccentB = b };
            framesButton.Click += (s, e) => ChangeFramePermission();
            ShowFramePermission();

            var maintenance = SettingsLayout.Header(L.T("Manutenção", "Maintenance"));
            const int toolW = (inner - 16) / 3;
            var diag = new NeonButton { Text = L.T("Diagnóstico", "Diagnostics"), Location = new Point(SettingsGroup.Inset, 12), Size = new Size(toolW, 36), AccentA = a, AccentB = b };
            diag.Click += (s, e) => { if (DiagnosticsRequested != null) DiagnosticsRequested(); };
            var export = new NeonButton { Text = L.T("Exportar", "Export"), Location = new Point(SettingsGroup.Inset + toolW + 8, 12), Size = new Size(toolW, 36), AccentA = a, AccentB = b };
            export.Click += (s, e) => ExportConfig();
            var import = new NeonButton { Text = L.T("Importar", "Import"), Location = new Point(SettingsLayout.Width - SettingsGroup.Inset - toolW, 12), Size = new Size(toolW, 36), AccentA = a, AccentB = b };
            import.Click += (s, e) => ImportConfig();
            var restore = new NeonButton { Text = L.T("Restaurar padrões do Windows", "Restore Windows defaults"), Glyph = GlyphKind.Power, Style = ButtonStyle.Danger, Location = new Point(SettingsGroup.Inset, 12), Size = new Size(inner, 36) };
            restore.Click += (s, e) => ConfirmRestore();
            var restoreNote = SettingsLayout.Caption(L.T("Velocidade 10, aceleração ligada, rolagem 3, plano Equilibrado e USB padrão. " +
                                                         "O Dia a dia passa a usar esses valores.",
                                                         "Speed 10, acceleration on, scroll 3, Balanced plan and default USB. " +
                                                         "Daily mode starts using these values."), inner);
            restoreNote.Location = new Point(SettingsGroup.Inset, restore.Bottom + 6);

            y = SettingsLayout.Place(generalPanel, 0, SettingsLayout.Header(L.T("Inicialização e avisos", "Startup and notices")),
                SettingsLayout.Group(startWithWindows, restoreOnExit, notifications, monitorCheckRow));
            y = SettingsLayout.Place(generalPanel, y, hotkeyLabel, SettingsLayout.Group(SettingsLayout.Cell(60, hotkeyHost, hotkeyDefault)));
            y = SettingsLayout.Place(generalPanel, y, framesLabel, SettingsLayout.Group(SettingsLayout.Cell(framesH, framesStatus, framesButton)));
            y = SettingsLayout.Place(generalPanel, y, maintenance, SettingsLayout.Group(
                SettingsLayout.Cell(60, diag, export, import),
                SettingsLayout.Cell(restoreNote.Bottom + 12, restore, restoreNote)));
            SettingsLayout.FinishPage(generalPanel, y);

            // --- Privacidade: tudo o que sai do PC, e como desligar ---
            var privacyPanel = SettingsLayout.NewPage();
            var outLabel = SettingsLayout.Header(L.T("O que sai do seu PC", "What leaves your PC"));
            var outGroup = SettingsLayout.Group(
                SettingsLayout.IconLine(GlyphKind.Clock,
                    L.T("Uma vez por dia, uma consulta à página de versões do GitHub, para saber se há versão nova; ao atualizar, " +
                        "o instalador é baixado da Release do GitHub.",
                        "Once a day, a request to the GitHub releases page, to know whether there is a new version; when updating, " +
                        "the installer is downloaded from the GitHub Release.")),
                SettingsLayout.IconLine(GlyphKind.Gauge,
                    L.T("Durante a partida, com o Raio-X da rede ligado: um ping por segundo ao seu roteador e ao 1.1.1.1 " +
                        "(Cloudflare), sem conteúdo.",
                        "During a match, with the network X-ray on: one ping per second to your router and to 1.1.1.1 " +
                        "(Cloudflare), with no content.")),
                SettingsLayout.IconLine(GlyphKind.Shield,
                    L.T("Mais nada: sem conta e sem telemetria. As configurações, o registro e o histórico das partidas ficam neste PC, em %APPDATA%\\MouseModes.",
                        "Nothing else: no account and no telemetry. Settings, the log and the match history stay on this PC, in %APPDATA%\\MouseModes.")));
            updateRow = MakeRow(L.T("Avisar sobre versão nova", "Notify about new versions"), L.T("Uma vez por dia consulta o GitHub; nunca durante o jogo", "Checks GitHub once a day; never during a game"), a, b);
            updateRow.Switch.SetChecked(cfg.CheckForUpdates, false);
            autoUpdateRow = MakeRow(L.T("Instalar versão nova sozinho", "Install new versions by itself"),
                L.T("Baixa do GitHub, confere o SHA-256 e instala fora do jogo", "Downloads, checks the SHA-256 and installs outside games"), a, b);
            autoUpdateRow.Switch.SetChecked(cfg.AutoUpdate, false);
            networkRow = MakeRow(L.T("Medir a rede no Raio-X", "Measure the network in the X-ray"),
                L.T("Um ping por segundo ao roteador e ao 1.1.1.1, só na partida", "One ping per second to the router and 1.1.1.1, only in a match"), a, b);
            networkRow.Switch.SetChecked(cfg.MeasureNetwork, false);
            historyRow = MakeRow(L.T("Guardar o histórico das partidas", "Keep the match history"),
                L.T("As últimas 100 partidas, só neste PC (bandeja → Partidas)", "The last 100 matches, only on this PC (tray → Previous)"), a, b);
            historyRow.Switch.SetChecked(cfg.KeepMatchHistory, false);
            var privacyNote = SettingsLayout.Caption(L.T("Desligando as duas primeiras, o app não fala com o GitHub sozinho (Procurar versão nova, no ícone da " +
                                                         "bandeja, continua funcionando). O código está aberto no GitHub para quem quiser conferir.",
                                                         "With the first two off, the app does not talk to GitHub by itself (Check for a new version, in the tray " +
                                                         "icon, still works). The code is open on GitHub for anyone who wants to check."), SettingsLayout.Width);
            privacyNote.BackColor = Theme.Bg;
            y = SettingsLayout.Place(privacyPanel, 0, outLabel, outGroup);
            y = SettingsLayout.Place(privacyPanel, y, SettingsLayout.Header(L.T("O que você controla", "What you control")),
                SettingsLayout.Group(updateRow, autoUpdateRow, networkRow, historyRow));
            y = SettingsLayout.PlaceCaption(privacyPanel, y, privacyNote);
            SettingsLayout.FinishPage(privacyPanel, y);

            pages = new Panel[] { perfPanel, dailyPanel, gamesPanel, turboPanel, generalPanel, privacyPanel };
            foreach (Panel p in pages)
            {
                // A página vai até a borda da janela e a barra nativa dela fica além da borda, escondida; a barra fina
                // (pageBar) fica na margem direita, 10 px depois dos grupos, e lê a rolagem da página visível.
                p.Location = new Point(PageX, PageY);
                p.Size = new Size(ClientSize.Width - PageX + SystemInformation.VerticalScrollBarWidth, PageH);
                Controls.Add(p);
            }
            pageBar = new ThinScrollBar { BackColor = Theme.Bg, RightInset = 4, Location = new Point(PageX + PageW + 6, PageY), Size = new Size(ClientSize.Width - PageX - PageW - 6, PageH) };
            Controls.Add(pageBar);

            // --- Navegação lateral ---
            nav = new[]
            {
                new NavItem { Text = "Performance", Glyph = GlyphKind.Bolt, AccentA = pa, AccentB = pb },
                new NavItem { Text = L.T("Dia a dia", "Daily"), Glyph = GlyphKind.Monitor, AccentA = a, AccentB = b },
                new NavItem { Text = L.T("Jogos", "Games"), Glyph = GlyphKind.Mouse, AccentA = pa, AccentB = pb },
                new NavItem { Text = "Turbo", Glyph = GlyphKind.Gauge, AccentA = pa, AccentB = pb },
                new NavItem { Text = L.T("Geral", "General"), Glyph = GlyphKind.Gear, AccentA = a, AccentB = b },
                new NavItem { Text = L.T("Privacidade", "Privacy"), Glyph = GlyphKind.Shield, AccentA = a, AccentB = b }
            };
            for (int i = 0; i < nav.Length; i++)
            {
                int index = i;
                // Sem divisória vertical: 24 px de espaço até as páginas.
                nav[i].Location = new Point(16, PageY + i * 44);
                nav[i].Size = new Size(PageX - 24 - 16, 36);
                nav[i].Click += (s, e) => SelectPage(index);
                Controls.Add(nav[i]);
            }

            // --- Rodapé: divisória e os botões. Salvar fica na cor da página mostrada: um só acento por tela. ---
            Controls.Add(new Rule { Location = new Point(0, PageY + PageH), Size = new Size(ClientSize.Width, 1) });
            Mode active = cfg.LastMode ?? Mode.DiaADia;
            var cancel = new NeonButton { Text = L.T("Cancelar", "Cancel"), Location = new Point(372, 630), Size = new Size(124, 36) };
            save = new NeonButton { Text = L.T("Salvar", "Save"), Style = ButtonStyle.Primary, Location = new Point(504, 630), Size = new Size(136, 36), AccentA = Theme.AccentA(active), AccentB = Theme.AccentB(active) };
            cancel.Click += (s, e) => Close();
            save.Click += (s, e) => SaveAndClose();
            Controls.Add(cancel);
            Controls.Add(save);
            TitleBar.AccentA = Theme.AccentA(active);
            TitleBar.AccentB = Theme.AccentB(active);

            SelectPage(0);
            FinishLayout();
        }

        /// <summary>Os três textos possíveis do estado do Caça-travadinhas (permitido, pedido, não pedido).</summary>
        private static string[] FrameStatusTexts()
        {
            return new[]
            {
                L.T("Ligado: mede os quadros em toda partida (FPS, 1% low e travadinhas) e mostra no Raio-X.",
                    "On: measures frames in every match (FPS, 1% low and stutters) and shows them in the X-ray."),
                L.T("Permissão dada. Falta sair e entrar de novo na conta do Windows para valer.",
                    "Permission granted. Sign out and back in to Windows for it to apply."),
                L.T("Precisa de uma permissão única do Windows (pede o administrador uma vez). Não entra no jogo.",
                    "Needs a one-time Windows permission (asks for the administrator once). It does not touch the game.")
            };
        }

        /// <summary>Estado da permissão do Caça-travadinhas e o que o botão faz.</summary>
        private void ShowFramePermission()
        {
            bool allowed = FramePermission.Allowed;
            string[] texts = FrameStatusTexts();
            if (allowed)
            {
                cfg.FramePermissionAsked = false;
                framesStatus.Text = texts[0];
            }
            else if (cfg.FramePermissionAsked)
            {
                framesStatus.Text = texts[1];
            }
            else
            {
                framesStatus.Text = texts[2];
            }
            framesButton.Text = allowed || cfg.FramePermissionAsked ? L.T("Tirar", "Remove") : L.T("Permitir", "Allow");
            framesButton.Invalidate();
        }

        private void ChangeFramePermission()
        {
            bool allow = !(FramePermission.Allowed || cfg.FramePermissionAsked);
            bool alreadyMember;
            string error = FramePermission.Change(allow, out alreadyMember);
            if (error != null)
            {
                Log.Warn(L.T("Caça-travadinhas: permissão não mudou (", "Stutter finder: permission not changed (") + error + ")");
                ConfirmDialog.Inform(this, L.T("Caça-travadinhas", "Stutter finder"), L.T("A permissão não mudou: ", "The permission did not change: ") + error + ".");
                return;
            }
            cfg.FramePermissionAsked = allow;
            // Só o Change muda; o Asked some quando a permissão passa a valer, este fica. Só marca quando foi o app que pôs:
            // se a conta já estava no grupo (administrador, política do PC), a desinstalação não tira.
            if (!allow) cfg.FramePermissionGranted = false;
            else if (!alreadyMember) cfg.FramePermissionGranted = true;
            try { cfg.Save(); } catch (Exception ex) { Log.Error(L.T("Salvar configuração", "Save settings"), ex); } // vale mesmo com Cancelar
            Log.Info(allow && alreadyMember ? L.T("Caça-travadinhas: a conta já estava no grupo Performance Log Users", "Stutter finder: the account was already in Performance Log Users")
                   : allow ? L.T("Caça-travadinhas: conta posta no grupo Performance Log Users", "Stutter finder: account added to Performance Log Users")
                           : L.T("Caça-travadinhas: conta tirada do grupo Performance Log Users", "Stutter finder: account removed from Performance Log Users"));
            ShowFramePermission();
            if (allow)
                ConfirmDialog.Inform(this, L.T("Caça-travadinhas", "Stutter finder"),
                    L.T("Pronto. Saia e entre de novo na conta do Windows (ou reinicie o PC) para começar a medir.",
                        "Done. Sign out and back in to Windows (or restart the PC) to start measuring."));
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

        private static ToggleRow MakeRow(string title, string subtitle, Color a, Color b)
        {
            return SettingsLayout.Toggle(title, subtitle, a, b);
        }

        private void SelectPage(int index)
        {
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visible = i == index;
                nav[i].Selected = i == index;
            }
            pageBar.Target = pages[index];
            // Salvar na paleta da página (Performance, Jogos e Turbo em laranja; Dia a dia, Geral e Privacidade em azul).
            save.AccentA = nav[index].AccentA;
            save.AccentB = nav[index].AccentB;
            save.Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Windows 10 (sem a borda do sistema): a barra fina cobre a coluna da borda que a janela pinta.
            pageBar.EdgeColor = SystemBorder ? Color.Empty : Theme.Border;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            // Cada página só aparece até a barra fina (região da janela): a parte de baixo dela, com a barra nativa,
            // fica recortada, e a barra fina na margem não fica coberta pela página.
            if (pageBar == null || pages == null) return;
            foreach (Panel p in pages)
            {
                var visible = new Size(Math.Max(1, pageBar.Left - p.Left), p.Height);
                Size was;
                if (clipped.TryGetValue(p, out was) && was == visible) continue;
                clipped[p] = visible;
                Region old = p.Region;
                p.Region = new Region(new Rectangle(Point.Empty, visible));
                if (old != null) old.Dispose();
            }
        }

        private readonly Dictionary<Panel, Size> clipped = new Dictionary<Panel, Size>();

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
            // Antes de gravar as páginas: no Dia a dia, quem abriu a janela traz o mouse de agora do Windows para o perfil
            // (a página grava por cima só o que a pessoa mudou).
            var saving = Saving;
            if (saving != null) saving();

            perfPanel.SaveTo(cfg.Performance);
            dailyPanel.SaveTo(cfg.Daily);
            gamesPanel.SaveTo(cfg);

            cfg.MouseOnlyWhenGameFocused = focusRow.Switch.Checked;
            cfg.GuardMouse = guardRow.Switch.Checked;
            cfg.AppsToClose = ReadList(appsToClose);
            cfg.BoostGamePriority = boostRow.Switch.Checked;
            cfg.BlockWinKey = winKeyRow.Switch.Checked;
            cfg.DisableAccessibilityHotkeys = stickyRow.Switch.Checked;
            cfg.SilenceNotifications = silenceRow.Switch.Checked;
            cfg.CalmBackground = calmRow.Switch.Checked;
            cfg.CloseAppsOnPerformance = closeRow.Switch.Checked;
            cfg.ForceCloseApps = forceRow.Switch.Checked;
            cfg.MonitorCheck = monitorCheckRow.Switch.Checked;
            cfg.CheckForUpdates = updateRow.Switch.Checked;
            cfg.AutoUpdate = autoUpdateRow.Switch.Checked;
            cfg.MeasureNetwork = networkRow.Switch.Checked;
            cfg.KeepMatchHistory = historyRow.Switch.Checked;
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
            }
            catch (Exception ex)
            {
                Log.Error(L.T("Salvar configurações", "Save settings"), ex);
                ConfirmDialog.Inform(this, L.T("Não foi possível salvar", "Could not save"), ex.Message);
                return;
            }
            Log.Info(L.T("Configurações salvas", "Settings saved"));

            // O config já foi salvo: uma falha aqui não pode impedir o app de usar o que foi salvo (Saved).
            try
            {
                Startup.SetEnabled(startWithWindows.Switch.Checked);
            }
            catch (Exception ex)
            {
                Log.Error(L.T("Iniciar com o Windows", "Start with Windows"), ex);
                ConfirmDialog.Inform(this, L.T("Iniciar com o Windows", "Start with Windows"),
                    L.T("As configurações foram salvas, mas não deu para mudar o \"Iniciar com o Windows\": ",
                        "Settings were saved, but \"Start with Windows\" could not be changed: ") + ex.Message);
            }

            var handler = Saved;
            if (handler != null) handler();
            Close();
        }

        /// <summary>Divisória de 1 px (rodapé).</summary>
        private class Rule : Control
        {
            public Rule() { BackColor = Theme.Border; }
        }
    }

    /// <summary>
    /// Montagem das páginas dos Ajustes (px a 96 dpi, antes da escala da janela): cabeçalho de grupo, grupo de linhas,
    /// linhas feitas de vários controles e legendas medidas pelo texto.
    /// </summary>
    internal static class SettingsLayout
    {
        /// <summary>Largura do conteúdo das páginas.</summary>
        public const int Width = 440;
        /// <summary>Altura visível das páginas.</summary>
        public const int PageHeight = 554;
        private const int HeaderH = 22, HeaderAbove = 24, HeaderBelow = 8, CaptionGap = 6;

        /// <summary>
        /// Página que chega perto do rodapé: 16 px de margem embaixo (rola o que faltar), para o fim não ficar colado
        /// na divisória do rodapé.
        /// </summary>
        public static void FinishPage(Panel page, int bottom)
        {
            page.AutoScrollMargin = bottom > PageHeight - Theme.Space.S16 ? new Size(0, Theme.Space.S16) : Size.Empty;
        }

        /// <summary>Página dos Ajustes: rola (barra escura) quando o conteúdo passa da altura.</summary>
        public static Panel NewPage()
        {
            var page = new Panel { BackColor = Theme.Bg };
            MakeScrollable(page);
            return page;
        }

        public static void MakeScrollable(Panel page)
        {
            page.AutoScroll = true;
            Theme.DarkScroll(page);
        }

        /// <summary>Cabeçalho de grupo: UiSemibold 11, Text, sentence case.</summary>
        public static SectionLabel Header(string text)
        {
            return new SectionLabel { Text = text, Size = new Size(Width, HeaderH) };
        }

        /// <summary>Título de uma linha feita de vários controles (o mesmo estilo do título do ToggleRow).</summary>
        public static SectionLabel RowTitle(string text)
        {
            return new SectionLabel { Text = text, RowTitle = true, Size = new Size(Width - SettingsGroup.Inset * 2, 20) };
        }

        /// <summary>Linha com chave: 56 px (com subtítulo).</summary>
        public static ToggleRow Toggle(string title, string subtitle, Color a, Color b)
        {
            var row = new ToggleRow { Title = title, Subtitle = subtitle, Size = new Size(Width, string.IsNullOrEmpty(subtitle) ? 44 : 56) };
            row.SetAccents(a, b);
            return row;
        }

        /// <summary>Legenda (Ui 8.5, TextSecondary) com a altura do texto mais comprido entre ele e as variantes.</summary>
        public static Label Caption(string text, int width, params string[] variants)
        {
            return Caption(Theme.TypeScale.Caption, text, width, variants);
        }

        public static Label Caption(float size, string text, int width, params string[] variants)
        {
            Font font = Theme.Ui(size);
            int h = Theme.TextHeight(text, font, width);
            if (variants != null)
                foreach (string v in variants) h = Math.Max(h, Theme.TextHeight(v, font, width));
            return new Label
            {
                Text = text ?? "",
                Size = new Size(width, h),
                ForeColor = Theme.TextSecondary,
                BackColor = Theme.Surface,
                Font = font,
                UseMnemonic = false
            };
        }

        /// <summary>Grupo com as linhas dadas, na largura da página.</summary>
        public static SettingsGroup Group(params Control[] rows)
        {
            var group = new SettingsGroup(Width);
            foreach (Control row in rows) group.AddRow(row);
            return group;
        }

        /// <summary>Linha de grupo com os controles já posicionados dentro dela.</summary>
        public static GroupCell Cell(int height, params Control[] children)
        {
            var cell = new GroupCell { Size = new Size(Width, height) };
            foreach (Control c in children) cell.Controls.Add(c);
            return cell;
        }

        /// <summary>Título com o valor à direita, o slider embaixo (trilha alinhada ao texto) e a legenda opcional.</summary>
        public static GroupCell SliderCell(SectionLabel title, NeonSlider slider, Label caption)
        {
            const int inset = SettingsGroup.Inset;
            title.Location = new Point(inset, 12);
            title.Size = new Size(Width - inset * 2, 20);
            // O slider desenha a trilha 12 px para dentro: assim ela começa e termina no recuo do texto.
            slider.Location = new Point(inset - 12, 34);
            slider.Size = new Size(Width - (inset - 12) * 2, 28);
            int bottom = slider.Bottom + 8;
            var cell = Cell(0, title, slider);
            if (caption != null)
            {
                caption.Location = new Point(inset, slider.Bottom);
                caption.Width = Width - inset * 2;
                cell.Controls.Add(caption);
                bottom = caption.Bottom + 10;
            }
            cell.Height = bottom;
            return cell;
        }

        /// <summary>Título e, embaixo, um controle de largura inteira (ex.: Segmented de 36 px).</summary>
        public static GroupCell ControlCell(SectionLabel title, Control control)
        {
            const int inset = SettingsGroup.Inset;
            title.Location = new Point(inset, 12);
            title.Size = new Size(Width - inset * 2, 20);
            control.Location = new Point(inset, 38);
            control.Size = new Size(Width - inset * 2, 36);
            return Cell(control.Bottom + 14, title, control);
        }

        /// <summary>Linha de ícone com um texto de várias linhas (a altura sai do texto).</summary>
        public static IconRow IconLine(GlyphKind glyph, string text)
        {
            return new IconRow { Glyph = glyph, Text = text, Size = new Size(Width, IconRow.HeightFor(text, Width)) };
        }

        /// <summary>Põe o cabeçalho (opcional) e o bloco a partir de y; devolve o pé do bloco.</summary>
        public static int Place(Control page, int y, SectionLabel header, Control block)
        {
            if (header != null)
            {
                if (y > 0) y += HeaderAbove;
                header.Location = new Point(0, y);
                header.Size = new Size(Width, HeaderH);
                page.Controls.Add(header);
                y += HeaderH + HeaderBelow;
            }
            else if (y > 0)
            {
                y += Theme.Space.BetweenGroups;
            }
            block.Location = new Point(0, y);
            if (block.Parent != page) page.Controls.Add(block);
            return block.Bottom;
        }

        /// <summary>Legenda logo abaixo de um grupo (6 px); devolve o pé dela.</summary>
        public static int PlaceCaption(Control page, int y, Label caption)
        {
            caption.Location = new Point(0, y + CaptionGap);
            if (caption.Parent != page) page.Controls.Add(caption);
            return caption.Bottom;
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
        private readonly SectionLabel listLabel;
        private readonly TextBox nameBox;
        private readonly ToggleRow customRow;
        private readonly Panel editor;
        private readonly NeonSlider speed;
        private readonly SectionLabel speedLabel;
        private readonly ToggleRow precision;
        private readonly Segmented plan;
        private readonly Segmented resolution;
        private readonly SectionLabel fpsLabel;
        private readonly Segmented fps;
        private readonly Label emptyNote;
        private IFpsLimiter limiter;
        private bool limiterChecked;
        private Entry current;
        private readonly bool autoLoaded; // troca automática ao abrir: a da bandeja, mudada depois, não é desfeita

        private readonly SettingsGroup customGroup; // a linha "Configurações próprias para ..." (some sem jogo escolhido)
        private readonly EmptyState emptyBox;       // moldura do emptyNote

        /// <summary>Altura de cada jogo na lista (px a 96 dpi) e quantos aparecem sem rolar.</summary>
        private const int ItemHeight = 34, VisibleItems = 6;

        public GamesPanel(AppConfig cfg, SettingsForm owner)
        {
            this.cfg = cfg;
            BackColor = Theme.Bg;
            SettingsLayout.MakeScrollable(this); // com o editor aberto a página passa da altura
            Color a = Theme.PerfA, b = Theme.PerfB;
            const int width = SettingsLayout.Width;

            foreach (string g in cfg.Games)
            {
                GameProfile gp = cfg.FindGameProfile(g);
                entries.Add(new Entry { Name = g, Custom = gp != null ? gp.Profile.Clone() : null });
            }

            autoSwitch = SettingsLayout.Toggle(L.T("Trocar ao abrir um jogo", "Switch when a game opens"), L.T("Ativa Performance quando um jogo da lista abrir", "Turns on Performance when a game from the list opens"), a, b);
            autoSwitch.Switch.SetChecked(cfg.AutoSwitch, false);
            autoLoaded = cfg.AutoSwitch;

            listLabel = new SectionLabel { Text = L.T("Jogos (começo do nome do processo)", "Games (start of the process name)"), RightColor = Theme.DangerText };
            list = new ListBox
            {
                BorderStyle = BorderStyle.None,
                IntegralHeight = true, // só jogos inteiros: nenhum cortado no pé
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = ItemHeight,
                Font = Theme.Ui(10f)
            };
            // A altura do item não acompanha a escala da janela sozinha.
            list.HandleCreated += (s, e) => list.ItemHeight = Math.Max(ItemHeight, (int)Math.Round(ItemHeight * list.DeviceDpi / 96f));
            list.DrawItem += DrawItem;
            list.SelectedIndexChanged += (s, e) => { list.Invalidate(); Select(list.SelectedIndex); };
            // FieldHost põe a lista 10 px para dentro em cima e embaixo: seis jogos inteiros.
            var listHost = new FieldHost(list) { Size = new Size(width, ItemHeight * VisibleItems + 20), Accent = b };

            nameBox = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Ui(10f) };
            nameBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                // Depois da tecla: o aviso de linha ampla abre uma caixa, e aberta aqui dentro o Enter ainda chegaria à
                // caixa de texto (o bipe de erro do Windows).
                BeginInvoke((Action)(() => AddName(nameBox.Text)));
            };
            string example = L.T("Nome do processo, ex.: cs2", "Process name, e.g. cs2");
            Theme.CueBanner(nameBox, example);
            // Incluir e Buscar são ações da linha (secundárias: o único primário da tela é o Salvar); Tirar é destrutivo.
            const int addW = 72, pickW = 72, removeW = 70, gap = 8;
            var nameHost = new FieldHost(nameBox) { Size = new Size(width - addW - pickW - removeW - gap * 3, 36), Accent = b, Placeholder = example };
            var add = new NeonButton { Text = L.T("Incluir", "Add"), Style = ButtonStyle.Secondary, Size = new Size(addW, 36), AccentA = a, AccentB = b };
            var pick = new NeonButton { Text = L.T("Buscar", "Find"), Style = ButtonStyle.Secondary, Size = new Size(pickW, 36), AccentA = a, AccentB = b };
            var remove = new NeonButton { Text = L.T("Tirar", "Remove"), Style = ButtonStyle.Danger, Size = new Size(removeW, 36) };
            add.Click += (s, e) => AddName(nameBox.Text);
            pick.Click += (s, e) =>
            {
                foreach (string p in owner.PickGames(entries.ConvertAll(x => x.Name))) AddName(p);
            };
            remove.Click += (s, e) => RemoveSelected();

            customRow = SettingsLayout.Toggle("", L.T("Desligado: usa as configurações do modo Performance", "Off: uses the Performance mode settings"), a, b);
            customRow.Switch.CheckedChanged += OnCustomToggled;
            customGroup = SettingsLayout.Group(customRow);

            speedLabel = SettingsLayout.RowTitle(L.T("Velocidade do ponteiro", "Pointer speed"));
            speed = new NeonSlider { Minimum = 1, Maximum = 20, Marker = 10, AccentA = a, AccentB = b };
            speed.ValueChanged += () => { speedLabel.RightText = speed.Value + " / 20"; speedLabel.Invalidate(); };
            precision = SettingsLayout.Toggle(L.T("Aceleração do Windows", "Windows acceleration"), L.T("\"Aprimorar precisão do ponteiro\"", "\"Enhance pointer precision\""), a, b);
            var planLabel = SettingsLayout.RowTitle(L.T("Plano de energia", "Power plan"));
            plan = new Segmented { Options = new[] { L.T("Manter", "Keep"), L.T("Equilibrado", "Balanced"), L.T("Alto", "High"), L.T("Máximo", "Ultimate") }, AccentA = a, AccentB = b };
            // Resolução do monitor principal com o jogo aberto (4:3 esticado depende da escala da placa em "Tela cheia").
            var resolutionLabel = SettingsLayout.RowTitle(L.T("Resolução com o jogo aberto", "Resolution with the game open"));
            resolutionLabel.RightText = L.T("monitor principal", "main monitor");
            resolutionLabel.RightColor = Theme.TextTertiary;
            var resolutionNames = new string[GameResolution.Options.Length];
            for (int i = 0; i < resolutionNames.Length; i++)
                resolutionNames[i] = GameResolution.Options[i] == null ? L.T("Nativa", "Native") : GameResolution.Format(GameResolution.Options[i]);
            resolution = new Segmented { Options = resolutionNames, AccentA = a, AccentB = b };
            // Limite de FPS no driver da placa (NVIDIA ou Intel Arc): gravado ao salvar, vale da próxima vez que o jogo abrir.
            fpsLabel = SettingsLayout.RowTitle(L.T("Limite de FPS (beta)", "FPS limit (beta)"));
            fpsLabel.RightColor = Theme.TextTertiary;
            var fpsNames = new string[FpsCap.Options.Length];
            for (int i = 0; i < fpsNames.Length; i++)
                fpsNames[i] = FpsCap.Options[i] == 0 ? L.T("Sem", "Off") : FpsCap.Options[i] == FpsCap.Auto ? "Auto" : FpsCap.Options[i].ToString();
            fps = new Segmented { Options = fpsNames, AccentA = a, AccentB = b };
            fps.SelectedIndexChanged += ShowFpsTarget;
            var editorGroup = SettingsLayout.Group(
                SettingsLayout.SliderCell(speedLabel, speed, null),
                precision,
                SettingsLayout.ControlCell(planLabel, plan),
                SettingsLayout.ControlCell(resolutionLabel, resolution),
                SettingsLayout.ControlCell(fpsLabel, fps));
            editor = new Panel { BackColor = Theme.Bg, Size = editorGroup.Size };
            editor.Controls.Add(editorGroup);

            // Sem jogo escolhido: estado vazio (Surface com borda) com o ícone e o texto centralizados.
            const int emptyH = 96, emptyTextW = width - 48;
            emptyNote = new Label
            {
                Text = L.T("Escolha um jogo na lista para dar a ele configurações próprias. Rolagem e USB seguem o modo Performance.", "Pick a game in the list to give it its own settings. Scroll and USB follow Performance mode."),
                ForeColor = Theme.TextSecondary,
                Font = Theme.Ui(9f),
                BackColor = Theme.Surface,
                TextAlign = ContentAlignment.TopCenter,
                UseMnemonic = false
            };
            int emptyTextH = Theme.TextHeight(emptyNote.Text, emptyNote.Font, emptyTextW);
            int glyphTop = Math.Max(8, (emptyH - 20 - 8 - emptyTextH) / 2);
            emptyNote.Bounds = new Rectangle(24, glyphTop + 28, emptyTextW, emptyTextH);
            emptyBox = new EmptyState { Glyph = GlyphKind.Mouse, GlyphTop = glyphTop, Size = new Size(width, emptyH) };
            emptyBox.Controls.Add(emptyNote);

            // Troca automática; a lista com o título (o aviso de linha ampla fica à direita dele); nome e botões; embaixo,
            // o jogo escolhido (ou o estado vazio) e as configurações próprias dele.
            int y = SettingsLayout.Place(this, 0, SettingsLayout.Header(L.T("Troca automática", "Automatic switch")), SettingsLayout.Group(autoSwitch));
            y = SettingsLayout.Place(this, y, listLabel, listHost);
            y += Theme.Space.InsideGroup;
            nameHost.Location = new Point(0, y);
            add.Location = new Point(nameHost.Right + gap, y);
            pick.Location = new Point(add.Right + gap, y);
            remove.Location = new Point(pick.Right + gap, y);
            y = nameHost.Bottom;
            SettingsLayout.Place(this, y, null, emptyBox);
            y = SettingsLayout.Place(this, y, null, customGroup);
            editor.Location = new Point(0, y + Theme.Space.InsideGroup);
            SettingsLayout.FinishPage(this, editor.Bottom); // o editor aberto passa da altura da página

            foreach (Control c in new Control[] { nameHost, add, pick, remove, editor })
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
            ShowBroad(has ? current.Name : null);
            customRow.Visible = has;
            customGroup.Visible = has;
            emptyNote.Visible = !has;
            emptyBox.Visible = !has;
            if (has)
            {
                customRow.Title = L.T("Configurações próprias para ", "Own settings for ") + current.Name;
                customRow.Switch.SetChecked(current.Custom != null, false);
                customRow.Invalidate();
                LoadEditor();
            }
            editor.Visible = has && current.Custom != null;
        }

        /// <summary>No título da lista, o que a linha selecionada também pega (vazio se ela é boa).</summary>
        private void ShowBroad(string entry)
        {
            List<string> hits = entry != null && GameListCheck.TooBroad(entry) ? GameListCheck.AlsoCatches(entry, null) : null;
            if (hits == null) listLabel.RightText = "";
            else if (hits.Count == 0) listLabel.RightText = L.T("curta demais", "too short");
            else
                listLabel.RightTextOptions = new[]
                {
                    L.T("também pega ", "also catches ") + GameListCheck.Names(hits),
                    L.T("pega ", "catches ") + hits[0] + (hits.Count > 1 ? L.T(" e mais ", " and ") + (hits.Count - 1) + L.T("", " more") : ""),
                    L.T("pega demais", "too broad")
                };
            listLabel.Invalidate();
        }

        /// <summary>Pergunta se inclui uma linha ampla mesmo assim (o aviso é o texto). Os testes trocam a pergunta.</summary>
        internal Func<string, bool> ConfirmBroad;

        private bool AskBroad(string warning)
        {
            if (ConfirmBroad != null) return ConfirmBroad(warning);
            return ConfirmDialog.Ask(FindForm(), L.T("Linha ampla demais", "Line too broad"), warning, L.T("Incluir assim", "Add anyway"));
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
            int res = Array.FindIndex(GameResolution.Options, o => o == current.Custom.Resolution);
            resolution.SelectedIndex = res < 0 ? 0 : res;
            int cap = Array.IndexOf(FpsCap.Options, current.Custom.FpsCap);
            fps.SelectedIndex = cap < 0 ? 0 : cap;
            ShowFpsTarget();
        }

        /// <summary>
        /// O executável que aparece ao lado do limite: o que vai receber o limite (FpsCap.TargetExe). Sem o jogo ter
        /// sido visto aberto é um palpite (o nome da lista com ".exe"), marcado com "?". Só pede para abrir o jogo quando
        /// há limite escolhido e o item é com certeza um começo de nome (ClearlyPrefix): aí o palpite certamente erra.
        /// </summary>
        internal static string FpsTargetText(AppConfig cfg, string game, int choice)
        {
            string exe = FpsCap.TargetExe(cfg, game);
            if (cfg.SeenGameExe(game) != null) return exe;
            if (choice != 0 && ClearlyPrefix(game)) return L.T("abra o jogo uma vez", "open the game once");
            return exe + "?";
        }

        /// <summary>
        /// O item da lista termina num separador ("FiveM_", "Jogo-"): nenhum executável de jogo termina assim, então é
        /// só o começo do nome. Fora isso não dá para saber ("cs2" é o nome inteiro, "FiveM" é começo de
        /// "FiveM_b3095_GTAProcess"), e o palpite com ".exe" fica valendo.
        /// </summary>
        internal static bool ClearlyPrefix(string game)
        {
            string name = GameDetector.Normalize(game);
            return name.Length > 0 && "-_.".IndexOf(name[name.Length - 1]) >= 0;
        }

        /// <summary>
        /// Versões do texto ao lado do limite, da mais longa para a mais curta (SectionLabel.RightTextOptions): sem
        /// espaço, perde primeiro a placa e depois a taxa; o executável (e o "?" do palpite) fica sempre inteiro.
        /// </summary>
        internal static string[] FpsLabelTexts(string card, int choice, int rate, string target)
        {
            if (choice == 0) return new[] { card + " · " + target, target };
            string fps = rate > 0 ? rate + " FPS" : L.T("taxa desconhecida", "unknown rate");
            return new[] { fps + " · " + card + " · " + target, fps + " · " + target, target };
        }

        /// <summary>Ao lado do título do limite: para qual executável e placa ele vai (ou que a placa não tem limitador).</summary>
        private void ShowFpsTarget()
        {
            if (current == null) return;
            if (!limiterChecked)
            {
                limiterChecked = true;
                limiter = FpsCap.Detect();
            }
            int choice = FpsCap.Options[Math.Max(0, fps.SelectedIndex)];
            string target = FpsTargetText(cfg, current.Name, choice);
            if (limiter == null)
                fpsLabel.RightText = L.T("esta placa não tem limitador", "this card has no limiter");
            else
                fpsLabel.RightTextOptions = FpsLabelTexts(limiter.Name, choice, choice == 0 ? 0 : FpsCap.Resolve(choice, FpsCap.PrimaryRefresh()), target);
            fpsLabel.RightColor = limiter == null ? Theme.TextTertiary : Theme.Text;
            fpsLabel.Invalidate();
        }

        private void CommitEditor()
        {
            if (current == null || current.Custom == null) return;
            current.Custom.PointerSpeed = speed.Value;
            current.Custom.EnhancePointerPrecision = precision.Switch.Checked;
            current.Custom.PowerPlan = (PowerPlanOption)Math.Max(0, plan.SelectedIndex);
            current.Custom.Resolution = GameResolution.Options[Math.Max(0, resolution.SelectedIndex)];
            current.Custom.FpsCap = FpsCap.Options[Math.Max(0, fps.SelectedIndex)];
        }

        private void AddName(string raw)
        {
            string name = GameDetector.Normalize(raw);
            if (name.Length == 0) return;
            int existing = entries.FindIndex(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing < 0)
            {
                // Uma linha que pega mais que o jogo (curta, ou começo do Steam, do navegador...) pede confirmação.
                string warning = GameListCheck.TooBroad(name) ? GameListCheck.Warning(name, OpenNowForWarning()) : null;
                if (warning != null && !AskBroad(warning)) return; // fica na caixa para corrigir
                entries.Add(new Entry { Name = name });
                existing = entries.Count - 1;
            }
            nameBox.Text = "";
            RefreshList(existing);
        }

        /// <summary>Os programas com janela aberta, só para dar exemplos no aviso de linha curta. Os testes trocam.</summary>
        internal Func<List<string>> OpenNow;

        private List<string> OpenNowForWarning()
        {
            try { return OpenNow != null ? OpenNow() : GameDetector.ListWindowedGameCandidates(); }
            catch { return null; }
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
            // Só se a pessoa mexeu aqui: a troca automática pode ter sido ligada ou desligada pela bandeja com a janela aberta.
            if (autoSwitch.Switch.Checked != autoLoaded) target.AutoSwitch = autoSwitch.Switch.Checked;
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
            bool broad = GameListCheck.TooBroad(entry.Name);
            string own = entry.Custom != null ? L.T("Próprio", "Own") : null;
            string tag = broad ? (own != null ? own + " · " : "") + L.T("Amplo", "Broad") : own;
            ListPainter.Draw(e, entry.Name, sel, tag, Theme.PerfA, Theme.PerfB, broad ? (Color?)Theme.Danger : null);
        }
    }

    /// <summary>Desenho dos itens das listas no tema escuro.</summary>
    public static class ListPainter
    {
        /// <param name="tagColor">Cor da etiqueta quando ela é um aviso (null = as cores da lista).</param>
        public static void Draw(DrawItemEventArgs e, string text, bool selected, string tag, Color a, Color b, Color? tagColor = null)
        {
            Graphics g = e.Graphics;
            Theme.HighQuality(g);
            ModePalette p = Theme.PaletteFor(a);
            using (var bg = new SolidBrush(Theme.Surface2)) g.FillRectangle(bg, e.Bounds);
            var r = new RectangleF(e.Bounds.X + 0.5f, e.Bounds.Y + 1.5f, e.Bounds.Width - 3, e.Bounds.Height - 3);
            if (selected)
            {
                // Seleção discreta: tinta do modo, barra de 3 px à esquerda e o texto normal (sem gradiente).
                Theme.FillRounded(g, r, Theme.Radius.Sm, p.Tint);
                using (var bar = new SolidBrush(p.Fill))
                    g.FillRectangle(bar, r.X, r.Y + 5, 3, Math.Max(4, r.Height - 10));
            }
            float tagW = 0;
            if (tag != null)
            {
                // Selo legível: números na fonte de números; palavras em semibold.
                bool numeric = tag.Length > 0 && char.IsDigit(tag[0]);
                Font tagFont = numeric ? Theme.Num(Theme.TypeScale.Caption) : Theme.UiSemibold(Theme.TypeScale.Caption);
                SizeF ts;
                using (var format = new StringFormat(StringFormatFlags.NoWrap)) ts = g.MeasureString(tag, tagFont, PointF.Empty, format);
                float badgeH = Math.Min(r.Height - 4, Math.Max(18f, (float)Math.Ceiling(tagFont.GetHeight(g)) + 3));
                tagW = ts.Width + 12;
                var badge = new RectangleF(r.Right - tagW - 6, r.Y + (r.Height - badgeH) / 2f, tagW, badgeH);
                Color tint = tagColor ?? p.Fill;
                Color fg = !tagColor.HasValue ? p.OnSurface : tint.ToArgb() == Theme.Danger.ToArgb() ? Theme.DangerText : tint;
                Color fill = Theme.Alpha(tint, 36);
                if (numeric && !tagColor.HasValue && !selected)
                {
                    // Número (ex.: "144 FPS") é dado, não alerta: selo neutro; só o item escolhido leva a cor da lista.
                    fill = Theme.Alpha(Color.White, 8);
                    fg = Theme.TextSecondary;
                }
                Theme.FillRounded(g, badge, badgeH / 2f, fill);
                Theme.DrawText(g, tag, tagFont, fg, badge, StringAlignment.Center, StringAlignment.Center, false);
            }
            Theme.DrawText(g, text, Theme.Ui(10f), Theme.Text,
                new RectangleF(r.X + 12, r.Y, r.Width - 24 - tagW, r.Height), StringAlignment.Near, StringAlignment.Center, false);
        }
    }

    /// <summary>Caixa de confirmação no tema do app.</summary>
    public class ConfirmDialog : GamerWindow
    {
        private ConfirmDialog(string title, string message, string confirmText, bool danger) : base(title, false)
        {
            CloseOnEscape = true;
            StartPosition = FormStartPosition.CenterParent;
            const int width = 420, textX = 76;

            // Ícone num círculo (perigo em vermelho; aviso ou informação em âmbar) e a mensagem ao lado, com a altura medida.
            Controls.Add(new IconBadge { Glyph = GlyphKind.Alert, Tone = danger ? Theme.Danger : Theme.Warning, Location = new Point(20, 60), Size = new Size(40, 40) });
            Label text = MeasuredNote(message, textX, 60, width - textX - 20, Theme.Text, 10f);
            // Uma linha só fica no meio do ícone.
            if (text.Height < 40) text.Top = 60 + (40 - text.Height) / 2;
            Controls.Add(text);

            // Botões alinhados à direita; a ação (destrutiva ou não) fica na ponta.
            int buttonsY = Math.Max(text.Bottom, 100) + 24;
            string okText = confirmText ?? "Ok";
            int okW = ButtonWidth(104, okText);
            var ok = new NeonButton
            {
                Text = okText,
                Style = danger ? ButtonStyle.DangerSolid : ButtonStyle.Primary,
                Location = new Point(width - 20 - okW, buttonsY),
                Size = new Size(okW, 36)
            };
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            if (confirmText != null)
            {
                string cancelText = L.T("Cancelar", "Cancel");
                int cancelW = ButtonWidth(104, cancelText);
                var cancel = new NeonButton { Text = cancelText, Location = new Point(ok.Left - 8 - cancelW, buttonsY), Size = new Size(cancelW, 36) };
                cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(cancel);
            }
            Controls.Add(ok);
            ClientSize = new Size(width, buttonsY + 36 + 20);
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
        private bool pageScroll; // "uma tela por vez" do Windows: continua assim até a pessoa mexer na régua
        private MouseProfile loaded; // o que a página mostrou ao abrir: o SaveTo grava só o que mudou

        private readonly InfoNote hintNote; // dica da página, sempre no fim
        private int contentBottom;          // pé do último grupo

        /// <param name="keepPlanLabel">
        /// Nome da 1ª opção de plano (não trocar). No Dia a dia é "Anterior": volta ao plano de antes do Performance.
        /// </param>
        public ProfilePanel(Color a, Color b, string hint, string keepPlanLabel = null)
        {
            BackColor = Theme.Bg;
            SettingsLayout.MakeScrollable(this);
            const int inner = SettingsLayout.Width - SettingsGroup.Inset * 2;

            speedLabel = SettingsLayout.RowTitle(L.T("Velocidade do ponteiro", "Pointer speed"));
            speed = new NeonSlider { Minimum = 1, Maximum = 20, Marker = 10, AccentA = a, AccentB = b };
            speedNote = SettingsLayout.Caption("", inner, SpeedNotes()); // alto o bastante para o texto mais comprido
            speed.ValueChanged += UpdateLabels;

            precision = SettingsLayout.Toggle(L.T("Aceleração do Windows", "Windows acceleration"),
                L.T("\"Aprimorar precisão do ponteiro\" (aceleração)", "\"Enhance pointer precision\" (acceleration)"), a, b);

            scrollLabel = SettingsLayout.RowTitle(L.T("Linhas por giro da roda", "Lines per wheel notch"));
            scroll = new NeonSlider { Minimum = 1, Maximum = 20, AccentA = a, AccentB = b };
            scroll.ValueChanged += () => pageScroll = false; // antes do UpdateLabels: o texto já sai com as linhas
            scroll.ValueChanged += UpdateLabels;

            var planLabel = SettingsLayout.RowTitle(L.T("Plano de energia", "Power plan"));
            plan = new Segmented
            {
                Options = new[] { keepPlanLabel ?? L.T("Manter", "Keep"), L.T("Equilibrado", "Balanced"), L.T("Alto", "High"), L.T("Máximo", "Ultimate") },
                AccentA = a,
                AccentB = b
            };

            usb = SettingsLayout.Toggle(L.T("USB sempre ativo", "USB always on"),
                L.T("Desativa a suspensão seletiva: o mouse nunca \"adormece\"", "Turns off selective suspend: the mouse never \"falls asleep\""), a, b);

            var pointer = SettingsLayout.Group(
                SettingsLayout.SliderCell(speedLabel, speed, speedNote),
                precision,
                SettingsLayout.SliderCell(scrollLabel, scroll, null));
            var power = SettingsLayout.Group(SettingsLayout.ControlCell(planLabel, plan), usb);
            int y = SettingsLayout.Place(this, 0, SettingsLayout.Header(L.T("Ponteiro e rolagem", "Pointer and scrolling")), pointer);
            contentBottom = SettingsLayout.Place(this, y, SettingsLayout.Header(L.T("Energia", "Power")), power);

            hintNote = new InfoNote { Text = hint ?? "", Accent = a, Visible = !string.IsNullOrEmpty(hint) };
            hintNote.Size = new Size(SettingsLayout.Width, InfoNote.HeightFor(hintNote.Text, SettingsLayout.Width));
            Controls.Add(hintNote);
            PlaceHint();
        }

        /// <summary>Mais um grupo de linhas (com cabeçalho) no fim da página; a dica continua depois dele.</summary>
        public void AddGroup(string header, params Control[] rows)
        {
            contentBottom = SettingsLayout.Place(this, contentBottom, SettingsLayout.Header(header), SettingsLayout.Group(rows));
            PlaceHint();
        }

        private void PlaceHint()
        {
            hintNote.Location = new Point(0, contentBottom + Theme.Space.BetweenGroups);
            SettingsLayout.FinishPage(this, hintNote.Visible ? hintNote.Bottom : contentBottom);
        }

        private static string[] SpeedNotes()
        {
            return new[]
            {
                L.T("1:1 — o ponteiro anda exatamente o que o sensor lê (recomendado para jogos)", "1:1 — the pointer moves exactly what the sensor reads (recommended for games)"),
                L.T("Multiplica o movimento: pode pular pixels", "Multiplies movement: may skip pixels"),
                L.T("Reduz o movimento do sensor", "Reduces the sensor movement")
            };
        }

        private void UpdateLabels()
        {
            int v = speed.Value;
            string[] notes = SpeedNotes();
            speedLabel.RightText = v + " / 20";
            speedNote.Text = v == 10 ? notes[0] : v > 10 ? notes[1] : notes[2];
            scrollLabel.RightText = pageScroll ? L.T("uma tela", "one screen")
                : scroll.Value + (scroll.Value == 1 ? L.T(" linha", " line") : L.T(" linhas", " lines"));
            speedLabel.Invalidate();
            scrollLabel.Invalidate();
        }

        public void LoadProfile(MouseProfile p)
        {
            loaded = p.Clone();
            speed.Value = Math.Max(1, Math.Min(20, p.PointerSpeed));
            precision.Switch.SetChecked(p.EnhancePointerPrecision, false);
            bool page = p.ScrollLines == MouseProfile.PageScroll;
            scroll.Maximum = Math.Max(20, p.ScrollLines);
            scroll.Value = page ? scroll.Maximum : Math.Max(1, p.ScrollLines);
            pageScroll = page; // depois do Value: a troca de valor acima apagaria a marca
            plan.SelectedIndex = (int)p.PowerPlan;
            usb.Switch.SetChecked(p.DisableUsbSelectiveSuspend, false);
            UpdateLabels();
        }

        /// <summary>
        /// Grava só o que a pessoa mudou nesta página desde o LoadProfile. O Dia a dia pode ser atualizado com o mouse do
        /// Windows enquanto a janela está aberta (um jogo entrou no Performance): o resto não volta ao valor antigo.
        /// </summary>
        public void SaveTo(MouseProfile p)
        {
            MouseProfile was = loaded ?? p.Clone();
            int scrollLines = pageScroll ? MouseProfile.PageScroll : scroll.Value;
            var chosenPlan = (PowerPlanOption)Math.Max(0, plan.SelectedIndex);
            if (speed.Value != was.PointerSpeed) p.PointerSpeed = speed.Value;
            if (precision.Switch.Checked != was.EnhancePointerPrecision) p.EnhancePointerPrecision = precision.Switch.Checked;
            if (scrollLines != was.ScrollLines) p.ScrollLines = scrollLines;
            if (chosenPlan != was.PowerPlan) p.PowerPlan = chosenPlan;
            if (usb.Switch.Checked != was.DisableUsbSelectiveSuspend) p.DisableUsbSelectiveSuspend = usb.Switch.Checked;
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
            const int width = 420;

            Label intro = MeasuredNote(L.T("Jogos da Steam e da Epic e programas abertos. Ctrl+clique escolhe vários.", "Steam and Epic games plus open programs. Ctrl+click picks several."),
                                       20, 56, width, Theme.TextSecondary, 9f);
            Controls.Add(intro);

            list = PickerList(SelectionMode.MultiExtended);
            list.DrawItem += DrawItem;
            list.DoubleClick += (s, e) => Confirm();
            list.SelectedIndexChanged += (s, e) => list.Invalidate();
            var listHost = new FieldHost(list) { Location = new Point(20, intro.Bottom + 12), Size = new Size(width, PickerItemHeight * 10 + 20), Accent = Theme.PerfB };
            Controls.Add(listHost);

            // Até 2 linhas: "N jogos instalados fora da lista (M já estão nela)."
            status = MeasuredNote(L.T("Procurando jogos instalados...", "Looking for installed games..."), 20, listHost.Bottom + 8, width, Theme.TextSecondary, 8.5f, 34);
            Controls.Add(status);

            string okText = L.T("Incluir na lista", "Add to the list");
            int okW = ButtonWidth(140, okText);
            var ok = new NeonButton { Text = okText, Style = ButtonStyle.Primary, Location = new Point(20 + width - okW, status.Bottom + 12), Size = new Size(okW, 36), AccentA = Theme.PerfA, AccentB = Theme.PerfB, Enabled = false };
            ClientSize = new Size(460, ok.Bottom + 20);
            ok.Click += (s, e) => Confirm();
            // Sem nada escolhido o botão fica apagado (o clique já não fazia nada).
            list.SelectedIndexChanged += (s, e) => ok.Enabled = list.SelectedItems.Count > 0;
            Controls.Add(ok);

            FinishLayout();
        }

        /// <summary>Altura dos itens das listas de escolha (px a 96 dpi), a mesma da lista de jogos dos Ajustes.</summary>
        internal const int PickerItemHeight = 34;

        /// <summary>Lista no estilo das Configurações: itens de 34 px, só itens inteiros, na escala da janela.</summary>
        internal static ListBox PickerList(SelectionMode mode)
        {
            var list = new ListBox
            {
                BorderStyle = BorderStyle.None,
                IntegralHeight = true,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = PickerItemHeight,
                Font = Theme.Ui(10f),
                SelectionMode = mode
            };
            // A altura do item não acompanha a escala da janela sozinha.
            list.HandleCreated += (s, e) => list.ItemHeight = Math.Max(PickerItemHeight, (int)Math.Round(PickerItemHeight * list.DeviceDpi / 96f));
            return list;
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
                list.Items.Add(new Item { Process = g.Process, Label = g.Name + "  ·  " + g.Process, Tag = g.Store });
            }
            int installed = list.Items.Count;
            foreach (string p in open)
            {
                if (!shown.Add(p)) continue;
                list.Items.Add(new Item { Process = p, Label = p, Tag = L.T("Aberto", "Open") });
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
            const int width = 360;

            Label intro = MeasuredNote(L.T("Deixe o programa ou jogo aberto e escolha o processo dele.", "Leave the program or game open and pick its process."),
                                       20, 56, width, Theme.TextSecondary, 9f);
            Controls.Add(intro);

            list = GamePickerForm.PickerList(SelectionMode.One);
            list.Items.AddRange(GameDetector.ListWindowedProcesses().ToArray());
            list.DrawItem += DrawItem;
            list.DoubleClick += (s, e) => Confirm();
            list.SelectedIndexChanged += (s, e) => list.Invalidate();
            var listHost = new FieldHost(list) { Location = new Point(20, intro.Bottom + 12), Size = new Size(width, GamePickerForm.PickerItemHeight * 10 + 20), Accent = Theme.PerfB };
            Controls.Add(listHost);

            // Aberto só pela página Turbo (modo Performance): na paleta dela.
            string okText = L.T("Adicionar", "Add");
            int okW = ButtonWidth(120, okText);
            var ok = new NeonButton { Text = okText, Style = ButtonStyle.Primary, Location = new Point(20 + width - okW, listHost.Bottom + 16), Size = new Size(okW, 36), AccentA = Theme.PerfA, AccentB = Theme.PerfB, Enabled = false };
            ClientSize = new Size(400, ok.Bottom + 20);
            ok.Click += (s, e) => Confirm();
            // Sem nada escolhido o botão fica apagado (o clique já não fazia nada).
            list.SelectedIndexChanged += (s, e) => ok.Enabled = list.SelectedIndex >= 0;
            Controls.Add(ok);

            FinishLayout();
        }

        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            ListPainter.Draw(e, (string)list.Items[e.Index], (e.State & DrawItemState.Selected) != 0, null, Theme.PerfA, Theme.PerfB);
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
