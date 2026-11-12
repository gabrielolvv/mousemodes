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
        public void FpsLabelTexts_DropTheCardThenTheRateButNeverTheExe()
        {
            Assert.Equal(new[] { "144 FPS · NVIDIA · cs2.exe?", "144 FPS · cs2.exe?", "cs2.exe?" },
                GamesPanel.FpsLabelTexts("NVIDIA", 144, 144, "cs2.exe?"));
            Assert.Equal(new[] { "taxa desconhecida · NVIDIA · abra o jogo uma vez", "taxa desconhecida · abra o jogo uma vez", "abra o jogo uma vez" },
                GamesPanel.FpsLabelTexts("NVIDIA", FpsCap.Auto, 0, "abra o jogo uma vez"));
            // Sem limite escolhido não há taxa: só a placa sai.
            Assert.Equal(new[] { "Intel Arc · fivem_.exe?", "fivem_.exe?" }, GamesPanel.FpsLabelTexts("Intel Arc", 0, 0, "fivem_.exe?"));
        }

        [Fact]
        public void SectionLabel_DrawsTheLongestTextThatFits()
        {
            var widths = new[] { 300f, 180f, 90f };
            Assert.Equal(0, SectionLabel.FirstThatFits(widths, 300f));
            Assert.Equal(1, SectionLabel.FirstThatFits(widths, 299f));
            Assert.Equal(2, SectionLabel.FirstThatFits(widths, 120f));
            Assert.Equal(2, SectionLabel.FirstThatFits(widths, 40f)); // nem a última cabe: ela, com "…"
            Assert.Equal(0, SectionLabel.FirstThatFits(new[] { 500f }, 10f));
            Assert.Equal(0, SectionLabel.FirstThatFits(new float[0], 10f));
        }

        [Fact]
        public void SectionLabel_RightTextStillWorksAlone()
        {
            using (var label = new SectionLabel())
            {
                label.RightTextOptions = new[] { "144 FPS · NVIDIA · cs2.exe?", "cs2.exe?" };
                Assert.Equal("144 FPS · NVIDIA · cs2.exe?", label.RightText);

                label.RightText = "esta placa não tem limitador"; // descarta as alternativas
                Assert.Null(label.RightTextOptions);
                Assert.Equal("esta placa não tem limitador", label.RightText);

                label.RightTextOptions = new string[0];
                Assert.Null(label.RightTextOptions);
                Assert.Equal("", label.RightText);
            }
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
