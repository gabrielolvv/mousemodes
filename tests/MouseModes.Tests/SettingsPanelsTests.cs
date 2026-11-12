using System.Drawing;
using System.Reflection;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>
    /// Páginas dos Ajustes sem abrir a janela: só os controles, e nada é aplicado no Windows (o SaveTo só grava no perfil
    /// e no config em memória).
    /// </summary>
    public class SettingsPanelsTests
    {
        private static T Field<T>(object owner, string name)
        {
            return (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
        }

        private static MouseProfile Daily()
        {
            return new MouseProfile { PointerSpeed = 8, EnhancePointerPrecision = true, ScrollLines = 3, PowerPlan = PowerPlanOption.Equilibrado };
        }

        [Fact]
        public void ProfilePanel_KeepsWhatChangedUnderTheOpenPage()
        {
            MouseProfile daily = Daily();
            using (var panel = new ProfilePanel(Color.Red, Color.Blue, "dica"))
            {
                panel.LoadProfile(daily);
                // Com a janela aberta, um jogo entrou no Performance e o Dia a dia seguiu o mouse do Windows.
                daily.PointerSpeed = 14;
                daily.EnhancePointerPrecision = false;
                daily.ScrollLines = 5;
                daily.DisableUsbSelectiveSuspend = true;

                panel.SaveTo(daily);
            }

            Assert.Equal(14, daily.PointerSpeed);
            Assert.False(daily.EnhancePointerPrecision);
            Assert.Equal(5, daily.ScrollLines);
            Assert.True(daily.DisableUsbSelectiveSuspend);
            Assert.Equal(PowerPlanOption.Equilibrado, daily.PowerPlan);
        }

        [Fact]
        public void ProfilePanel_SavesWhatThePersonChanged()
        {
            MouseProfile daily = Daily();
            using (var panel = new ProfilePanel(Color.Red, Color.Blue, "dica"))
            {
                panel.LoadProfile(daily);
                daily.EnhancePointerPrecision = false; // veio do Windows
                Field<NeonSlider>(panel, "speed").Value = 12; // mexido na página

                panel.SaveTo(daily);
            }

            Assert.Equal(12, daily.PointerSpeed);
            Assert.False(daily.EnhancePointerPrecision);
            Assert.Equal(3, daily.ScrollLines);
        }

        [Fact]
        public void GamesPanel_DoesNotUndoTheTrayAutoSwitch()
        {
            var cfg = new AppConfig { AutoSwitch = true };
            using (var panel = new GamesPanel(cfg, null))
            {
                cfg.AutoSwitch = false; // desligada pela bandeja com os Ajustes abertos
                panel.SaveTo(cfg);
                Assert.False(cfg.AutoSwitch);

                Field<ToggleRow>(panel, "autoSwitch").Switch.SetChecked(false, false); // agora mexida na página
                cfg.AutoSwitch = true;
                panel.SaveTo(cfg);
                Assert.False(cfg.AutoSwitch);
            }
        }
    }
}
