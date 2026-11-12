using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>
    /// Só os caminhos que não aplicam nada no Windows. O plano de energia passa por um aplicador de mentira,
    /// conferido antes de qualquer pedido: sem ele, a fila rodaria o powercfg de verdade.
    /// </summary>
    public class ModeManagerTests : IDisposable
    {
        private readonly TempFolder dir = new TempFolder(); // também desvia o log
        private readonly List<string> applied = new List<string>();
        private int delayMs = 300;
        private int? usbSuspend; // o que o "Windows" responde sobre a suspensão seletiva de USB (null = não deu para ler)
        private string activePlan = "232e2748-0959-4183-a9e8-1b5372b4f906";

        public void Dispose()
        {
            dir.Dispose();
        }

        private ModeManager NewManager(AppConfig cfg)
        {
            var modes = new ModeManager(cfg);
            modes.PowerApplier = (p, warnings, restore) =>
            {
                Thread.Sleep(delayMs);
                lock (applied) applied.Add(p.PowerPlan + (restore != null ? "->" + restore : ""));
                if (p.PowerPlan == PowerPlanOption.DesempenhoMaximo) warnings.Add("falha de mentira");
            };
            // Trava: se o aplicador não for o daqui, para antes de enfileirar qualquer pedido.
            Type owner = modes.PowerApplier.Method.DeclaringType;
            Assert.True(owner == typeof(ModeManagerTests) || owner.DeclaringType == typeof(ModeManagerTests),
                "aplicador de mentira não instalado: " + owner);
            modes.UsbSuspendReader = () => usbSuspend;
            modes.ActivePlanReader = () => activePlan;
            return modes;
        }

        private static MouseProfile Plan(PowerPlanOption plan)
        {
            return new MouseProfile { PowerPlan = plan };
        }

        [Fact]
        public void QueuePower_ReturnsAtOnceAndAppliesTheLastRequest()
        {
            ModeManager modes = NewManager(new AppConfig());
            var warned = new List<string>();
            modes.PowerWarnings += w => { lock (warned) warned.AddRange(w); };

            var clock = Stopwatch.StartNew();
            modes.QueuePower(Plan(PowerPlanOption.AltoDesempenho), null);
            modes.QueuePower(Plan(PowerPlanOption.Equilibrado), null);
            modes.QueuePower(Plan(PowerPlanOption.DesempenhoMaximo), null);
            Assert.True(clock.ElapsedMilliseconds < 200, "a tela não pode esperar o powercfg");

            Assert.True(modes.WaitForPower(5000));
            Assert.Equal("DesempenhoMaximo", applied[applied.Count - 1]);
            Assert.True(applied.Count <= 2, "o pedido do meio devia ter sido substituído: " + string.Join(", ", applied));
            Assert.Contains("falha de mentira", warned);
        }

        [Fact]
        public void WaitForPower_TimesOutWhileAPlanIsStillBeingApplied()
        {
            ModeManager modes = NewManager(new AppConfig());
            delayMs = 1500;

            modes.QueuePower(Plan(PowerPlanOption.NaoAlterar), "00000000-0000-0000-0000-000000000001");

            Assert.False(modes.WaitForPower(100));
            Assert.True(modes.WaitForPower(5000));
            Assert.Equal(new[] { "NaoAlterar->00000000-0000-0000-0000-000000000001" }, applied.ToArray());
        }

        [Fact]
        public void Start_InDailyKeepsWindowsAsItIs()
        {
            AppConfig cfg = AppConfig.CreateDefault(); // LastMode = DiaADia
            cfg.Daily.PointerSpeed = cfg.Daily.PointerSpeed == 3 ? 4 : 3; // perfil diferente do Windows
            ModeManager modes = NewManager(cfg);
            Mode? changed = null;
            modes.ModeChanged += (mode, reason, warnings) => changed = mode;

            modes.Start();

            Assert.Equal(Mode.DiaADia, changed);
            Assert.Equal(MouseSettings.Read().PointerSpeed, cfg.Daily.PointerSpeed); // o perfil seguiu o Windows
            Assert.True(modes.WaitForPower(1000));
            Assert.Empty(applied); // nenhum plano de energia foi pedido
        }

        [Fact]
        public void SyncDailyFromWindows_TakesTheWindowsMouseAndAppliesNothing()
        {
            // Os Ajustes chamam isto antes de abrir no Dia a dia: a página mostra o mouse de verdade.
            MouseProfile windows = MouseSettings.Read();
            var cfg = new AppConfig { Daily = new MouseProfile { PowerPlan = PowerPlanOption.AltoDesempenho }, Performance = new MouseProfile() };
            cfg.Daily.PointerSpeed = windows.PointerSpeed == 3 ? 4 : 3;
            cfg.Daily.EnhancePointerPrecision = !windows.EnhancePointerPrecision;
            cfg.Daily.ScrollLines = windows.ScrollLines == 7 ? 8 : 7;
            ModeManager modes = NewManager(cfg);
            Mode? changed = null;
            modes.ModeChanged += (mode, reason, warnings) => changed = mode;

            modes.SyncDailyFromWindows();

            Assert.Equal(windows.PointerSpeed, cfg.Daily.PointerSpeed);
            Assert.Equal(windows.EnhancePointerPrecision, cfg.Daily.EnhancePointerPrecision);
            Assert.Equal(windows.ScrollLines, cfg.Daily.ScrollLines);
            Assert.Equal(PowerPlanOption.AltoDesempenho, cfg.Daily.PowerPlan); // o plano fica
            Assert.Null(changed);
            Assert.True(modes.WaitForPower(1000));
            Assert.Empty(applied);
        }

        [Fact]
        public void Start_FirstTimeTakesTheUsbSettingFromWindows()
        {
            AppConfig cfg = AppConfig.CreateDefault(); // Dia a dia nasce com a suspensão ativada (padrão do app)
            usbSuspend = 0; // a pessoa já tinha desativado no Windows
            ModeManager modes = NewManager(cfg);

            modes.Start();

            Assert.True(cfg.Daily.DisableUsbSelectiveSuspend);
            Assert.True(cfg.UsbSuspendSeeded);
            Assert.True(modes.WaitForPower(1000));
            Assert.Empty(applied); // só leu: nada foi gravado no Windows
        }

        [Fact]
        public void Start_AfterTheFirstTimeTheSettingsWin()
        {
            AppConfig cfg = AppConfig.CreateDefault();
            cfg.UsbSuspendSeeded = true; // já leu antes; depois a pessoa ligou a suspensão nos Ajustes
            usbSuspend = 0;              // o Windows ainda tem o valor do Performance, por exemplo

            NewManager(cfg).Start();

            Assert.False(cfg.Daily.DisableUsbSelectiveSuspend);
        }

        [Fact]
        public void Start_TriesAgainWhenTheUsbSettingCannotBeRead()
        {
            AppConfig cfg = AppConfig.CreateDefault();
            usbSuspend = null;

            NewManager(cfg).Start();
            Assert.False(cfg.UsbSuspendSeeded);
            Assert.False(cfg.Daily.DisableUsbSelectiveSuspend);

            usbSuspend = 1;
            NewManager(cfg).Start();
            Assert.True(cfg.UsbSuspendSeeded);
            Assert.False(cfg.Daily.DisableUsbSelectiveSuspend);
        }

        [Theory]
        [InlineData(PowerPlanOption.NaoAlterar)]
        [InlineData(PowerPlanOption.Equilibrado)]
        [InlineData(PowerPlanOption.AltoDesempenho)]
        public void RememberDailyPlan_SavesTheActivePlanWhateverTheDailyOption(PowerPlanOption daily)
        {
            // Mesmo sem "ANTERIOR" agora: a pessoa pode escolher "ANTERIOR" no meio do Performance.
            var cfg = new AppConfig { Daily = new MouseProfile { PowerPlan = daily }, Performance = new MouseProfile() };

            NewManager(cfg).RememberDailyPlan();

            Assert.Equal(activePlan, cfg.DailyPlanGuid);
        }

        [Fact]
        public void SetGameFocused_DoesNothingOutsidePerformance()
        {
            AppConfig cfg = new AppConfig { MouseOnlyWhenGameFocused = true, Daily = new MouseProfile() };
            ModeManager modes = NewManager(cfg);

            modes.SetGameFocused(false);

            Assert.False(modes.DailyMouseInUse);
        }

        [Theory]
        [InlineData("232E2748-0959-4183-A9E8-1B5372B4F906", "232e2748-0959-4183-a9e8-1b5372b4f906")]
        [InlineData("{232e2748-0959-4183-a9e8-1b5372b4f906}", "232e2748-0959-4183-a9e8-1b5372b4f906")]
        [InlineData("nao-e-guid", null)]
        [InlineData("232e2748-0959-4183-a9e8-1b5372b4f906 & calc", null)]
        [InlineData(null, null)]
        public void ValidGuid_OnlyLetsRealGuidsReachPowercfg(string value, string expected)
        {
            Assert.Equal(expected, ModeManager.ValidGuid(value));
        }
    }
}
