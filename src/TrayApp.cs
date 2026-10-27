using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>
    /// Controla o app: tela principal, ícone da bandeja, atalho, detecção de jogos e extras do Performance.
    /// A tela principal pode ser fechada; o app continua rodando na bandeja.
    /// </summary>
    public class TrayApp : ApplicationContext
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private readonly AppConfig cfg;
        private readonly ModeManager modes;
        private readonly MainForm mainForm;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem perfItem;
        private readonly ToolStripMenuItem dailyItem;
        private readonly ToolStripMenuItem autoItem;
        private readonly Timer gameTimer;
        private readonly Timer updateTimer;
        private readonly HotkeyWindow hotkey;
        private readonly System.Threading.RegisteredWaitHandle showWait;
        private readonly System.Threading.RegisteredWaitHandle quitWait;
        private readonly GameBooster booster = new GameBooster();
        private readonly WinKeyBlocker winKey;
        private readonly ForegroundWatcher foreground;

        private SettingsForm settingsForm;
        private PollingRateForm pollingForm;
        private ClickTestForm clickForm;
        private AimTestForm aimForm;
        private DiagnosticsForm diagForm;
        private IntPtr iconHandle = IntPtr.Zero;
        private string currentGame;
        private bool gameWasRunning;
        private bool autoActivated;
        private bool quiet = true;
        private bool trayHintShown;
        private Mode lastMode = Mode.DiaADia;
        private bool lastBoostShown;
        private List<MonitorStatus> monitors = new List<MonitorStatus>();
        private bool monitorWarned;
        private bool quitting;
        private ReleaseInfo pendingRelease; // versão nova esperando o fim do Performance para avisar
        private string balloonUrl;          // link do aviso que está na tela (só o de versão nova tem)
        private bool checkingUpdate;

        /// <param name="startHidden">true quando inicia com o Windows: fica só na bandeja.</param>
        /// <param name="showSignal">Sinal disparado quando o usuário abre o app de novo.</param>
        public TrayApp(bool startHidden, System.Threading.EventWaitHandle showSignal, System.Threading.EventWaitHandle quitSignal)
        {
            cfg = AppConfig.LoadOrCreate();
            L.Init(cfg.Language); // antes de criar qualquer tela
            Log.Info("MouseModes " + typeof(TrayApp).Assembly.GetName().Version.ToString(3) + " iniciado" + (startHidden ? " (com o Windows)" : "") + " — " + Application.ExecutablePath);

            // Se o app caiu com as notificações silenciadas, devolve agora.
            if (cfg.NotificationBackup != null && cfg.NotificationBackup.Active)
            {
                Log.Warn("O app foi fechado sem restaurar as notificações da última vez; restaurando agora");
                NotificationSilencer.Restore(cfg);
            }

            modes = new ModeManager(cfg);
            modes.RunningGame = () => currentGame;
            modes.ModeChanged += OnModeChanged;
            modes.PowerWarnings += warnings =>
            {
                if (quitting || !mainForm.IsHandleCreated || mainForm.IsDisposed) return;
                mainForm.BeginInvoke((Action)(() =>
                    ShowBalloon(L.T("Plano de energia", "Power plan"), string.Join("\n", warnings), ToolTipIcon.Warning)));
            };
            winKey = new WinKeyBlocker(() => cfg.Games);

            // Mouse do jogo só com o jogo na tela (opção): troca a cada mudança de janela em primeiro plano.
            foreground = new ForegroundWatcher();
            foreground.Changed += name => modes.SetGameFocused(GameFocused());

            // --- Tela principal ---
            mainForm = new MainForm();
            mainForm.ModeRequested += SwitchManually;
            mainForm.AutoToggled += ToggleAuto;
            mainForm.SettingsRequested += OpenSettings;
            mainForm.PollingRequested += OpenPolling;
            mainForm.ClickTestRequested += OpenClickTest;
            mainForm.AimTestRequested += OpenAimTest;
            mainForm.QuitRequested += Quit;
            mainForm.HiddenToTray += OnHiddenToTray;
            mainForm.MonitorFixRequested += FixMonitors;
            mainForm.ShowAuto(cfg.AutoSwitch);
            IntPtr forceHandle = mainForm.Handle; // garante o handle para BeginInvoke

            // --- Monitores: checa agora e sempre que a configuração de vídeo mudar ---
            RefreshMonitors();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;

            // --- Menu da bandeja ---
            var openItem = new ToolStripMenuItem(L.T("Abrir MouseModes", "Open MouseModes"), null, (s, e) => ShowMain());
            openItem.Font = new Font(openItem.Font, FontStyle.Bold);
            perfItem = new ToolStripMenuItem(L.T("Modo Performance", "Performance mode"), null, (s, e) => SwitchManually(Mode.Performance));
            dailyItem = new ToolStripMenuItem(L.T("Modo Dia a dia", "Daily mode"), null, (s, e) => SwitchManually(Mode.DiaADia));
            autoItem = new ToolStripMenuItem(L.T("Trocar sozinho quando abrir um jogo", "Switch automatically when a game opens"), null, (s, e) => ToggleAuto());
            autoItem.Checked = cfg.AutoSwitch;

            var menu = new ContextMenuStrip();
            menu.Items.Add(openItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(perfItem);
            menu.Items.Add(dailyItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(autoItem);
            menu.Items.Add(new ToolStripMenuItem(L.T("Medir polling rate...", "Measure polling rate..."), null, (s, e) => OpenPolling()));
            menu.Items.Add(new ToolStripMenuItem(L.T("Teste de clique...", "Click test..."), null, (s, e) => OpenClickTest()));
            menu.Items.Add(new ToolStripMenuItem(L.T("Testar mira e DPI...", "Aim and DPI test..."), null, (s, e) => OpenAimTest()));
            menu.Items.Add(new ToolStripMenuItem(L.T("Configurações...", "Settings..."), null, (s, e) => OpenSettings()));
            menu.Items.Add(new ToolStripMenuItem(L.T("Diagnóstico...", "Diagnostics..."), null, (s, e) => OpenDiagnostics()));
            menu.Items.Add(new ToolStripMenuItem(L.T("Procurar versão nova", "Check for a new version"), null, (s, e) => CheckForUpdate(true)));
            menu.Items.Add(LanguageMenu());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(L.T("Sair", "Quit"), null, (s, e) => Quit()));

            tray = new NotifyIcon
            {
                ContextMenuStrip = menu,
                Text = "MouseModes",
                Visible = true
            };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowMain();
            };
            // O link fica até o próximo aviso: o "fechou" de um aviso substituído pode chegar depois do novo.
            tray.BalloonTipClicked += (s, e) => OpenBalloonLink();

            // --- Atalho global ---
            hotkey = new HotkeyWindow();
            hotkey.Pressed += Toggle;
            bool hotkeyPreferred = hotkey.Register(cfg.Hotkey, cfg.CustomHotkey);
            mainForm.ShowHotkey(hotkey.ActiveCaps);

            // --- Abrir de novo o .exe traz esta janela em vez de abrir outra cópia ---
            if (showSignal != null)
            {
                showWait = System.Threading.ThreadPool.RegisterWaitForSingleObject(showSignal, (state, timedOut) =>
                {
                    if (mainForm.IsHandleCreated && !mainForm.IsDisposed)
                        mainForm.BeginInvoke((Action)ShowMain);
                }, null, -1, false);
            }

            // --- O instalador pede para sair (MouseModes.exe --quit) antes de atualizar/desinstalar ---
            if (quitSignal != null)
            {
                quitWait = System.Threading.ThreadPool.RegisterWaitForSingleObject(quitSignal, (state, timedOut) =>
                {
                    if (mainForm.IsHandleCreated && !mainForm.IsDisposed)
                        mainForm.BeginInvoke((Action)(() => { Log.Info("Saída pedida pelo instalador"); Quit(); }));
                }, null, -1, true);
            }

            // "Iniciar com o Windows" aponta para o exe; se ele mudou de lugar (ex.: instalado), corrige.
            try { Startup.RefreshPath(); } catch (Exception ex) { Log.Error("Atualizar Iniciar com o Windows", ex); }

            // Sempre começa no Dia a dia, sem regravar o Windows (só desfaz um Performance que ficou para trás).
            // Se um jogo já estiver aberto, o detector troca logo em seguida.
            modes.Start();

            gameTimer = new Timer { Interval = 3000 };
            gameTimer.Tick += (s, e) => CheckGames();
            gameTimer.Start();

            // Versão nova: confere de hora em hora se já passou um dia da última consulta. A primeira espera 1 min,
            // para não pesar na abertura junto com o Windows.
            updateTimer = new Timer { Interval = 60 * 1000 };
            updateTimer.Tick += (s, e) =>
            {
                updateTimer.Interval = 60 * 60 * 1000;
                CheckForUpdate(false);
            };
            updateTimer.Start();
            CheckGames();

            quiet = false;

            if (!startHidden) ShowMain();

            if (!hotkeyPreferred) WarnHotkey(L.T("Atalho alterado", "Hotkey changed"));
        }

        private void ShowMain()
        {
            if (mainForm.IsDisposed) return;
            RefreshMonitors();
            mainForm.ShowAndFocus();
        }

        private void OnHiddenToTray()
        {
            if (trayHintShown) return;
            trayHintShown = true;
            ShowBalloon(L.T("MouseModes continua rodando", "MouseModes is still running"),
                L.T("Ele fica aqui perto do relógio. Clique no ícone para abrir de novo.", "It stays here next to the clock. Click the icon to open it again."), ToolTipIcon.Info);
        }

        // ---------------- Modos ----------------

        private void Toggle()
        {
            SwitchManually(modes.Current == Mode.Performance ? Mode.DiaADia : Mode.Performance);
        }

        private void SwitchManually(Mode mode)
        {
            // Escolha manual vence a automática: não volta sozinho quando o jogo fechar.
            autoActivated = false;
            modes.Apply(mode, L.T("Troca manual", "Manual switch"));
        }

        private void ToggleAuto()
        {
            cfg.AutoSwitch = !cfg.AutoSwitch;
            autoItem.Checked = cfg.AutoSwitch;
            mainForm.ShowAuto(cfg.AutoSwitch);
            gameWasRunning = false; // reavalia na próxima checagem
            Log.Info("Troca automática " + (cfg.AutoSwitch ? "ligada" : "desligada"));
            SaveConfig();
            if (cfg.AutoSwitch) CheckGames();
        }

        private void CheckGames()
        {
            string game = GameDetector.FindRunning(cfg.Games);
            bool running = game != null;

            if (game != currentGame)
                Log.Info(running ? "Jogo detectado: " + game : "Jogo fechado: " + currentGame);

            // Prioridade alta: aplica a processos novos do jogo (o FiveM, por exemplo, abre o processo do GTA depois).
            if (running && modes.Current == Mode.Performance && cfg.BoostGamePriority)
                booster.Boost(cfg.Games);
            else if (!running)
                booster.Prune();

            bool gameChanged = game != currentGame;
            if (gameChanged || booster.AnyBoosted != lastBoostShown)
            {
                currentGame = game;
                lastBoostShown = booster.AnyBoosted;
                mainForm.ShowGame(game, booster.AnyBoosted);
            }

            if (running == gameWasRunning)
            {
                // Trocou de um jogo para outro sem sair do Performance: aplica o perfil do jogo novo.
                if (gameChanged && modes.Current == Mode.Performance && cfg.PerformanceFor(game) != modes.CurrentProfile)
                    modes.Apply(Mode.Performance, running ? L.T("Perfil do jogo: ", "Game profile: ") + game : L.T("Perfil Performance", "Performance profile"));
                return;
            }
            gameWasRunning = running;

            if (!cfg.AutoSwitch)
            {
                if (modes.Current == Mode.Performance && cfg.PerformanceFor(game) != modes.CurrentProfile)
                    modes.Apply(Mode.Performance, running ? L.T("Perfil do jogo: ", "Game profile: ") + game : L.T("Perfil Performance", "Performance profile"));
                return;
            }

            if (running && modes.Current != Mode.Performance)
            {
                autoActivated = true;
                modes.Apply(Mode.Performance, L.T("Jogo detectado: ", "Game detected: ") + game);
            }
            else if (running && cfg.PerformanceFor(game) != modes.CurrentProfile)
            {
                modes.Apply(Mode.Performance, L.T("Perfil do jogo: ", "Game profile: ") + game);
            }
            else if (!running && autoActivated)
            {
                autoActivated = false;
                if (modes.Current == Mode.Performance)
                    modes.Apply(Mode.DiaADia, L.T("Jogo fechado", "Game closed"));
            }
            else if (!running && modes.Current == Mode.Performance && modes.CurrentGameProfile != null)
            {
                modes.Apply(Mode.Performance, L.T("Perfil Performance", "Performance profile"));
            }
        }

        private void OnModeChanged(Mode mode, string reason, List<string> warnings)
        {
            bool perf = mode == Mode.Performance;
            perfItem.Checked = perf;
            dailyItem.Checked = !perf;
            tray.Text = perf ? "MouseModes — Performance" : L.T("MouseModes — Dia a dia", "MouseModes — Daily");
            SetTrayIcon(mode);

            string shown = reason;
            if (perf && modes.CurrentGameProfile != null && reason.IndexOf(L.T("Perfil", "profile"), StringComparison.OrdinalIgnoreCase) < 0)
                shown += L.T("  ·  perfil próprio", "  ·  own profile");
            mainForm.ShowMode(mode, shown, modes.CurrentProfile);

            bool entering = perf && lastMode != Mode.Performance;
            lastMode = mode;

            // Avisos primeiro: depois disso as notificações podem ficar silenciadas.
            if (warnings.Count > 0)
            {
                ShowBalloon(L.T("Alguns ajustes falharam", "Some settings failed"), string.Join("\n", warnings), ToolTipIcon.Warning);
            }
            else if (!quiet && cfg.ShowNotifications && !(mainForm.Visible && mainForm.ContainsFocus))
            {
                // Com a janela em foco o aviso é redundante: a própria tela já mostra o modo.
                ShowBalloon(perf ? L.T("Modo Performance", "Performance mode") : L.T("Modo Dia a dia", "Daily mode"), shown, ToolTipIcon.Info);
            }

            ApplyExtras(perf, entering);
            modes.SetGameFocused(GameFocused());
            if (!perf) ShowPendingUpdate(); // saiu do jogo: agora pode avisar da versão nova
        }

        private bool GameFocused()
        {
            string name = foreground.ProcessName;
            return name != null && GameBooster.Matches(name, cfg.Games);
        }

        /// <summary>Tudo que só vale no Performance: prioridade, teclas, notificações, fechar programas, aviso de monitor.</summary>
        private void ApplyExtras(bool perf, bool entering)
        {
            if (perf && cfg.BoostGamePriority) booster.Boost(cfg.Games);
            else booster.RestoreAll();

            if (perf && cfg.BlockWinKey) winKey.Enable();
            else winKey.Disable();

            if (perf && cfg.DisableAccessibilityHotkeys) AccessibilityHotkeys.Disable();
            else AccessibilityHotkeys.Restore();

            if (perf && cfg.SilenceNotifications) NotificationSilencer.Silence(cfg);
            else NotificationSilencer.Restore(cfg);

            if (currentGame != null || lastBoostShown)
            {
                lastBoostShown = booster.AnyBoosted;
                mainForm.ShowGame(currentGame, lastBoostShown);
            }

            if (!entering) return;

            if (cfg.CloseAppsOnPerformance && cfg.AppsToClose.Count > 0)
            {
                AppCloser.CloseAsync(cfg.AppsToClose, cfg.Games, cfg.ForceCloseApps, (closed, failed) =>
                {
                    if (closed.Count == 0 && failed.Count == 0) return;
                    if (!mainForm.IsHandleCreated || mainForm.IsDisposed) return;
                    mainForm.BeginInvoke((Action)(() =>
                    {
                        var lines = new List<string>();
                        if (closed.Count > 0) lines.Add(L.T("Fechados: ", "Closed: ") + string.Join(", ", closed.ToArray()));
                        if (failed.Count > 0) lines.Add(L.T("Não fecharam: ", "Did not close: ") + string.Join(", ", failed.ToArray()) + L.T(" (ative \"Forçar\" no Turbo)", " (turn on \"Force\" in Turbo)"));
                        ShowBalloon(L.T("Programas em segundo plano", "Background programs"), string.Join("\n", lines.ToArray()),
                            failed.Count > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
                    }));
                });
            }

            if (cfg.MonitorCheck && !monitorWarned)
            {
                MonitorStatus low = monitors.Find(m => m.BelowMax);
                if (low != null)
                {
                    monitorWarned = true;
                    Log.Warn(low.Label + " em " + low.CurrentHz + " Hz (aceita " + low.MaxHz + " Hz)");
                    ShowBalloon(low.Label + L.T(" em ", " at ") + low.CurrentHz + " Hz",
                        L.T("Ele aceita ", "It supports ") + low.MaxHz + L.T(" Hz. Ajuste na tela do MouseModes para ter menos delay.", " Hz. Fix it on the MouseModes screen for less delay."),
                        ToolTipIcon.Warning);
                }
            }
        }

        // ---------------- Monitores ----------------

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            if (mainForm.IsHandleCreated && !mainForm.IsDisposed)
                mainForm.BeginInvoke((Action)RefreshMonitors);
        }

        private void RefreshMonitors()
        {
            monitors = cfg.MonitorCheck ? Displays.Scan() : new List<MonitorStatus>();
            if (cfg.MonitorCheck) mainForm.ShowMonitors(monitors);
            else mainForm.HideMonitors();
        }

        private void FixMonitors()
        {
            var results = new List<string>();
            bool allOk = true;
            foreach (MonitorStatus m in Displays.Scan())
            {
                if (!m.BelowMax) continue;
                string msg;
                bool ok = Displays.SetMaxRefresh(m, out msg);
                allOk &= ok;
                string line = m.Label + ": " + (ok ? m.CurrentHz + " → " + m.MaxHz + " Hz" : L.T("não ajustado", "not changed")) + (msg != null ? " (" + msg + ")" : "");
                results.Add(line);
                if (ok) Log.Info("Monitor ajustado — " + line); else Log.Warn("Monitor — " + line);
            }
            RefreshMonitors();
            if (results.Count > 0)
                ShowBalloon(allOk ? L.T("Monitor ajustado", "Monitor adjusted") : L.T("Ajuste do monitor", "Monitor adjustment"), string.Join("\n", results),
                    allOk ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }

        // ---------------- Janelas ----------------

        private void OpenSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                settingsForm.Activate();
                return;
            }

            settingsForm = new SettingsForm(cfg);
            settingsForm.Saved += () =>
            {
                autoItem.Checked = cfg.AutoSwitch;
                mainForm.ShowAuto(cfg.AutoSwitch);
                // Registra de novo sempre: o atalho pode ter mudado (opção fixa ou livre) ou ter ficado livre agora.
                if (!hotkey.Register(cfg.Hotkey, cfg.CustomHotkey)) WarnHotkey(L.T("Atalho em uso", "Hotkey in use"));
                mainForm.ShowHotkey(hotkey.ActiveCaps);
                gameWasRunning = false;
                modes.Apply(modes.Current, L.T("Configurações salvas", "Settings saved"));
                RefreshMonitors();
                CheckGames();
            };
            settingsForm.DiagnosticsRequested += OpenDiagnostics;
            settingsForm.RestoreDefaultsRequested += () =>
            {
                autoActivated = false;
                modes.RestoreWindowsDefaults();
                SaveConfig();
                ShowBalloon(L.T("Padrões do Windows restaurados", "Windows defaults restored"),
                    L.T("Velocidade 10, aceleração ligada, rolagem 3, plano Equilibrado e USB padrão.", "Speed 10, acceleration on, scroll 3, Balanced plan and default USB."), ToolTipIcon.Info);
            };
            settingsForm.Show();
            settingsForm.Activate();
        }

        private void OpenPolling()
        {
            if (pollingForm != null && !pollingForm.IsDisposed) { pollingForm.Activate(); return; }
            pollingForm = new PollingRateForm();
            pollingForm.Show();
            pollingForm.Activate();
        }

        private void OpenClickTest()
        {
            if (clickForm != null && !clickForm.IsDisposed) { clickForm.Activate(); return; }
            clickForm = new ClickTestForm();
            clickForm.Show();
            clickForm.Activate();
        }

        /// <summary>Idioma: seguir o Windows, português ou inglês. Vale na próxima vez que o app abrir.</summary>
        private ToolStripMenuItem LanguageMenu()
        {
            var menu = new ToolStripMenuItem("Idioma / Language");
            string[][] options =
            {
                new[] { "", L.T("Automático (do Windows)", "Automatic (from Windows)") },
                new[] { L.Portuguese, "Português" },
                new[] { L.English, "English" }
            };
            foreach (string[] option in options)
            {
                string code = option[0];
                var item = new ToolStripMenuItem(option[1]) { Checked = (cfg.Language ?? "") == code };
                item.Click += (s, e) =>
                {
                    foreach (ToolStripMenuItem other in menu.DropDownItems) other.Checked = other == item;
                    SetLanguage(code);
                };
                menu.DropDownItems.Add(item);
            }
            return menu;
        }

        private void SetLanguage(string code)
        {
            string language = code.Length == 0 ? null : code;
            if (language == cfg.Language) return;
            cfg.Language = language;
            SaveConfig();
            Log.Info("Idioma escolhido: " + (language ?? "automático"));
            // O aviso já sai no idioma escolhido; as telas mudam quando o app abrir de novo.
            bool english = L.UseEnglish(language, System.Globalization.CultureInfo.CurrentUICulture);
            ShowBalloon("MouseModes", english
                ? "The new language applies the next time MouseModes opens (Quit, then open it again)."
                : "O idioma novo vale na próxima vez que o MouseModes abrir (Sair e abrir de novo).", ToolTipIcon.Info);
        }

        private void OpenAimTest()
        {
            if (aimForm != null && !aimForm.IsDisposed) { aimForm.Activate(); return; }
            var host = new AimTestHost
            {
                CurrentProfile = () => modes.CurrentProfile,
                CurrentDpi = () => cfg.AimTestDpi,
                SaveDpi = dpi =>
                {
                    if (cfg.AimTestDpi == dpi) return;
                    cfg.AimTestDpi = dpi;
                    SaveConfig();
                }
            };
            aimForm = new AimTestForm(host);
            aimForm.Show();
            aimForm.Activate();
        }

        private void OpenDiagnostics()
        {
            if (diagForm != null && !diagForm.IsDisposed) { diagForm.Activate(); return; }
            diagForm = new DiagnosticsForm();
            diagForm.Show();
            diagForm.Activate();
        }

        // ---------------- Utilidades ----------------

        private void SaveConfig()
        {
            try { cfg.Save(); }
            catch (Exception ex)
            {
                Log.Error("Salvar configuração", ex);
                ShowBalloon(L.T("Erro ao salvar", "Could not save"), ex.Message, ToolTipIcon.Error);
            }
        }

        /// <summary>O atalho escolhido não pôde ser registrado: diz qual ficou no lugar (ou que nenhum ficou).</summary>
        private void WarnHotkey(string title)
        {
            string wanted = HotkeyWindow.DescribePreference(cfg.Hotkey, cfg.CustomHotkey);
            if (hotkey.ActiveCaps == null)
                ShowBalloon(L.T("Atalho indisponível", "Hotkey unavailable"), wanted +
                    L.T(" e as outras opções já são usadas por outros programas. ", " and the other options are already used by other programs. ") +
                    L.T("Use a janela ou o ícone da bandeja.", "Use the window or the tray icon."), ToolTipIcon.Warning);
            else
                ShowBalloon(title, wanted + L.T(" já é usado por outro programa. Ativo: ", " is already used by another program. Active: ") + string.Join(" + ", hotkey.ActiveCaps) + ".",
                    ToolTipIcon.Warning);
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            ShowBalloon(title, text, icon, null);
        }

        /// <param name="url">Link aberto ao clicar no aviso (só a página de releases do app).</param>
        private void ShowBalloon(string title, string text, ToolTipIcon icon, string url)
        {
            if (string.IsNullOrEmpty(text)) text = " ";
            balloonUrl = url;
            tray.ShowBalloonTip(url != null ? 8000 : 2500, title, text, icon);
        }

        private void OpenBalloonLink()
        {
            string url = balloonUrl;
            balloonUrl = null;
            if (!UpdateChecker.IsReleaseLink(url)) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Abrir " + url, ex); }
        }

        // ---------------- Versão nova ----------------

        /// <param name="manual">Pedido pelo menu: ignora a espera de um dia e avisa também quando não há nada novo.</param>
        private void CheckForUpdate(bool manual)
        {
            if (checkingUpdate || quitting) return;
            if (!manual)
            {
                if (!cfg.CheckForUpdates) return;
                if (cfg.LastUpdateCheck.HasValue && DateTime.UtcNow - cfg.LastUpdateCheck.Value < TimeSpan.FromDays(1)) return;
            }
            checkingUpdate = true;

            // O pedido pode levar alguns segundos: fora da thread da tela.
            var worker = new System.Threading.Thread(() =>
            {
                ReleaseInfo latest = UpdateChecker.FetchLatest();
                bool posted = false;
                try
                {
                    if (!mainForm.IsDisposed && mainForm.IsHandleCreated)
                    {
                        mainForm.BeginInvoke((Action)(() => UpdateChecked(latest, manual)));
                        posted = true;
                    }
                }
                catch (InvalidOperationException) { } // app fechando
                if (!posted) checkingUpdate = false;
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void UpdateChecked(ReleaseInfo latest, bool manual)
        {
            checkingUpdate = false;
            if (quitting) return;
            if (latest == null)
            {
                if (manual) ShowBalloon(L.T("Versão nova", "New version"),
                    L.T("Não foi possível consultar o GitHub agora. Tente mais tarde.", "Could not reach GitHub right now. Try again later."), ToolTipIcon.Warning);
                return;
            }

            cfg.LastUpdateCheck = DateTime.UtcNow;
            SaveConfig();
            bool newer = UpdateChecker.IsNewer(latest.Version, UpdateChecker.Current);
            Log.Info("Versão mais nova no GitHub: " + latest.Tag + (newer ? " (esta é " + UpdateChecker.Current + ")" : " (já é esta)"));

            if (!newer)
            {
                if (manual) ShowBalloon(L.T("Versão nova", "New version"),
                    L.T("Você já tem a mais recente (", "You already have the latest (") + UpdateChecker.Current + ").", ToolTipIcon.Info);
                return;
            }
            if (!manual && latest.Tag == cfg.NotifiedVersion) return; // já avisou desta versão
            pendingRelease = latest;
            ShowPendingUpdate(manual);
        }

        /// <summary>
        /// Avisa da versão nova guardada. A checagem automática espera o fim do Performance (não atrapalha o jogo);
        /// a pedida pelo menu avisa na hora.
        /// </summary>
        private void ShowPendingUpdate(bool now = false)
        {
            if (pendingRelease == null || (!now && modes.Current == Mode.Performance)) return;
            ReleaseInfo release = pendingRelease;
            pendingRelease = null;
            cfg.NotifiedVersion = release.Tag;
            SaveConfig();
            ShowBalloon("MouseModes " + release.Tag + L.T(" disponível", " available"),
                L.T("Você está na ", "You have ") + UpdateChecker.Current + L.T(". Clique aqui para abrir a página do download.", ". Click here to open the download page."),
                ToolTipIcon.Info, release.Url);
        }

        private void SetTrayIcon(Mode mode)
        {
            bool perf = mode == Mode.Performance;

            // Quadrado arredondado em gradiente com o ícone do modo (raio = Performance, monitor = Dia a dia).
            IntPtr newHandle;
            using (var bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    var box = new RectangleF(1, 1, 30, 30);
                    using (LinearGradientBrush lb = Theme.Gradient(box, Theme.AccentA(mode), Theme.AccentB(mode), 45f))
                        Theme.FillRounded(g, box, 8, lb);
                    var glyph = perf ? new RectangleF(7, 5, 18, 22) : new RectangleF(6, 6, 20, 20);
                    using (var white = new SolidBrush(Color.White))
                        Theme.DrawGlyph(g, perf ? GlyphKind.Bolt : GlyphKind.Monitor, glyph, white, 2.4f);
                }
                newHandle = bmp.GetHicon();
            }

            Icon newIcon = Icon.FromHandle(newHandle);
            Icon old = tray.Icon;
            tray.Icon = newIcon;
            mainForm.Icon = newIcon;
            if (old != null) old.Dispose();
            if (iconHandle != IntPtr.Zero) DestroyIcon(iconHandle);
            iconHandle = newHandle;
        }

        /// <summary>Windows desligando ou saindo da conta: desfaz o que não pode ficar para trás.</summary>
        private void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e)
        {
            Log.Info("Windows encerrando a sessão");
            RestoreGameTimeTweaks();
        }

        private void RestoreGameTimeTweaks()
        {
            booster.RestoreAll();
            winKey.Disable();
            AccessibilityHotkeys.Restore();
            NotificationSilencer.Restore(cfg);
        }

        private void Quit()
        {
            if (quitting) return;
            quitting = true;

            gameTimer.Stop();
            updateTimer.Stop();
            if (showWait != null) showWait.Unregister(null);
            if (quitWait != null) quitWait.Unregister(null);
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;

            if (cfg.RestoreDailyOnExit && modes.Current == Mode.Performance)
            {
                quiet = true;
                modes.Apply(Mode.DiaADia, L.T("Saindo", "Quitting"));
            }
            foreground.Dispose();
            // Se continuar no Performance, sem o app ninguém troca o mouse de volta: deixa o do Performance.
            modes.SetGameFocused(true);
            // O plano de energia roda numa thread de fundo, que morre com o processo. O instalador espera até 20 s.
            modes.WaitForPower(15000);
            // Mesmo se o modo continuar Performance, os extras do jogo não podem ficar ativos sem o app.
            RestoreGameTimeTweaks();
            Log.Info("MouseModes encerrado");

            SaveConfig();
            hotkey.Dispose();
            foreach (Form f in new Form[] { settingsForm, pollingForm, clickForm, aimForm, diagForm })
            {
                if (f != null && !f.IsDisposed) f.Close();
            }

            mainForm.AllowClose();
            mainForm.Close();

            tray.Visible = false;
            tray.Dispose();
            if (iconHandle != IntPtr.Zero) DestroyIcon(iconHandle);
            iconHandle = IntPtr.Zero;

            ExitThread();
        }
    }
}
