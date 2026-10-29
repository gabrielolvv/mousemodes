using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Resolução por jogo com um monitor de mentira: nenhum teste muda a resolução de verdade.</summary>
    public class GameResolutionTests
    {
        private sealed class FakeScreen : IScreenModes
        {
            public readonly List<string> Calls = new List<string>();
            public bool Accept = true;

            public bool Set(int width, int height, out int hz, out string error)
            {
                Calls.Add("set " + width + "x" + height);
                hz = Accept ? 144 : 0;
                error = Accept ? null : "o monitor principal não aceita";
                return Accept;
            }

            public void Reset()
            {
                Calls.Add("reset");
            }
        }

        private static GameResolution New(AppConfig cfg, FakeScreen screen)
        {
            return new GameResolution(cfg, () => { }, screen);
        }

        [Theory]
        [InlineData("1280x960", 1280, 960)]
        [InlineData("1280×960", 1280, 960)]
        [InlineData(" 1440 X 1080 ", 1440, 1080)]
        public void TryParse_ReadsBothSeparators(string text, int width, int height)
        {
            int w, h;
            Assert.True(GameResolution.TryParse(text, out w, out h));
            Assert.Equal(width, w);
            Assert.Equal(height, h);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("nativa")]
        [InlineData("1280")]
        [InlineData("100x100")]      // pequena demais para ser uma resolução de monitor
        [InlineData("-1280x960")]
        public void TryParse_RejectsAnythingElse(string text)
        {
            int w, h;
            Assert.False(GameResolution.TryParse(text, out w, out h));
        }

        [Fact]
        public void Ensure_ChangesOnceAndGoesBackWhenTheGameCloses()
        {
            var cfg = new AppConfig();
            var screen = new FakeScreen();
            GameResolution resolution = New(cfg, screen);

            resolution.Ensure("1280x960");
            resolution.Ensure("1280×960"); // a cada 3 s, com o jogo aberto: não mexe de novo
            Assert.Equal(new[] { "set 1280x960" }, screen.Calls.ToArray());
            Assert.True(cfg.ResolutionChanged);
            Assert.Equal("1280x960", resolution.Applied);

            resolution.Ensure(null); // o jogo fechou
            Assert.Equal(new[] { "set 1280x960", "reset" }, screen.Calls.ToArray());
            Assert.False(cfg.ResolutionChanged);
            Assert.Null(resolution.Applied);
        }

        [Fact]
        public void Ensure_DoesNothingWithoutAResolutionToApply()
        {
            var cfg = new AppConfig();
            var screen = new FakeScreen();
            GameResolution resolution = New(cfg, screen);

            resolution.Ensure(null);
            resolution.Ensure("nativa");
            Assert.Empty(screen.Calls);
            Assert.False(cfg.ResolutionChanged);
        }

        [Fact]
        public void Ensure_DoesNotRetryAResolutionTheMonitorRefused()
        {
            var cfg = new AppConfig();
            var screen = new FakeScreen { Accept = false };
            GameResolution resolution = New(cfg, screen);

            resolution.Ensure("1440x1080");
            resolution.Ensure("1440x1080");
            Assert.Equal(new[] { "set 1440x1080" }, screen.Calls.ToArray());
            Assert.False(cfg.ResolutionChanged);

            // Fechou o jogo e abriu de novo: tenta outra vez.
            resolution.Ensure(null);
            resolution.Ensure("1440x1080");
            Assert.Equal(new[] { "set 1440x1080", "set 1440x1080" }, screen.Calls.ToArray());
        }

        [Fact]
        public void Ensure_SwitchesBetweenTwoGames()
        {
            var cfg = new AppConfig();
            var screen = new FakeScreen();
            GameResolution resolution = New(cfg, screen);

            resolution.Ensure("1280x960");
            resolution.Ensure("1024x768");
            Assert.Equal(new[] { "set 1280x960", "set 1024x768" }, screen.Calls.ToArray());
            Assert.Equal("1024x768", resolution.Applied);
        }

        [Fact]
        public void Recover_UndoesWhatACrashLeftBehind()
        {
            var cfg = new AppConfig { ResolutionChanged = true };
            var screen = new FakeScreen();

            GameResolution.Recover(cfg, () => { }, screen);
            Assert.Equal(new[] { "reset" }, screen.Calls.ToArray());
            Assert.False(cfg.ResolutionChanged);

            GameResolution.Recover(cfg, () => { }, screen); // já desfeito: não mexe
            Assert.Single(screen.Calls);
        }

        [Fact]
        public void Resolution_IsCopiedWithTheProfileAndNotExportedAsMachineState()
        {
            var profile = new MouseProfile { Resolution = "1280x960" };
            Assert.Equal("1280x960", profile.Clone().Resolution);

            var cfg = new AppConfig { ResolutionChanged = true };
            Assert.False(cfg.ForExport().ResolutionChanged);
            Assert.Contains("ResolutionChanged", AppConfig.MachineOnly);
        }
    }
}
