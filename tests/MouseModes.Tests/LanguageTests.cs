using System.Globalization;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>
    /// Escolha do idioma. Só a função pura: L.Init muda o estado global, e os outros testes (que rodam ao mesmo
    /// tempo) conferem textos em português.
    /// </summary>
    public class LanguageTests
    {
        [Theory]
        [InlineData("pt-BR", false)]
        [InlineData("pt-PT", false)]
        [InlineData("en-US", true)]
        [InlineData("es-ES", true)]
        [InlineData("de-DE", true)]
        public void FollowsWindowsWhenNothingWasChosen(string windows, bool english)
        {
            Assert.Equal(english, L.UseEnglish(null, new CultureInfo(windows)));
            Assert.Equal(english, L.UseEnglish("", new CultureInfo(windows)));
        }

        [Fact]
        public void TheChoiceWinsOverWindows()
        {
            Assert.True(L.UseEnglish("en", new CultureInfo("pt-BR")));
            Assert.False(L.UseEnglish("pt", new CultureInfo("en-US")));
            Assert.True(L.UseEnglish("EN", new CultureInfo("pt-BR")));
        }

        [Fact]
        public void UnknownChoiceFollowsWindows()
        {
            Assert.False(L.UseEnglish("fr", new CultureInfo("pt-BR")));
            Assert.True(L.UseEnglish("fr", new CultureInfo("en-US")));
            Assert.True(L.UseEnglish(null, null));
        }

        [Fact]
        public void PortugueseUntilInitIsCalled()
        {
            // Nenhum teste chama L.Init: os textos ficam em português, que é o que os outros testes conferem.
            Assert.False(L.IsEnglish);
            Assert.Equal("Ajustes", L.T("Ajustes", "Settings"));
        }

        [Fact]
        public void TheLanguageGoesWithTheExportedConfig()
        {
            var here = new AppConfig();
            here.CopySettingsFrom(new AppConfig { Language = "en" }.ForExport());
            Assert.Equal("en", here.Language);
        }
    }
}
