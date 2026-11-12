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

        [Fact]
        public void FpsTargetText_ShowsTheExeTheCapGoesTo()
        {
            var cfg = new AppConfig();
            cfg.Games.AddRange(new[] { "cs2", "FortniteClient", "FiveM_" });

            // Ainda não visto: o nome da lista com ".exe", que é para onde o limite vai mesmo, marcado como palpite.
            Assert.Equal("cs2.exe?", GamesPanel.FpsTargetText(cfg, "cs2", 144));
            Assert.Equal("cs2.exe?", GamesPanel.FpsTargetText(cfg, "cs2", 0));
            Assert.Equal("fortniteclient.exe?", GamesPanel.FpsTargetText(cfg, "FortniteClient", 0));

            // Visto aberto: o executável exato, sem "?".
            Assert.True(cfg.RememberGameExe("FortniteClient-Win64-Shipping"));
            Assert.Equal("fortniteclient-win64-shipping.exe", GamesPanel.FpsTargetText(cfg, "FortniteClient", 144));

            // Começo de nome com certeza: com limite, pede para abrir o jogo; sem limite, não pede nada.
            Assert.Equal("abra o jogo uma vez", GamesPanel.FpsTargetText(cfg, "FiveM_", 144));
            Assert.Equal("fivem_.exe?", GamesPanel.FpsTargetText(cfg, "FiveM_", 0));
        }

        [Fact]
        public void ClearlyPrefix_OnlyForNamesEndingInASeparator()
        {
            Assert.True(GamesPanel.ClearlyPrefix("FiveM_"));
            Assert.True(GamesPanel.ClearlyPrefix("Jogo-"));
            Assert.False(GamesPanel.ClearlyPrefix("cs2"));
            Assert.False(GamesPanel.ClearlyPrefix("cs2.exe"));
            Assert.False(GamesPanel.ClearlyPrefix("League of Legends"));
            Assert.False(GamesPanel.ClearlyPrefix(""));
            Assert.False(GamesPanel.ClearlyPrefix(null));
        }
    }
}
