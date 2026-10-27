using System;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Leitura da resposta do GitHub e comparação de versões. Nenhum teste acessa a internet.</summary>
    public class UpdateCheckerTests
    {
        [Fact]
        public void Parse_ReadsTheTagAndBuildsTheLinkItself()
        {
            // Trecho no formato da API (valores inventados), com um html_url de outro lugar antes do tag_name.
            const string json = "{\"html_url\":\"https://exemplo.invalid/outra-coisa\",\"id\":1," +
                                "\"author\":{\"html_url\":\"https://exemplo.invalid/autor\"},\"tag_name\":\"v1.2.0\",\"draft\":false}";

            ReleaseInfo release = UpdateChecker.Parse(json);

            Assert.Equal("v1.2.0", release.Tag);
            Assert.Equal(new Version(1, 2, 0), release.Version);
            Assert.Equal("https://github.com/gabrielolvv/mousemodes/releases/tag/v1.2.0", release.Url);
        }

        [Theory]
        [InlineData("{\"tag_name\":\"1.3\"}", "1.3.0")]
        [InlineData("{ \"tag_name\" : \"v2.0.1\" }", "2.0.1")]
        public void Parse_AcceptsTagsWithOrWithoutV(string json, string version)
        {
            Assert.Equal(Version.Parse(version), UpdateChecker.Parse(json).Version);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("{\"message\":\"Not Found\"}")]
        [InlineData("{\"tag_name\":\"ultima\"}")]
        [InlineData("{\"tag_name\":\"v1\"}")]
        public void Parse_ReturnsNullWithoutAVersion(string json)
        {
            Assert.Null(UpdateChecker.Parse(json));
        }

        [Theory]
        [InlineData("1.2.0", "1.1.1", true)]
        [InlineData("1.1.2", "1.1.1", true)]
        [InlineData("1.1.1", "1.1.1", false)]
        [InlineData("1.1.0", "1.1.1", false)]
        [InlineData("1.1.1", "1.1.1.0", false)] // o exe tem quatro partes; a tag, três
        [InlineData("1.2", "1.1.9", true)]
        public void IsNewer_ComparesThreeParts(string latest, string current, bool newer)
        {
            Assert.Equal(newer, UpdateChecker.IsNewer(Version.Parse(latest), Version.Parse(current)));
        }

        [Theory]
        [InlineData("https://github.com/gabrielolvv/mousemodes/releases/tag/v1.2.0", true)]
        [InlineData("https://github.com/outra-pessoa/outro-app/releases/tag/v1.2.0", false)]
        [InlineData("https://github.com/gabrielolvv/mousemodes/releases/tag/v1 \"&calc", false)]
        [InlineData("http://github.com/gabrielolvv/mousemodes/releases/", false)]
        [InlineData(null, false)]
        public void IsReleaseLink_OnlyOpensTheAppsReleasesPage(string url, bool allowed)
        {
            Assert.Equal(allowed, UpdateChecker.IsReleaseLink(url));
        }

        [Fact]
        public void CheckForUpdatesIsOnByDefaultAndItsStateStaysOnThisPc()
        {
            var cfg = new AppConfig { LastUpdateCheck = DateTime.UtcNow, NotifiedVersion = "v1.2.0" };
            Assert.True(cfg.CheckForUpdates);

            AppConfig export = cfg.ForExport();
            Assert.Null(export.LastUpdateCheck);
            Assert.Null(export.NotifiedVersion);
            Assert.True(export.CheckForUpdates);
        }
    }
}
