using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Interpretação dos valores do registro. Nada aqui muda o Windows.</summary>
    public class WindowsChecksTests
    {
        [Theory]
        [InlineData(null, true)] // sem valor: ligado por padrão
        [InlineData(1, true)]
        [InlineData(0, false)]
        public void GameMode(object value, bool on)
        {
            WindowsCheck check = WindowsChecks.GameMode(value);
            Assert.Equal(on, check.Ok);
            Assert.Equal(on ? "Ligado" : "Desligado", check.State);
            Assert.Equal(on, check.Advice.Length == 0);
            Assert.Equal("ms-settings:gaming-gamemode", check.SettingsUri);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(0, false)]
        [InlineData(1, true)]
        public void BackgroundRecording(object value, bool recording)
        {
            WindowsCheck check = WindowsChecks.BackgroundRecording(value, 19045);
            Assert.Equal(!recording, check.Ok);
            Assert.Equal(recording ? "Ligada" : "Desligada", check.State);
        }

        [Fact]
        public void BackgroundRecording_OpensTheRightPageOnEachWindows()
        {
            Assert.Equal("ms-settings:gaming-gamedvr", WindowsChecks.BackgroundRecording(1, 19045).SettingsUri);
            Assert.Equal("ms-settings:gaming-captures", WindowsChecks.BackgroundRecording(1, 22631).SettingsUri);
        }

        [Fact]
        public void HardwareScheduling()
        {
            Assert.True(WindowsChecks.HardwareScheduling(2).Ok);
            Assert.Equal("Ligado", WindowsChecks.HardwareScheduling(2).State);

            WindowsCheck off = WindowsChecks.HardwareScheduling(1);
            Assert.Null(off.Ok); // depende da placa: não é marcado como errado
            Assert.Equal("Desligado", off.State);
            Assert.NotEmpty(off.Advice);

            WindowsCheck unsupported = WindowsChecks.HardwareScheduling(null);
            Assert.Null(unsupported.Ok);
            Assert.Equal("Sem suporte", unsupported.State);
        }

        [Theory]
        [InlineData("SwapEffectUpgradeEnable=1;", true, "Ligadas")]
        [InlineData("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", true, "Ligadas")]
        [InlineData(" swapeffectupgradeenable = 1 ;", true, "Ligadas")]
        [InlineData("SwapEffectUpgradeEnable=0;", false, "Desligadas")]
        [InlineData("VRROptimizeEnable=0;", null, "Padrão do Windows")]
        [InlineData("", null, "Padrão do Windows")]
        [InlineData(null, null, "Padrão do Windows")]
        public void WindowedOptimizations(string settings, bool? ok, string state)
        {
            WindowsCheck check = WindowsChecks.WindowedOptimizations(settings);
            Assert.Equal(ok, check.Ok);
            Assert.Equal(state, check.State);
        }

        [Fact]
        public void Read_ReturnsEveryCheckWithAPageToOpen()
        {
            using (new TempFolder()) // desvia o log
            {
                List<WindowsCheck> checks = WindowsChecks.Read();

                Assert.InRange(checks.Count, 3, 4); // a 4ª só no Windows 11
                foreach (WindowsCheck check in checks)
                {
                    Assert.False(string.IsNullOrEmpty(check.Name));
                    Assert.False(string.IsNullOrEmpty(check.State));
                    Assert.StartsWith("ms-settings:", check.SettingsUri);
                }
            }
        }
    }
}
