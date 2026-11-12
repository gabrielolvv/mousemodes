using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Só a conversão das linhas de rolagem: nada aqui grava no Windows.</summary>
    public class MouseSettingsTests
    {
        [Theory]
        [InlineData(-1, MouseProfile.PageScroll)] // 0xFFFFFFFF: "uma tela por vez"
        [InlineData(1, 1)]
        [InlineData(3, 3)]
        [InlineData(100, 100)]
        public void ScrollFromWindows_KeepsPageScroll(int raw, int expected)
        {
            Assert.Equal(expected, MouseSettings.ScrollFromWindows(raw));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(101)]
        [InlineData(-2)]
        public void ScrollFromWindows_IgnoresOutOfRange(int raw)
        {
            Assert.Null(MouseSettings.ScrollFromWindows(raw));
        }

        [Theory]
        [InlineData(MouseProfile.PageScroll, uint.MaxValue)]
        [InlineData(3, 3u)]
        [InlineData(0, 1u)]
        [InlineData(-5, 1u)]
        [InlineData(500, 100u)]
        public void ScrollToWindows_WritesPageScrollBack(int lines, uint expected)
        {
            Assert.Equal(expected, MouseSettings.ScrollToWindows(lines));
        }

        [Fact]
        public void PageScrollSurvivesTheConfigAndIsDescribed()
        {
            using (var dir = new TempFolder())
            {
                string path = dir.File("config.xml");
                var cfg = new AppConfig { Daily = new MouseProfile { ScrollLines = MouseProfile.PageScroll }, Performance = new MouseProfile() };
                cfg.SaveTo(path);

                Assert.Equal(MouseProfile.PageScroll, AppConfig.LoadOrCreate(path).Daily.ScrollLines);
            }
            Assert.Equal("7", MouseSettings.DescribeScroll(7));
            Assert.NotEqual("-1", MouseSettings.DescribeScroll(MouseProfile.PageScroll));
        }
    }
}
