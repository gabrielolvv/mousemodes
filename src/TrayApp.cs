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
                    ShowBalloon("Plano de energia", string.Join("\n", warnings), ToolTipIcon.Warning)));
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
            var openItem = new ToolStripMenuItem("Abrir MouseModes", null, (s, e) => ShowMain());
            openItem.Font = new Font(openItem.Font, FontStyle.Bold);
            perfItem = new ToolStripMenuItem("Modo Performance", null, (s, e) => SwitchManually(Mode.Performance));
            dailyItem = new ToolStripMenuItem("Modo Dia a dia", null, (s, e) => SwitchManually(Mode.DiaADia));
            autoItem = new ToolStripMenuItem("Trocar sozinho quando abrir um jogo", null, (s, e) => ToggleAuto());
            autoItem.Checked = cfg.AutoSwitch;

            var menu = new ContextMenuStrip();
            menu.Items.Add(openItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(perfItem);
            menu.Items.Add(dailyItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(autoItem);
            menu.Items.Add(new ToolStripMenuItem("Medir polling rate...", null, (s, e) => OpenPolling()));
            menu.Items.Add(new ToolStripMenuItem("Teste de clique...", null, (s, e) => OpenClickTest()));
            menu.Items.Add(new ToolStripMenuItem("Configurações...", null, (s, e) => OpenSettings()));
            menu.Items.Add(new ToolStripMenuItem("Diagnóstico...", null, (s, e) => OpenDiagnostics()));
            menu.Items.Add(new ToolStripMenuItem("Procurar versão nova", null, (s, e) => CheckForUpdate(true)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Sair", null, (s, e) => Quit()));

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

            if (!hotkeyPreferred) WarnHotkey("Atalho alterado");
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
            ShowBalloon("MouseModes continua rodando",
                "Ele fica aqui perto do relógio. Clique no ícone para abrir de novo.", ToolTipIcon.Info);
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
            modes.Apply(mode, "Troca manual");
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
                    modes.Apply(Mode.Performance, running ? "Perfil do jogo: " + game : "Perfil Performance");
                return;
            }
            gameWasRunning = running;

            if (!cfg.AutoSwitch)
            {
                if (modes.Current == Mode.Performance && cfg.PerformanceFor(game) != modes.CurrentProfile)
                    modes.Apply(Mode.Performance, running ? "Perfil do jogo: " + game : "Perfil Performance");
                return;
            }

            if (running && modes.Current != Mode.Performance)
            {
                autoActivated = true;
                modes.Apply(Mode.Performance, "Jogo detectado: " + game);
            }
            else if (running && cfg.PerformanceFor(game) != modes.CurrentProfile)
            {
                modes.Apply(Mode.Performance, "Perfil do jogo: " + game);
            }
            else if (!running && autoActivated)
            {
                autoActivated = false;
                if (modes.Current == Mode.Performance)
                    modes.Apply(Mode.DiaADia, "Jogo fechado");
            }
            else if (!running && modes.Current == Mode.Performance && modes.CurrentGameProfile != null)
            {
                modes.Apply(Mode.Performance, "Perfil Performance");
            }
        }

        private void OnModeChanged(Mode mode, string reason, List<string> warnings)
        {
            bool perf = mode == Mode.Performance;
            perfItem.Checked = perf;
            dailyItem.Checked = !perf;
            tray.Text = perf ? "MouseModes — Performance" : "MouseModes — Dia a dia";
            SetTrayIcon(mode);

            string shown = reason;
            if (perf && modes.CurrentGameProfile != null && reason.IndexOf("Perfil", StringComparison.Ordinal) < 0)
                shown += "  ·  perfil próprio";
            mainForm.ShowMode(mode, shown, modes.CurrentProfile);

            bool entering = perf && lastMode != Mode.Performance;
            lastMode = mode;

            // Avisos primeiro: depois disso as notificações podem ficar silenciadas.
            if (warnings.Count > 0)
            {
                ShowBalloon("Alguns ajustes falharam", string.Join("\n", warnings), ToolTipIcon.Warning);
            }
            else if (!quiet && cfg.ShowNotifications && !(mainForm.Visible && mainForm.ContainsFocus))
            {
                // Com a janela em foco o aviso é redundante: a própria tela já mostra o modo.
                ShowBalloon(perf ? "Modo Performance" : "Modo Dia a dia", shown, ToolTipIcon.Info);
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
                        if (closed.Count > 0) lines.Add("Fechados: " + string.Join(", ", closed.ToArray()));
                        if (failed.Count > 0) lines.Add("Não fecharam: " + string.Join(", ", failed.ToArray()) + " (ative \"Forçar\" no Turbo)");
                        ShowBalloon("Programas em segundo plano", string.Join("\n", lines.ToArray()),
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
                    ShowBalloon(low.Label + " em " + low.CurrentHz + " Hz",
                        "Ele aceita " + low.MaxHz + " Hz. Ajuste na tela do MouseModes para ter menos delay.",
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
                string line = m.Label + ": " + (ok ? m.CurrentHz + " → " + m.MaxHz + " Hz" : "não ajustado") + (msg != null ? " (" + msg + ")" : "");
                results.Add(line);
                if (ok) Log.Info("Monitor ajustado — " + line); else Log.Warn("Monitor — " + line);
            }
            RefreshMonitors();
            if (results.Count > 0)
                ShowBalloon(allOk ? "Monitor ajustado" : "Ajuste do monitor", string.Join("\n", results),
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
                if (!hotkey.Register(cfg.Hotkey, cfg.CustomHotkey)) WarnHotkey("Atalho em uso");
                mainForm.ShowHotkey(hotkey.ActiveCaps);
                gameWasRunning = false;
                modes.Apply(modes.Current, "Configurações salvas");
                RefreshMonitors();
                CheckGames();
            };
            settingsForm.DiagnosticsRequested += OpenDiagnostics;
            settingsForm.RestoreDefaultsRequested += () =>
            {
                autoActivated = false;
                modes.RestoreWindowsDefaults();
                SaveConfig();
                ShowBalloon("Padrões do Windows restaurados",
                    "Velocidade 10, aceleração ligada, rolagem 3, plano Equilibrado e USB padrão.", ToolTipIcon.Info);
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
                ShowBalloon("Erro ao salvar", ex.Message, ToolTipIcon.Error);
            }
        }

        /// <summary>O atalho escolhido não pôde ser registrado: diz qual ficou no lugar (ou que nenhum ficou).</summary>
        private void WarnHotkey(string title)
        {
            string wanted = HotkeyWindow.DescribePreference(cfg.Hotkey, cfg.CustomHotkey);
            if (hotkey.ActiveCaps == null)
                ShowBalloon("Atalho indisponível", wanted + " e as outras opções já são usadas por outros programas. " +
                    "Use a janela ou o ícone da bandeja.", ToolTipIcon.Warning);
            else
                ShowBalloon(title, wanted + " já é usado por outro programa. Ativo: " + string.Join(" + ", hotkey.ActiveCaps) + ".",
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
                if (manual) ShowBalloon("Versão nova", "Não foi possível consultar o GitHub agora. Tente mais tarde.", ToolTipIcon.Warning);
                return;
            }

            cfg.LastUpdateCheck = DateTime.UtcNow;
            SaveConfig();
            bool newer = UpdateChecker.IsNewer(latest.Version, UpdateChecker.Current);
            Log.Info("Versão mais nova no GitHub: " + latest.Tag + (newer ? " (esta é " + UpdateChecker.Current + ")" : " (já é esta)"));

            if (!newer)
            {
                if (manual) ShowBalloon("Versão nova", "Você já tem a mais recente (" + UpdateChecker.Current + ").", ToolTipIcon.Info);
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
            ShowBalloon("MouseModes " + release.Tag + " disponível",
                "Você está na " + UpdateChecker.Current + ". Clique aqui para abrir a página do download.",
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
                modes.Apply(Mode.DiaADia, "Saindo");
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
            foreach (Form f in new Form[] { settingsForm, pollingForm, clickForm, diagForm })
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
