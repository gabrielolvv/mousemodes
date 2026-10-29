using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace MouseModes.Tests
{
    public class ExportImportTests
    {
        private static AppConfig Sample()
        {
            var cfg = new AppConfig
            {
                Performance = new MouseProfile { PointerSpeed = 9, PowerPlan = PowerPlanOption.DesempenhoMaximo },
                Daily = new MouseProfile { PointerSpeed = 12, EnhancePointerPrecision = true },
                Hotkey = 2,
                MouseOnlyWhenGameFocused = true,
                CloseAppsOnPerformance = true,
                // Estado deste PC: não pode ir para outro.
                LastMode = Mode.Performance,
                DailyPlanGuid = "381b4222-f694-41f0-9685-ff5bb260df2e",
                NotificationBackup = new NotificationBackup { Active = true, ToastEnabled = 1, GlobalToasts = 1 }
            };
            cfg.Games.AddRange(new[] { "JogoUm", "JogoDois" });
            cfg.AppsToClose.Add("ProgramaInventado");
            cfg.GameProfiles.Add(new GameProfile { Process = "JogoUm", Profile = new MouseProfile { PointerSpeed = 7 } });
            cfg.RememberPlanCopy("e9a42b02-d5df-448d-aa00-03f14749eb61", "232e2748-0959-4183-a9e8-1b5372b4f906");
            return cfg;
        }

        [Fact]
        public void ExportLeavesOutWhatOnlyBelongsToThisPc()
        {
            AppConfig original = Sample();
            AppConfig export = original.ForExport();

            Assert.Null(export.LastMode);
            Assert.Null(export.DailyPlanGuid);
            Assert.Null(export.NotificationBackup);
            Assert.Empty(export.PlanCopies);
            Assert.Equal(new[] { "JogoUm", "JogoDois" }, export.Games.ToArray());
            Assert.Equal(PowerPlanOption.DesempenhoMaximo, export.Performance.PowerPlan);

            // A original não muda.
            Assert.Equal(Mode.Performance, original.LastMode);
            Assert.NotNull(original.FindPlanCopy("e9a42b02-d5df-448d-aa00-03f14749eb61"));
        }

        [Fact]
        public void ImportBringsTheSettingsAndKeepsThisPcState()
        {
            using (var dir = new TempFolder())
            {
                string file = dir.File("MouseModes-config.xml");
                Sample().ForExport().SaveTo(file);

                var here = new AppConfig
                {
                    LastMode = Mode.DiaADia,
                    DailyPlanGuid = "eb7d18b8-9829-4035-954a-b778668d7d03",
                    NotificationBackup = new NotificationBackup { Active = true, ToastEnabled = -1, GlobalToasts = 0 }
                };
                here.RememberPlanCopy("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "d62dc7a9-e82f-40eb-8ba9-4b6cf1ab4ed1");

                here.CopySettingsFrom(AppConfig.Import(file));

                Assert.Equal(new[] { "JogoUm", "JogoDois" }, here.Games.ToArray());
                Assert.Equal(new[] { "ProgramaInventado" }, here.AppsToClose.ToArray());
                Assert.Equal(7, Assert.Single(here.GameProfiles).Profile.PointerSpeed);
                Assert.Equal(9, here.Performance.PointerSpeed);
                Assert.Equal(12, here.Daily.PointerSpeed);
                Assert.Equal(2, here.Hotkey);
                Assert.True(here.MouseOnlyWhenGameFocused);
                Assert.True(here.CloseAppsOnPerformance);

                Assert.Equal(Mode.DiaADia, here.LastMode);
                Assert.Equal("eb7d18b8-9829-4035-954a-b778668d7d03", here.DailyPlanGuid);
                Assert.Equal(-1, here.NotificationBackup.ToastEnabled);
                Assert.Equal("d62dc7a9-e82f-40eb-8ba9-4b6cf1ab4ed1", here.FindPlanCopy("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"));
            }
        }

        [Fact]
        public void ImportRejectsFilesThatAreNotAConfig()
        {
            using (var dir = new TempFolder())
            {
                Assert.ThrowsAny<Exception>(() => AppConfig.Import(dir.Text("qualquer.xml", "<html><body>nada</body></html>")));
                Assert.ThrowsAny<Exception>(() => AppConfig.Import(dir.Text("quebrado.xml", "<AppConfig><Hotkey>1</Hot")));
            }
        }

        [Fact]
        public void ImportFixesAnOutOfRangeHotkey()
        {
            var here = new AppConfig();
            here.CopySettingsFrom(new AppConfig { Hotkey = 99 });
            Assert.Equal(0, here.Hotkey);
        }

        [Fact]
        public void EverySettingIsEitherImportedOrMarkedAsThisPcOnly()
        {
            // Uma configuração nova entra na importação sozinha; esta lista força pensar se ela é só deste PC.
            var expectedMachineOnly = new HashSet<string>
            {
                "LastMode", "DailyPlanGuid", "PlanCopies", "NotificationBackup", "CalmBackup", "LastUpdateCheck", "NotifiedVersion",
                "ResolutionChanged"
            };
            Assert.Equal(expectedMachineOnly, new HashSet<string>(AppConfig.MachineOnly));
            foreach (string name in AppConfig.MachineOnly)
                Assert.NotNull(typeof(AppConfig).GetProperty(name));
        }
    }
}
