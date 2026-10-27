using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace MouseModes.Tests
{
    public class ConfigTests
    {
        private static AppConfig RoundTrip(AppConfig cfg)
        {
            var serializer = new XmlSerializer(typeof(AppConfig));
            var writer = new StringWriter();
            serializer.Serialize(writer, cfg);
            return (AppConfig)serializer.Deserialize(new StringReader(writer.ToString()));
        }

        [Fact]
        public void SaveTo_CreatesThenReplacesWithoutLeavingTmp()
        {
            using (var dir = new TempFolder())
            {
                string path = dir.File(@"cfg\config.xml");
                var cfg = new AppConfig { Hotkey = 1 };

                cfg.SaveTo(path);
                cfg.Hotkey = 2;
                cfg.SaveTo(path);

                Assert.Equal(2, AppConfig.LoadOrCreate(path).Hotkey);
                Assert.False(File.Exists(path + ".tmp"));
            }
        }

        [Fact]
        public void LoadOrCreate_CorruptFileGoesToBakAndStartsOver()
        {
            using (var dir = new TempFolder())
            {
                string path = dir.Text("config.xml", "<AppConfig><Hotkey>3</Hot");

                AppConfig loaded = AppConfig.LoadOrCreate(path);

                Assert.Equal(0, loaded.Hotkey);
                Assert.Contains("</Hot", File.ReadAllText(path + ".bak"));
            }
        }

        [Fact]
        public void LoadOrCreate_RecoversWhenOnlyTheTmpIsLeft()
        {
            using (var dir = new TempFolder())
            {
                string path = dir.File("config.xml");
                new AppConfig { Hotkey = 3 }.SaveTo(path);
                File.Move(path, path + ".tmp"); // a versão antiga caiu entre apagar e mover

                Assert.Equal(3, AppConfig.LoadOrCreate(path).Hotkey);
                Assert.True(File.Exists(path));
            }
        }

        [Fact]
        public void OldConfigWithoutLastModeReadsAsUnknown()
        {
            const string old = "<?xml version=\"1.0\"?><AppConfig xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><AutoSwitch>true</AutoSwitch></AppConfig>";
            var cfg = (AppConfig)new XmlSerializer(typeof(AppConfig)).Deserialize(new StringReader(old));
            Assert.Null(cfg.LastMode);
        }

        [Fact]
        public void NewFieldsSurviveTheXml()
        {
            var cfg = new AppConfig
            {
                LastMode = Mode.Performance,
                DailyPlanGuid = "381b4222-f694-41f0-9685-ff5bb260df2e",
                MouseOnlyWhenGameFocused = true
            };
            cfg.RememberPlanCopy("e9a42b02-d5df-448d-aa00-03f14749eb61", "232e2748-0959-4183-a9e8-1b5372b4f906");

            AppConfig back = RoundTrip(cfg);

            Assert.Equal(Mode.Performance, back.LastMode);
            Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", back.DailyPlanGuid);
            Assert.True(back.MouseOnlyWhenGameFocused);
            Assert.Equal("232e2748-0959-4183-a9e8-1b5372b4f906", back.FindPlanCopy("e9a42b02-d5df-448d-aa00-03f14749eb61"));
        }

        [Fact]
        public void CreateDefault_StartsInDailyWithThePreviousPlan()
        {
            AppConfig cfg = AppConfig.CreateDefault(); // só lê o mouse do Windows
            Assert.Equal(Mode.DiaADia, cfg.LastMode);
            Assert.Equal(PowerPlanOption.NaoAlterar, cfg.Daily.PowerPlan);
            Assert.Equal(PowerPlanOption.AltoDesempenho, cfg.Performance.PowerPlan);
        }

        [Fact]
        public void PlanCopies_RememberReplaceForgetAndRejectInvalidGuids()
        {
            const string ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
            var cfg = new AppConfig();

            cfg.RememberPlanCopy(ultimate, "232E2748-0959-4183-A9E8-1B5372B4F906");
            Assert.Equal("232e2748-0959-4183-a9e8-1b5372b4f906", cfg.FindPlanCopy(ultimate.ToUpperInvariant()));

            cfg.RememberPlanCopy(ultimate, "d62dc7a9-e82f-40eb-8ba9-4b6cf1ab4ed1");
            Assert.Single(cfg.PlanCopies);
            Assert.Equal("d62dc7a9-e82f-40eb-8ba9-4b6cf1ab4ed1", cfg.FindPlanCopy(ultimate));

            cfg.PlanCopies[0].Guid = "x & calc"; // config editado à mão
            Assert.Null(cfg.FindPlanCopy(ultimate));

            cfg.RememberPlanCopy(ultimate, null);
            Assert.Empty(cfg.PlanCopies);
        }
    }
}
