using System;
using System.Drawing;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>Tela principal. Fechar no X só esconde: o app continua na bandeja.</summary>
    public class MainForm : GamerWindow
    {
        public event Action<Mode> ModeRequested;
        public event Action AutoToggled;
        public event Action SettingsRequested;
        public event Action PollingRequested;
        public event Action ClickTestRequested;
        public event Action AimTestRequested;
        public event Action SensitivityRequested;
        public event Action QuitRequested;
        public event Action HiddenToTray;
        public event Action MonitorFixRequested;

        private readonly HeroCard hero;
        private readonly ModeTile perfTile;
        private readonly ModeTile dailyTile;
        private readonly ToggleRow autoRow;
        private readonly StatusRow monitorRow;
        private readonly HotkeyHint hint;
        private readonly NeonButton pollingButton;
        private readonly NeonButton settingsButton;
        private readonly NeonButton clickButton;
        private readonly NeonButton aimButton;
        private readonly NeonButton sensButton;
        private readonly NeonButton quitButton;
        private readonly Timer animTimer;

        // Troca de modo: a paleta não passa pelas cores do meio (roxo, verde-água). As cores de acento somem até
        // FadeLow, a paleta troca de uma vez na metade e elas voltam; no fim fica exatamente a paleta do modo.
        private const double FadeMs = 150;
        private const float FadeLow = 0.4f;
        private readonly System.Diagnostics.Stopwatch fade = new System.Diagnostics.Stopwatch();
        private double fadeStartMs;   // ponto da curva em que a troca começou (troca de novo no meio de outra)
        private bool fading;
        private Mode fadeFrom = Mode.DiaADia;

        private Mode mode = Mode.DiaADia;
        private Mode shownPalette = Mode.DiaADia; // paleta que está na tela
        private float accentOpacity = 1f;
        private bool painted;     // a janela já foi desenhada com a paleta atual (sem isso não há o que esmaecer)
        private double phase;     // pulso do ponto de "jogo detectado"
        private bool gameOpen;
        private bool allowClose;

        public MainForm() : base("MouseModes", true)
        {
            ClientSize = new Size(480, 712); // cabe numa tela de 768 px com a barra de tarefas

            // Margem da janela 20; entre blocos 16, dentro de um bloco 8 (também entre os dois tiles e entre os botões).
            // Cartões com Inset 8 começam 8 px antes: tudo fica na coluna de x = 20 a 460.
            hero = new HeroCard { Location = new Point(12, 52), Size = new Size(456, 204) };

            perfTile = new ModeTile
            {
                Location = new Point(12, 256),
                Size = new Size(232, 104),
                Title = "Performance",
                Subtitle = L.T("Jogos, sem aceleração", "Games, no acceleration"),
                Glyph = GlyphKind.Bolt,
                AccentA = Theme.PerfA,
                AccentB = Theme.PerfB
            };
            dailyTile = new ModeTile
            {
                Location = new Point(236, 256),
                Size = new Size(232, 104),
                Title = L.T("Dia a dia", "Daily"),
                Subtitle = L.T("Trabalho e navegação", "Work and browsing"),
                Glyph = GlyphKind.Monitor,
                AccentA = Theme.DailyA,
                AccentB = Theme.DailyB
            };
            perfTile.Click += (s, e) => Raise(ModeRequested, Mode.Performance);
            dailyTile.Click += (s, e) => Raise(ModeRequested, Mode.DiaADia);

            autoRow = new ToggleRow
            {
                Location = new Point(20, 368),
                Size = new Size(440, 64),
                Title = L.T("Troca automática ao abrir um jogo", "Switch automatically when a game opens"),
                Subtitle = L.T("Nenhum jogo da lista aberto", "No game from the list is open"),
                DotColor = Theme.TextTertiary
            };
            autoRow.Switch.CheckedChanged += () => Raise(AutoToggled);

            monitorRow = new StatusRow
            {
                Location = new Point(20, 440),
                Size = new Size(440, 56),
                Title = L.T("Verificando monitores…", "Checking monitors…"),
                Subtitle = "",
                Tone = StatusTone.Neutral // ainda sem resultado: nem verde nem âmbar
            };
            monitorRow.ActionClicked += () => Raise(MonitorFixRequested);

            // Ações: as duas ferramentas de mira lado a lado (44 px, a primária na cor do modo) e, abaixo, as
            // ferramentas secundárias de 40 px numa grade de 3. Sair fica no rodapé, discreto (e no menu da bandeja).
            aimButton = new NeonButton { Text = L.T("Testar mira e DPI", "Aim and DPI test"), Glyph = GlyphKind.Target, Style = ButtonStyle.Primary, Location = new Point(20, 512), Size = new Size(216, 44) };
            aimButton.Click += (s, e) => Raise(AimTestRequested);
            sensButton = new NeonButton { Text = L.T("Sensibilidade", "Sensitivity"), Glyph = GlyphKind.Sliders, Location = new Point(244, 512), Size = new Size(216, 44) };
            sensButton.Click += (s, e) => Raise(SensitivityRequested);

            pollingButton = new NeonButton { Text = "Polling", Glyph = GlyphKind.Gauge, Location = new Point(20, 564), Size = new Size(141, 40) };
            clickButton = new NeonButton { Text = L.T("Clique", "Click"), Glyph = GlyphKind.Mouse, Location = new Point(169, 564), Size = new Size(142, 40) };
            settingsButton = new NeonButton { Text = L.T("Ajustes", "Settings"), Glyph = GlyphKind.Gear, Location = new Point(319, 564), Size = new Size(141, 40) };
            quitButton = new NeonButton { Text = L.T("Sair", "Quit"), Glyph = GlyphKind.Power, Style = ButtonStyle.QuietDanger, Size = new Size(80, 32) };
            pollingButton.Click += (s, e) => Raise(PollingRequested);
            clickButton.Click += (s, e) => Raise(ClickTestRequested);
            settingsButton.Click += (s, e) => Raise(SettingsRequested);
            quitButton.Click += (s, e) => Raise(QuitRequested);

            hint = new HotkeyHint { Location = new Point(20, 620), Size = new Size(440, 76), FooterAction = quitButton };

            Controls.Add(hero);
            Controls.Add(perfTile);
            Controls.Add(dailyTile);
            Controls.Add(autoRow);
            Controls.Add(monitorRow);
            Controls.Add(aimButton);
            Controls.Add(sensButton);
            Controls.Add(pollingButton);
            Controls.Add(clickButton);
            Controls.Add(settingsButton);
            Controls.Add(hint);

            // Só anda durante a troca de modo ou com um jogo aberto (o ponto pulsa).
            animTimer = new Timer { Interval = 16 };
            animTimer.Tick += (s, e) => Tick();
            VisibleChanged += (s, e) =>
            {
                if (Visible)
                {
                    UpdateTimer();
                    return;
                }
                // Escondida: termina a troca já na paleta do modo e desenha do zero na próxima vez.
                painted = false;
                if (fading) SnapColors();
                animTimer.Stop();
            };

            ApplyColors();
            FinishLayout();
        }

        // ---------------- API usada pelo TrayApp ----------------

        public void ShowMode(Mode newMode, string reason, MouseProfile profile)
        {
            mode = newMode;
            bool perf = mode == Mode.Performance;
            hero.ModeName = perf ? "Performance" : L.T("Dia a dia", "Daily");
            hero.Glyph = perf ? GlyphKind.Bolt : GlyphKind.Monitor;
            hero.Reason = reason ?? "";
            hero.StatLabels = new[] { L.T("Sensibilidade", "Sensitivity"), L.T("Aceleração", "Acceleration"), L.T("Energia", "Power"), "USB" };
            hero.StatValues = Stats(profile, perf);
            // Os tiles acompanham a troca de paleta: só animam o que já está desenhado na tela.
            bool animate = Visible && painted;
            perfTile.SetActive(perf, animate);
            dailyTile.SetActive(!perf, animate);
            hero.Invalidate();
            if (mode == shownPalette && !fading) return;
            // Só esmaece o que já está na tela; antes do primeiro desenho a paleta troca direto.
            if (Visible && painted) StartFade(); else SnapColors();
        }

        public void ShowAuto(bool enabled)
        {
            autoRow.Switch.SetChecked(enabled, false);
        }

        public void ShowMonitors(System.Collections.Generic.List<MonitorStatus> monitors)
        {
            var low = monitors.FindAll(m => m.BelowMax);
            if (monitors.Count == 0)
            {
                monitorRow.Tone = StatusTone.Neutral;
                monitorRow.Title = L.T("Monitores", "Monitors");
                monitorRow.Subtitle = L.T("Não foi possível ler a taxa de atualização", "Could not read the refresh rate");
                monitorRow.SetAction(null);
            }
            else if (low.Count == 0)
            {
                monitorRow.Warning = false;
                monitorRow.Title = monitors.Count == 1 ? L.T("Monitor na taxa máxima", "Monitor at its max refresh rate") : L.T("Monitores na taxa máxima", "Monitors at their max refresh rate");
                monitorRow.Subtitle = string.Join("  ·  ", monitors.ConvertAll(m => m.Label + ": " + m.CurrentHz + " Hz").ToArray());
                monitorRow.SetAction(null);
            }
            else
            {
                MonitorStatus first = low[0];
                monitorRow.Warning = true;
                monitorRow.Title = low.Count == 1
                    ? first.Label + L.T(" em ", " at ") + first.CurrentHz + " Hz"
                    : low.Count + L.T(" monitores abaixo do máximo", " monitors below their max");
                monitorRow.Subtitle = low.Count == 1
                    ? L.T("Ele aceita ", "It supports ") + first.MaxHz + L.T(" Hz: mais fluido e menos delay", " Hz: smoother, with less delay")
                    : string.Join("  ·  ", low.ConvertAll(m => m.Label + ": " + m.CurrentHz + " → " + m.MaxHz + " Hz").ToArray());
                monitorRow.SetAction(low.Count == 1 ? first.MaxHz + " Hz" : L.T("Ajustar", "Fix"));
            }
            monitorRow.Invalidate();
        }

        /// <summary>Mostra as teclas do atalho que conseguiu ser registrado (null = nenhum).</summary>
        public void ShowHotkey(string[] caps)
        {
            hint.Keys = caps;
        }

        public void HideMonitors()
        {
            monitorRow.Tone = StatusTone.Neutral;
            monitorRow.Title = L.T("Aviso de monitor desligado", "Monitor warning is off");
            monitorRow.Subtitle = L.T("Ligue em Ajustes → Geral", "Turn it on in Settings → General");
            monitorRow.SetAction(null);
            monitorRow.Invalidate();
        }

        public void ShowGame(string game, bool boosted)
        {
            gameOpen = game != null;
            autoRow.Subtitle = gameOpen
                ? game + L.T(" detectado", " detected") + (boosted ? L.T("  ·  prioridade alta", "  ·  high priority") : "")
                : L.T("Nenhum jogo da lista aberto", "No game from the list is open");
            autoRow.DotColor = gameOpen ? Theme.Success : Theme.TextTertiary;
            autoRow.DotPulse = 0;
            autoRow.Invalidate();
            UpdateTimer();
        }

        public void ShowAndFocus()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        /// <summary>Chamado ao sair do app, para o X deixar de só esconder a janela.</summary>
        public void AllowClose()
        {
            allowClose = true;
        }

        // ---------------- Animação ----------------

        private void UpdateTimer()
        {
            if (Visible && (fading || gameOpen)) animTimer.Start();
            else animTimer.Stop();
        }

        private void StartFade()
        {
            // Trocou de novo no meio de outra troca: continua da opacidade em que está, sem pulo.
            double start = 0;
            if (fading)
            {
                start = (1 - accentOpacity) / (1 - FadeLow) * FadeMs / 2;
            }
            fadeFrom = shownPalette;
            fadeStartMs = start;
            fade.Restart();
            fading = true;
            UpdateTimer();
        }

        /// <summary>Fim da troca (ou troca sem animação): a paleta do modo, opaca.</summary>
        private void SnapColors()
        {
            fading = false;
            fade.Reset();
            shownPalette = mode;
            accentOpacity = 1f;
            ApplyColors();
            UpdateTimer();
        }

        private void Tick()
        {
            if (fading)
            {
                double t = (fadeStartMs + fade.Elapsed.TotalMilliseconds) / FadeMs;
                if (t >= 1)
                {
                    SnapColors();
                }
                else
                {
                    // Primeira metade: some na paleta de antes. Segunda: volta já na do modo novo.
                    bool firstHalf = t < 0.5;
                    shownPalette = firstHalf ? fadeFrom : mode;
                    float half = (float)(firstHalf ? t / 0.5 : (t - 0.5) / 0.5);
                    accentOpacity = firstHalf ? 1f - (1f - FadeLow) * half : FadeLow + (1f - FadeLow) * half;
                    ApplyColors();
                }
            }

            if (gameOpen)
            {
                phase += 0.03;
                autoRow.DotPulse = (float)((Math.Sin(phase) + 1) / 2);
                autoRow.Invalidate();
            }
            UpdateTimer();
        }

        private void ApplyColors()
        {
            Color a = Theme.AccentA(shownPalette);
            Color b = Theme.AccentB(shownPalette);
            float o = accentOpacity;
            hero.AccentA = a;
            hero.AccentB = b;
            hero.AccentOpacity = o;
            TitleBar.AccentA = a;
            TitleBar.AccentB = b;
            TitleBar.AccentOpacity = o;
            autoRow.SetAccents(a, b, o);
            foreach (NeonButton button in new[] { aimButton, sensButton, pollingButton, clickButton, settingsButton })
            {
                button.AccentA = a;
                button.AccentB = b;
                button.AccentOpacity = o;
                button.Invalidate();
            }
            hero.Invalidate();
            TitleBar.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            painted = true;
        }

        private static string[] Stats(MouseProfile p, bool perf)
        {
            string power;
            switch (p.PowerPlan)
            {
                case PowerPlanOption.AltoDesempenho: power = L.T("Alto", "High"); break;
                case PowerPlanOption.DesempenhoMaximo: power = L.T("Máximo", "Max"); break;
                case PowerPlanOption.Equilibrado: power = L.T("Equilibrado", "Balanced"); break;
                default: power = perf ? L.T("Padrão", "Default") : L.T("Anterior", "Previous"); break;
            }
            return new[]
            {
                p.PointerSpeed + "/20" + (p.PointerSpeed == 10 ? " · 1:1" : ""),
                p.EnhancePointerPrecision ? L.T("Ligada", "On") : L.T("Desligada", "Off"),
                power,
                p.DisableUsbSelectiveSuspend ? L.T("Ativo", "On") : "Eco"
            };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                Raise(HiddenToTray);
                return;
            }
            base.OnFormClosing(e);
        }

        private static void Raise(Action handler)
        {
            if (handler != null) handler();
        }

        private static void Raise(Action<Mode> handler, Mode m)
        {
            if (handler != null) handler(m);
        }
    }
}
