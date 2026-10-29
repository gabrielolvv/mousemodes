using System;
using System.IO;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>
    /// Atualização sozinha: links, hosts aceitos, leitura do .sha256 e a conferência do arquivo baixado.
    /// Nada aqui acessa a internet nem roda instalador.
    /// </summary>
    public class UpdaterTests
    {
        [Fact]
        public void AssetUrl_PointsToThisRepositoryRelease()
        {
            Assert.Equal("https://github.com/gabrielolvv/mousemodes/releases/download/v1.5.0/MouseModes-Setup.exe",
                Updater.AssetUrl("v1.5.0", Updater.Installer));
            // Uma tag estranha não escapa do caminho.
            Assert.StartsWith(Updater.DownloadBase + "..%2F", Updater.AssetUrl("../evil", Updater.Installer));
        }

        [Theory]
        [InlineData("https://github.com/gabrielolvv/mousemodes/releases/download/v1.5.0/MouseModes-Setup.exe", true)]
        [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1/2", true)]
        [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1/2", true)]
        [InlineData("http://github.com/gabrielolvv/mousemodes/releases/download/v1.5.0/MouseModes-Setup.exe", false)]
        [InlineData("https://github.com.evil.example/x", false)]
        [InlineData("https://evil.example/MouseModes-Setup.exe", false)]
        [InlineData("https://github.com:8443/x", false)]
        public void IsAllowed_OnlyAcceptsGitHubOverHttps(string url, bool expected)
        {
            Assert.Equal(expected, Updater.IsAllowed(new Uri(url)));
        }

        [Theory]
        [InlineData("18fae039f670e4064143bf07693129055347f9a6a310adee30cbd891cf30a678  MouseModes-Setup.exe\n")]
        [InlineData("18FAE039F670E4064143BF07693129055347F9A6A310ADEE30CBD891CF30A678 *MouseModes-Setup.exe\r\n")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  other.exe\n18fae039f670e4064143bf07693129055347f9a6a310adee30cbd891cf30a678  MouseModes-Setup.exe")]
        public void ParseSha256_ReadsTheSha256sumFormat(string content)
        {
            Assert.Equal("18fae039f670e4064143bf07693129055347f9a6a310adee30cbd891cf30a678", Updater.ParseSha256(content, Updater.Installer));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("18fae039  MouseModes-Setup.exe")]                                                      // hash curto
        [InlineData("18fae039f670e4064143bf07693129055347f9a6a310adee30cbd891cf30a678  OtherSetup.exe")]   // outro arquivo
        [InlineData("<html>Not Found</html>")]
        public void ParseSha256_RejectsAnythingElse(string content)
        {
            Assert.Null(Updater.ParseSha256(content, Updater.Installer));
        }

        [Theory]
        [InlineData(@"C:\Users\a\AppData\Local\Programs\MouseModes", @"c:\users\a\appdata\local\programs\mousemodes\", true)]
        [InlineData(@"C:\Users\a\Downloads", @"C:\Users\a\AppData\Local\Programs\MouseModes", false)]
        [InlineData(@"C:\Users\a\AppData\Local\Programs\MouseModes2", @"C:\Users\a\AppData\Local\Programs\MouseModes", false)]
        [InlineData(null, @"C:\x", false)]
        public void SameFolder_IgnoresCaseAndTheTrailingSlash(string a, string b, bool expected)
        {
            Assert.Equal(expected, Updater.SameFolder(a, b));
        }

        [Fact]
        public void IsInstalledCopy_IsFalseForALooseCopy()
        {
            // Uma cópia solta (Downloads, pendrive) só avisa: o instalador gravaria em outra pasta.
            Assert.False(Updater.IsInstalledCopy(@"C:\Temp\mm-loose-copy\MouseModes.exe"));
        }

        [Fact]
        public void Verify_AcceptsTheRightFileWithTheRightHashAndVersion()
        {
            // O próprio MouseModes.exe faz o papel do instalador: produto "MouseModes" e a versão deste build.
            string exe = typeof(Updater).Assembly.Location;
            using (var temp = new TempFolder())
            {
                string copy = temp.File("MouseModes-Setup.exe");
                File.Copy(exe, copy);
                Assert.Null(Updater.Verify(copy, Updater.Sha256Of(copy), UpdateChecker.Current));
            }
        }

        [Fact]
        public void Verify_RefusesAnotherVersionOrAWrongHash()
        {
            string exe = typeof(Updater).Assembly.Location;
            using (var temp = new TempFolder())
            {
                string copy = temp.File("MouseModes-Setup.exe");
                File.Copy(exe, copy);
                string hash = Updater.Sha256Of(copy);

                Assert.Equal("versão do instalador diferente da Release", Updater.Verify(copy, hash, new Version(99, 0, 0)));
                Assert.Equal("SHA-256 diferente do publicado na Release", Updater.Verify(copy, new string('0', 64), UpdateChecker.Current));
                Assert.Equal("SHA-256 diferente do publicado na Release", Updater.Verify(copy, null, UpdateChecker.Current));
            }
        }

        [Fact]
        public void Verify_RefusesFilesThatAreNotTheInstaller()
        {
            using (var temp = new TempFolder())
            {
                string html = temp.Text("page.exe", "<html>" + new string(' ', 4096) + "</html>");
                Assert.Equal("não é um executável", Updater.Verify(html, Updater.Sha256Of(html), UpdateChecker.Current));

                // Começa com MZ e bate com o hash, mas não diz que é o MouseModes.
                string fake = temp.File("fake.exe");
                var bytes = new byte[4096];
                bytes[0] = (byte)'M';
                bytes[1] = (byte)'Z';
                File.WriteAllBytes(fake, bytes);
                Assert.Equal("não é o instalador do MouseModes", Updater.Verify(fake, Updater.Sha256Of(fake), UpdateChecker.Current));

                string tiny = temp.Text("tiny.exe", "MZ");
                Assert.Equal("tamanho inesperado", Updater.Verify(tiny, Updater.Sha256Of(tiny), UpdateChecker.Current));
                Assert.Equal("tamanho inesperado", Updater.Verify(temp.File("missing.exe"), null, UpdateChecker.Current));
            }
        }
    }
}
