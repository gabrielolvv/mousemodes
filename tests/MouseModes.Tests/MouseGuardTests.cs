using System;
using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Vigia do mouse com perfis inventados: nada aqui lê nem muda o mouse do Windows.</summary>
    public class MouseGuardTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);

        private static MouseProfile Mouse(int speed = 10, bool accel = false, int scroll = 3)
        {
            return new MouseProfile { PointerSpeed = speed, EnhancePointerPrecision = accel, ScrollLines = scroll };
        }

        [Fact]
        public void NothingToDoWhileTheMouseIsTheModes()
        {
            var guard = new MouseGuard();
            // O resto do perfil (plano, USB, limite de FPS) não é do mouse do Windows: não conta.
            MouseProfile expected = Mouse();
            expected.PowerPlan = PowerPlanOption.DesempenhoMaximo;
            expected.FpsCap = 144;

            Assert.Equal(GuardAction.None, guard.Check(expected, Mouse(), T0));
            Assert.Equal(GuardAction.None, guard.Check(null, Mouse(accel: true), T0)); // fora do Performance
            Assert.Equal(0, guard.FixesSinceReset);
        }

        [Fact]
        public void ReappliesAFewTimesThenGivesUpUntilTheNextModeSwitch()
        {
            var guard = new MouseGuard();
            MouseProfile expected = Mouse(), changed = Mouse(accel: true);

            for (int i = 0; i < MouseGuard.MaxFixes; i++)
                Assert.Equal(GuardAction.Reapply, guard.Check(expected, changed, T0.AddMinutes(i)));
            Assert.Equal(MouseGuard.MaxFixes, guard.FixesSinceReset);

            Assert.Equal(GuardAction.GiveUp, guard.Check(expected, changed, T0.AddMinutes(4)));
            Assert.Equal(GuardAction.None, guard.Check(expected, changed, T0.AddMinutes(5))); // desistiu: não briga

            guard.Reset(); // trocou de modo
            Assert.Equal(GuardAction.Reapply, guard.Check(expected, changed, T0.AddMinutes(6)));
            Assert.Equal(1, guard.FixesSinceReset);
        }

        [Fact]
        public void OldFixesStopCountingAfterTheWindow()
        {
            var guard = new MouseGuard();
            MouseProfile expected = Mouse(), changed = Mouse(speed: 14);

            for (int i = 0; i < MouseGuard.MaxFixes; i++) guard.Check(expected, changed, T0.AddMinutes(i));

            // Uma mudança de vez em quando (o software do mouse abrindo de novo) não leva a desistir.
            Assert.Equal(GuardAction.Reapply, guard.Check(expected, changed, T0 + MouseGuard.FixWindow + TimeSpan.FromMinutes(1)));
        }

        [Theory]
        [InlineData(25, 3, 20, 3)]      // velocidade escrita à mão acima de 20: o Windows guarda 20
        [InlineData(0, 3, 1, 3)]
        [InlineData(10, 0, 10, 1)]      // rolagem 0 vira 1
        [InlineData(10, 150, 10, 100)]
        [InlineData(10, -3, 10, 1)]
        [InlineData(10, MouseProfile.PageScroll, 10, MouseProfile.PageScroll)] // a tela por vez continua tela por vez
        public void ComparesWithWhatWindowsReallyStores(int speed, int scroll, int windowsSpeed, int windowsScroll)
        {
            var guard = new MouseGuard();
            Assert.Equal(GuardAction.None, guard.Check(Mouse(speed, scroll: scroll), Mouse(windowsSpeed, scroll: windowsScroll), T0));
            Assert.Equal("", MouseGuard.Describe(Mouse(speed, scroll: scroll), Mouse(windowsSpeed, scroll: windowsScroll)));
        }

        [Fact]
        public void DescribesWhatChanged()
        {
            Assert.Equal("", MouseGuard.Describe(Mouse(), Mouse()));
            string text = MouseGuard.Describe(Mouse(speed: 10, accel: false, scroll: 3), Mouse(speed: 14, accel: true, scroll: MouseProfile.PageScroll));
            Assert.Contains("10 → 14", text);
            Assert.Contains("3 → " + MouseSettings.DescribeScroll(MouseProfile.PageScroll), text);
            Assert.Equal(3, text.Split(new[] { " · " }, StringSplitOptions.None).Length);
        }

        [Fact]
        public void PointsToMouseSoftwareThatIsOpen()
        {
            List<string> found = MouseGuard.LikelyCulprits(new[] { "chrome", "lghub_agent", "lghub", "RazerAppEngine", "explorer", null });
            Assert.Equal(new[] { "Logitech G HUB", "Razer Synapse" }, found.ToArray());
            Assert.Empty(MouseGuard.LikelyCulprits(new[] { "chrome", "cs2" }));

            Assert.Contains("Logitech G HUB", MouseGuard.FixedMessage("x", found));
            Assert.DoesNotContain(L.T("Pode ter sido", "It may have been"), MouseGuard.FixedMessage("x", new List<string>()));
            Assert.Contains("x", MouseGuard.GaveUpMessage("x", null));
        }

        [Fact]
        public void ExpectedMouseIsTheAppliedProfileOrTheDailyOneWithTheGameOffScreen()
        {
            MouseProfile perf = Mouse(speed: 8), daily = Mouse(speed: 12, accel: true);

            Assert.Same(perf, ModeManager.Expected(Mode.Performance, false, perf, daily));
            Assert.Same(daily, ModeManager.Expected(Mode.Performance, true, perf, daily)); // alt-tab com a opção ligada
            Assert.Null(ModeManager.Expected(Mode.DiaADia, false, daily, daily)); // no Dia a dia o mouse é o da pessoa
        }

        [Fact]
        public void GuardIsOnForOldConfigsWithoutTheSetting()
        {
            using (var temp = new TempFolder())
            {
                string path = temp.Text("config.xml", "<?xml version=\"1.0\"?><AppConfig><AutoSwitch>true</AutoSwitch></AppConfig>");
                Assert.True(AppConfig.LoadOrCreate(path).GuardMouse);
            }
        }
    }
}
