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
        private readonly Timer animTimer;

        private Mode mode = Mode.DiaADia;
        private float modeT;      // 0 = Dia a dia, 1 = Performance (para a transição de cor)
        private double phase;     // pulso do brilho
        private bool gameOpen;
        private bool allowClose;

        public MainForm() : base("MouseModes", true)
        {
            ClientSize = new Size(480, 724); // com o botão do teste de mira, ainda cabe numa tela de 768 px com a barra de tarefas

            hero = new HeroCard { Location = new Point(12, 52), Size = new Size(456, 226) };

            perfTile = new ModeTile
            {
                Location = new Point(12, 282),
                Size = new Size(228, 104),
                Title = "PERFORMANCE",
                Subtitle = "Jogos, sem aceleração",
                Glyph = GlyphKind.Bolt,
                AccentA = Theme.PerfA,
                AccentB = Theme.PerfB
            };
            dailyTile = new ModeTile
            {
                Location = new Point(240, 282),
                Size = new Size(228, 104),
                Title = "DIA A DIA",
                Subtitle = "Trabalho e navegação",
                Glyph = GlyphKind.Monitor,
                AccentA = Theme.DailyA,
                AccentB = Theme.DailyB
            };
            perfTile.Click += (s, e) => Raise(ModeRequested, Mode.Performance);
            dailyTile.Click += (s, e) => Raise(ModeRequested, Mode.DiaADia);

            autoRow = new ToggleRow
            {
                Location = new Point(20, 398),
                Size = new Size(440, 70),
                Title = "Troca automática ao abrir um jogo",
                Subtitle = "Nenhum jogo da lista aberto",
                DotColor = Theme.Faint
            };
            autoRow.Switch.CheckedChanged += () => Raise(AutoToggled);

            monitorRow = new StatusRow
            {
                Location = new Point(20, 478),
                Size = new Size(440, 60),
                Title = "Verificando monitores...",
                Subtitle = ""
            };
            monitorRow.ActionClicked += () => Raise(MonitorFixRequested);

            aimButton = new NeonButton { Text = "Testar mira e DPI", Glyph = GlyphKind.Target, Style = ButtonStyle.Primary, Location = new Point(20, 552), Size = new Size(440, 40) };
            aimButton.Click += (s, e) => Raise(AimTestRequested);

            pollingButton = new NeonButton { Text = "Polling", Glyph = GlyphKind.Gauge, Location = new Point(20, 600), Size = new Size(104, 44) };
            clickButton = new NeonButton { Text = "Clique", Glyph = GlyphKind.Mouse, Location = new Point(132, 600), Size = new Size(104, 44) };
            settingsButton = new NeonButton { Text = "Ajustes", Glyph = GlyphKind.Gear, Location = new Point(244, 600), Size = new Size(104, 44) };
            var quitButton = new NeonButton { Text = "Sair", Glyph = GlyphKind.Power, Style = ButtonStyle.Danger, Location = new Point(356, 600), Size = new Size(104, 44) };
            pollingButton.Click += (s, e) => Raise(PollingRequested);
            clickButton.Click += (s, e) => Raise(ClickTestRequested);
            settingsButton.Click += (s, e) => Raise(SettingsRequested);
            quitButton.Click += (s, e) => Raise(QuitRequested);

            hint = new HotkeyHint { Location = new Point(20, 656), Size = new Size(440, 56) };

            Controls.Add(hero);
            Controls.Add(perfTile);
            Controls.Add(dailyTile);
            Controls.Add(autoRow);
            Controls.Add(monitorRow);
            Controls.Add(aimButton);
            Controls.Add(pollingButton);
            Controls.Add(clickButton);
            Controls.Add(settingsButton);
            Controls.Add(quitButton);
            Controls.Add(hint);

            animTimer = new Timer { Interval = 33 };
            animTimer.Tick += (s, e) => Tick();
            VisibleChanged += (s, e) =>
            {
                if (Visible) animTimer.Start(); else animTimer.Stop();
            };

            ApplyColors();
            FinishLayout();
        }

        // ---------------- API usada pelo TrayApp ----------------

        public void ShowMode(Mode newMode, string reason, MouseProfile profile)
        {
            mode = newMode;
            bool perf = mode == Mode.Performance;
            hero.ModeName = perf ? "PERFORMANCE" : "DIA A DIA";
            hero.Glyph = perf ? GlyphKind.Bolt : GlyphKind.Monitor;
            hero.Reason = reason ?? "";
            hero.StatLabels = new[] { "SENS", "ACEL", "ENERGIA", "USB" };
            hero.StatValues = Stats(profile, perf);
            perfTile.Active = perf;
            dailyTile.Active = !perf;
            hero.Invalidate();
            if (!Visible) { modeT = perf ? 1f : 0f; ApplyColors(); }
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
                monitorRow.Warning = false;
                monitorRow.Title = "Monitores";
                monitorRow.Subtitle = "Não foi possível ler a taxa de atualização";
                monitorRow.SetAction(null);
            }
            else if (low.Count == 0)
            {
                monitorRow.Warning = false;
                monitorRow.Title = monitors.Count == 1 ? "Monitor na taxa máxima" : "Monitores na taxa máxima";
                monitorRow.Subtitle = string.Join("  ·  ", monitors.ConvertAll(m => m.Label + ": " + m.CurrentHz + " Hz").ToArray());
                monitorRow.SetAction(null);
            }
            else
            {
                MonitorStatus first = low[0];
                monitorRow.Warning = true;
                monitorRow.Title = low.Count == 1
                    ? first.Label + " em " + first.CurrentHz + " Hz"
                    : low.Count + " monitores abaixo do máximo";
                monitorRow.Subtitle = low.Count == 1
                    ? "Ele aceita " + first.MaxHz + " Hz: mais fluido e menos delay"
                    : string.Join("  ·  ", low.ConvertAll(m => m.Label + ": " + m.CurrentHz + " → " + m.MaxHz + " Hz").ToArray());
                monitorRow.SetAction(low.Count == 1 ? first.MaxHz + " Hz" : "Ajustar");
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
            monitorRow.Warning = false;
            monitorRow.Title = "Aviso de monitor desligado";
            monitorRow.Subtitle = "Ligue em Ajustes → Turbo";
            monitorRow.SetAction(null);
            monitorRow.Invalidate();
        }

        public void ShowGame(string game, bool boosted)
        {
            gameOpen = game != null;
            autoRow.Subtitle = gameOpen
                ? game + " detectado" + (boosted ? "  ·  prioridade alta" : "")
                : "Nenhum jogo da lista aberto";
            autoRow.DotColor = gameOpen ? Color.FromArgb(57, 255, 136) : Theme.Faint;
            autoRow.DotPulse = 0;
            autoRow.Invalidate();
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

        private void Tick()
        {
            float target = mode == Mode.Performance ? 1f : 0f;
            bool changing = modeT != target;
            modeT = Theme.Approach(modeT, target, 0.12f);

            phase += 0.06;
            float pulse = (float)((Math.Sin(phase) + 1) / 2);
            hero.Pulse = pulse;
            if (changing) ApplyColors();
            hero.Invalidate();

            if (gameOpen)
            {
                autoRow.DotPulse = pulse;
                autoRow.Invalidate();
            }
        }

        private void ApplyColors()
        {
            Color a = Theme.Lerp(Theme.DailyA, Theme.PerfA, modeT);
            Color b = Theme.Lerp(Theme.DailyB, Theme.PerfB, modeT);
            hero.AccentA = a;
            hero.AccentB = b;
            TitleBar.AccentA = a;
            TitleBar.AccentB = b;
            autoRow.SetAccents(a, b);
            pollingButton.AccentA = a;
            pollingButton.AccentB = b;
            settingsButton.AccentA = a;
            settingsButton.AccentB = b;
            clickButton.AccentA = a;
            clickButton.AccentB = b;
            aimButton.AccentA = a;
            aimButton.AccentB = b;
            aimButton.Invalidate();
            clickButton.Invalidate();
            TitleBar.Invalidate();
            pollingButton.Invalidate();
            settingsButton.Invalidate();
        }

        private static string[] Stats(MouseProfile p, bool perf)
        {
            string power;
            switch (p.PowerPlan)
            {
                case PowerPlanOption.AltoDesempenho: power = "ALTO"; break;
                case PowerPlanOption.DesempenhoMaximo: power = "MÁXIMO"; break;
                case PowerPlanOption.Equilibrado: power = "EQUIL."; break;
                default: power = perf ? "PADRÃO" : "ANTERIOR"; break;
            }
            return new[]
            {
                p.PointerSpeed + "/20" + (p.PointerSpeed == 10 ? " · 1:1" : ""),
                p.EnhancePointerPrecision ? "ON" : "OFF",
                power,
                p.DisableUsbSelectiveSuspend ? "ATIVO" : "ECO"
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
