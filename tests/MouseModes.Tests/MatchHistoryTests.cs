using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>
    /// Histórico do Raio-X numa pasta temporária: partidas inventadas, gravadas e lidas de volta. Nada aqui lê nem grava o
    /// histórico de verdade (%APPDATA%).
    /// </summary>
    public class MatchHistoryTests : IDisposable
    {
        private readonly TempFolder temp = new TempFolder();

        public MatchHistoryTests()
        {
            MatchHistory.FolderOverride = temp.File("history");
        }

        public void Dispose()
        {
            MatchHistory.FolderOverride = null;
            temp.Dispose();
        }

        private static MatchSettings Settings(bool calm = false, int fpsCap = 0)
        {
            return new MatchSettings { PointerSpeed = 10, PowerPlan = PowerPlanOption.AltoDesempenho, FpsCap = fpsCap, CalmBackground = calm };
        }

        private static MatchRecord Match(string game, DateTime ended, double minutes = 30, double fps = 144, int stutters = 6,
                                         int ping = 20, MatchSettings settings = null)
        {
            var report = new MatchReport
            {
                Game = game,
                Duration = TimeSpan.FromMinutes(minutes),
                Culprits = new List<Culprit> { new Culprit { Name = "chrome", AveragePercent = 4, PeakPercent = 30, IoBytes = 1 } },
                Frames = new FrameReport
                {
                    Frames = (int)(fps * minutes * 60), AverageFps = fps, Low1Fps = fps * 0.6, Low01Fps = fps * 0.4, MedianMs = 1000 / fps, StutterCount = stutters,
                    Worst = new List<Stutter> { new Stutter { At = 61, Ms = 85, Cause = "nessa hora o chrome usava 30% do processador" } },
                    FromDriver = true
                },
                Network = new NetworkReport
                {
                    Samples = 1800, InternetMedian = ping, Jitter = 2.5, LossPercent = 0.5, GatewayMedian = 2, Verdict = NetVerdict.Stable,
                    SpikeCount = 1, Spikes = new List<NetSpike> { new NetSpike { At = 120, Seconds = 3, PeakMs = 140, Busy = "steam", BusyBytes = 5 } }
                },
                CpuSpeed = new CpuSpeedReport
                {
                    AveragePerformance = 97, MeasuredSeconds = 1800, LimitedSeconds = 90, LowestLimit = 70,
                    Worst = new List<CpuSlowdown> { new CpuSlowdown { At = 300, Seconds = 90, LowestLimit = 70, OnBattery = false } }
                }
            };
            return MatchRecord.From(report, settings ?? Settings(), ended);
        }

        [Fact]
        public void SavedMatchComesBackWithEverythingTheReportShows()
        {
            var ended = new DateTime(2026, 11, 16, 21, 40, 12, DateTimeKind.Utc);
            MatchRecord saved = Match("cs2", ended, settings: Settings(calm: true, fpsCap: 144));
            string path = MatchHistory.Save(saved);

            Assert.Equal(Path.Combine(MatchHistory.Folder, "20261116-214012-cs2.xml"), path);
            Assert.False(File.Exists(path + ".tmp"));
            MatchRecord back = Assert.Single(MatchHistory.Load());
            Assert.Equal(path, back.FilePath);
            Assert.Equal(ended, back.Ended.ToUniversalTime());
            Assert.Equal(MatchRecord.CurrentSchema, back.Schema);

            MatchReport report = back.ToReport();
            Assert.Equal("cs2", report.Game);
            Assert.Equal(TimeSpan.FromMinutes(30), report.Duration);
            Assert.Equal("chrome", report.Culprits[0].Name);
            Assert.Equal(144, report.Frames.AverageFps);
            Assert.True(report.Frames.FromDriver);
            Assert.Equal(85, report.Frames.Worst[0].Ms);
            Assert.Equal(20, report.Network.InternetMedian);
            Assert.Equal("steam", report.Network.Spikes[0].Busy);
            Assert.Equal(90, report.CpuSpeed.Worst[0].Seconds);
            Assert.NotNull(report.Bursts);
            Assert.NotNull(report.CpuBursts);
            Assert.True(back.Settings.CalmBackground);
            Assert.Equal(144, back.Settings.FpsCap);
            Assert.Equal(PowerPlanOption.AltoDesempenho, back.Settings.PowerPlan);
        }

        [Fact]
        public void ThrottlingOnBatteryStillExplainsItselfWhenReadFromTheHistory()
        {
            MatchRecord saved = Match("cs2", new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc));
            saved.CpuSpeed.LimitedSeconds = 90;
            saved.CpuSpeed.All = new List<CpuSlowdown> { new CpuSlowdown { At = 300, Seconds = 90, LowestLimit = 70, OnBattery = true } };
            string live = saved.CpuSpeed.VerdictText();
            MatchHistory.Save(saved);

            CpuSpeedReport back = Assert.Single(MatchHistory.Load()).ToReport().CpuSpeed;

            Assert.Null(back.All); // os trechos todos não vão para o histórico
            Assert.Equal(90, back.BatterySeconds);
            Assert.Equal(live, back.VerdictText());
        }

        [Fact]
        public void KeepsOnlyTheNewestMatchesAndSkipsBrokenFiles()
        {
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Directory.CreateDirectory(MatchHistory.Folder);
            File.WriteAllText(Path.Combine(MatchHistory.Folder, "99999999-999999-cortado.xml"), "<MatchRecord><Game>cs2"); // cortado no meio
            for (int i = 0; i < MatchHistory.MaxRecords + 3; i++) MatchHistory.Save(Match("cs2", start.AddHours(i)));

            List<MatchRecord> all = MatchHistory.Load();
            Assert.Equal(MatchHistory.MaxRecords - 1, all.Count); // o cortado conta no limite, mas não é lido
            Assert.Equal(start.AddHours(MatchHistory.MaxRecords + 2), all[0].Ended.ToUniversalTime()); // a mais nova primeiro
            Assert.DoesNotContain(all, r => r.Ended.ToUniversalTime() < start.AddHours(4)); // as mais antigas foram apagadas
        }

        [Fact]
        public void TwoMatchesInTheSameSecondGetTheirOwnFiles()
        {
            var ended = new DateTime(2026, 11, 16, 21, 40, 12, DateTimeKind.Utc);
            string a = MatchHistory.Save(Match("Doom: The Dark Ages", ended));
            string b = MatchHistory.Save(Match("Doom: The Dark Ages", ended));

            Assert.Equal("20261116-214012-Doom__The_Dark_Ages.xml", Path.GetFileName(a));
            Assert.Equal("20261116-214012-Doom__The_Dark_Ages-2.xml", Path.GetFileName(b));
            Assert.Equal(2, MatchHistory.Load().Count);
        }

        [Fact]
        public void PreviousIsTheLastMatchOfTheSameGameBeforeThisOne()
        {
            var t = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
            MatchRecord oldCs = Match("cs2", t);
            MatchRecord valorant = Match("VALORANT-Win64-Shipping", t.AddHours(1));
            MatchRecord lastCs = Match("CS2", t.AddHours(2));
            MatchRecord now = Match("cs2", t.AddHours(3));
            var newestFirst = new List<MatchRecord> { now, lastCs, valorant, oldCs };

            Assert.Same(lastCs, MatchHistory.Previous(newestFirst, now));
            Assert.Same(oldCs, MatchHistory.Previous(newestFirst, lastCs));
            Assert.Null(MatchHistory.Previous(newestFirst, oldCs));
            Assert.Null(MatchHistory.Previous(newestFirst, valorant));
            // A partida que acabou de terminar, ainda fora da lista (histórico desligado), também acha a anterior.
            Assert.Same(lastCs, MatchHistory.Previous(new List<MatchRecord> { lastCs, oldCs }, now));
        }

        [Fact]
        public void ClearDeletesEveryMatch()
        {
            var t = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
            MatchHistory.Save(Match("cs2", t));
            MatchHistory.Save(Match("cs2", t.AddHours(1)));

            Assert.Equal(2, MatchHistory.Clear());
            Assert.Empty(MatchHistory.Load());
            Assert.Equal(0, MatchHistory.Clear());
        }

        [Fact]
        public void ComparisonSaysWhatGotBetterWorseOrStayedTheSame()
        {
            var t = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
            MatchRecord before = Match("cs2", t, minutes: 60, fps: 120, stutters: 12, ping: 20);
            MatchRecord now = Match("cs2", t.AddHours(2), minutes: 30, fps: 150, stutters: 2, ping: 21);

            List<ComparisonLine> lines = MatchComparison.Results(now, before);

            ComparisonLine fps = lines[0];
            Assert.StartsWith("120 → 150 (+25%)", fps.Text.Substring(fps.Text.IndexOf(':') + 2));
            Assert.Equal(1, fps.Better);
            ComparisonLine stutters = lines[2]; // por hora: 12 em 1 h contra 2 em 30 min (4 por hora)
            Assert.Contains("12 → 4 (-67%)", stutters.Text);
            Assert.Equal(1, stutters.Better);
            ComparisonLine ping = lines[3];
            Assert.Contains("20 ms → 21 ms", ping.Text);
            Assert.Equal(0, ping.Better); // 5% de diferença é variação de servidor, não mudança
            Assert.DoesNotContain("%)", ping.Text);

            ComparisonLine worse = MatchComparison.Number("Ping", 20, 40, false, "0", " ms");
            Assert.Equal(-1, worse.Better);
            Assert.Equal(0, MatchComparison.Number("x", 0, 0, true, "0", "").Better);
        }

        [Fact]
        public void ChangingTheFpsLimitMakesTheAverageFpsNeitherBetterNorWorse()
        {
            var t = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
            MatchRecord before = Match("cs2", t, fps: 280, settings: Settings(fpsCap: 0));
            MatchRecord now = Match("cs2", t.AddHours(1), fps: 141, settings: Settings(fpsCap: FpsCap.Auto));

            ComparisonLine fps = MatchComparison.Results(now, before)[0];

            Assert.Equal(0, fps.Better);
            Assert.EndsWith(L.T(" (o limite de FPS mudou)", " (the FPS limit changed)"), fps.Text);
            // Com o mesmo limite, a queda volta a contar.
            MatchRecord sameCap = Match("cs2", t.AddHours(2), fps: 141, settings: Settings(fpsCap: 0));
            Assert.Equal(-1, MatchComparison.Results(sameCap, before)[0].Better);
        }

        [Fact]
        public void SmallDifferencesAreNoiseNotChange()
        {
            var t = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
            MatchRecord before = Match("cs2", t, ping: 8);
            MatchRecord now = Match("cs2", t.AddHours(1), ping: 9);
            before.Network.LossPercent = 0;
            now.Network.LossPercent = 0.06; // 1 ping perdido em 1800

            List<ComparisonLine> lines = MatchComparison.Results(now, before);

            Assert.Equal(0, lines[3].Better); // ping 8 → 9 ms
            Assert.Equal(0, lines[4].Better); // perda 0 → 0,1%: a rede segue estável para o Raio-X
        }

        [Fact]
        public void StuttersPerHourNeedEnoughFramesOnBothSides()
        {
            var t = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
            MatchRecord before = Match("cs2", t, minutes: 30, stutters: 6);   // 12 por hora
            MatchRecord shortOne = Match("cs2", t.AddHours(1), minutes: 2, stutters: 1); // 30 por hora, de um só evento

            ComparisonLine stutters = MatchComparison.Results(shortOne, before)[2];

            Assert.Contains("12 → 30", stutters.Text);
            Assert.Equal(0, stutters.Better);
            // Por hora de quadros medidos: 10 min de quadros numa partida de 60 min (o resto em menu ou pausa).
            MatchRecord paused = Match("cs2", t.AddHours(2), minutes: 60, stutters: 5);
            paused.Frames.Frames = (int)(paused.Frames.AverageFps * 10 * 60);
            Assert.Contains("12 → 30", MatchComparison.Results(paused, before)[2].Text);
        }

        [Fact]
        public void PercentageMatchesTheNumbersAsShown()
        {
            ComparisonLine fromZero = MatchComparison.Number("x", 0.4, 0.6, false, "0", "");
            Assert.Equal("x: 0 → 1", fromZero.Text); // sem "(+50%)" ao lado de 0 → 1
            ComparisonLine rounded = MatchComparison.Number("x", 10.4, 12.6, true, "0", "");
            Assert.Equal("x: 10 → 13 (+30%)", rounded.Text);
            Assert.Equal(0, MatchComparison.Number("x", 100, 101.5, true, "0", "", 2).Better);
        }

        [Fact]
        public void ComparisonOnlyUsesWhatBothMatchesMeasured()
        {
            var t = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
            MatchRecord before = Match("cs2", t);
            MatchRecord now = Match("cs2", t.AddHours(1));
            now.Frames = null;   // sem a permissão nesta
            before.Network = null; // rede desligada na anterior

            List<ComparisonLine> lines = MatchComparison.Results(now, before);

            Assert.Equal(2, lines.Count); // processador freado e programas em segundo plano
            Assert.Empty(MatchComparison.Results(now, null));
        }

        [Fact]
        public void SettingChangesListOnlyWhatChanged()
        {
            MatchSettings before = Settings(calm: false, fpsCap: 0);
            MatchSettings now = Settings(calm: true, fpsCap: FpsCap.Auto);
            now.PowerPlan = PowerPlanOption.DesempenhoMaximo;
            now.Resolution = "1280x960";

            List<string> changes = MatchComparison.SettingChanges(now, before);

            Assert.Equal(4, changes.Count);
            Assert.Contains(changes, c => c.EndsWith(" → 1280×960"));
            Assert.Empty(MatchComparison.SettingChanges(Settings(), Settings()));
            Assert.Empty(MatchComparison.SettingChanges(now, null));
        }

        [Fact]
        public void SettingsComeFromTheGamesOwnProfileWhenItHasOne()
        {
            var cfg = new AppConfig { CalmBackground = true };
            cfg.Performance = new MouseProfile { PointerSpeed = 10, FpsCap = 0 };
            cfg.GameProfiles.Add(new GameProfile { Process = "cs2", Profile = new MouseProfile { PointerSpeed = 6, FpsCap = 240 } });

            MatchSettings own = MatchSettings.From(cfg, "cs2");
            MatchSettings shared = MatchSettings.From(cfg, "r5apex");

            Assert.True(own.OwnProfile);
            Assert.Equal(240, own.FpsCap);
            Assert.Equal(6, own.PointerSpeed);
            Assert.False(shared.OwnProfile);
            Assert.Equal(10, shared.PointerSpeed);
            Assert.True(shared.CalmBackground);
        }

        [Fact]
        public void HistoryIsOnForOldConfigsWithoutTheSetting()
        {
            string path = temp.Text("config.xml", "<?xml version=\"1.0\"?><AppConfig><MeasureNetwork>false</MeasureNetwork></AppConfig>");
            AppConfig cfg = AppConfig.LoadOrCreate(path);
            Assert.True(cfg.KeepMatchHistory);
            Assert.False(cfg.MeasureNetwork);
        }

        [Fact]
        public void HistoryWindowListsMatchesAndDeletesThemAfterAsking()
        {
            var t = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
            MatchHistory.Save(Match("cs2", t));
            MatchRecord withoutFrames = Match("r5apex", t.AddHours(1));
            withoutFrames.Frames = null;
            MatchHistory.Save(withoutFrames);
            bool cleared = false;

            using (var form = new MatchHistoryForm(new AppConfig(), null, () => cleared = true))
            {
                var list = (ListBox)typeof(MatchHistoryForm).GetField("list", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(form);
                Assert.Equal(2, list.Items.Count);
                var newest = (MatchRecord)list.Items[0];
                Assert.Equal("r5apex", newest.Game);
                Assert.Null(MatchHistoryForm.ItemTag(newest));
                Assert.Equal("144 FPS", MatchHistoryForm.ItemTag((MatchRecord)list.Items[1]));
                Assert.Contains("cs2", MatchHistoryForm.ItemText((MatchRecord)list.Items[1]));

                var clear = typeof(MatchHistoryForm).GetMethod("ClearAll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                form.ConfirmClear = question => false; // cancelou
                clear.Invoke(form, null);
                Assert.Equal(2, MatchHistory.Load().Count);
                Assert.False(cleared);

                form.ConfirmClear = question => true;
                clear.Invoke(form, null);
                Assert.Empty(MatchHistory.Load());
                Assert.Empty(list.Items);
                Assert.True(cleared);
            }
        }

        [Fact]
        public void HistoryStatusLineFitsItsTextAndOnlyGrowsWhileOpen()
        {
            var t = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
            MatchHistory.Save(Match("cs2", t));
            MatchHistory.Save(Match("cs2", t.AddHours(1)));
            var cfg = new AppConfig { KeepMatchHistory = true };
            const System.Reflection.BindingFlags field = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            using (var form = new MatchHistoryForm(cfg, null, null))
            {
                var status = (Label)typeof(MatchHistoryForm).GetField("status", field).GetValue(form);
                var open = (Control)typeof(MatchHistoryForm).GetField("open", field).GetValue(form);
                var clear = (Control)typeof(MatchHistoryForm).GetField("clear", field).GetValue(form);
                float k = status.Width / 440f;
                Func<string, int> needed = text => (int)Math.Ceiling(Theme.TextHeight(text, status.Font, 440) * k);
                List<MatchRecord> two = MatchHistory.Load();

                // "2 partidas guardadas.": a linha tem a altura do texto (não a das 3 linhas do caso mais longo).
                Assert.Equal(needed(status.Text), status.Height);
                int gap = open.Top - status.Bottom, oneLine = status.Height, formHeight = form.Height;
                Assert.True(gap > 0);

                // Vazio com o histórico desligado (o texto mais longo): cresce e empurra os botões e a janela juntos.
                cfg.KeepMatchHistory = false;
                form.Fill(new List<MatchRecord>());
                Assert.True(status.Height >= needed(status.Text));
                Assert.True(status.Height > oneLine);
                Assert.Equal(gap, open.Top - status.Bottom);
                Assert.Equal(open.Top, clear.Top);
                Assert.False(open.Enabled); // sem partida, nada para abrir
                Assert.Equal(formHeight + status.Height - oneLine, form.Height);

                // De volta a uma linha: com a janela aberta não encolhe (nada de sobe-e-desce).
                int grown = status.Height, grownForm = form.Height;
                cfg.KeepMatchHistory = true;
                form.Fill(two);
                Assert.Equal(grown, status.Height);
                Assert.Equal(grownForm, form.Height);
                Assert.Equal(gap, open.Top - status.Bottom);
            }
        }

        [Fact]
        public void ReportWindowShowsTheComparisonWithThePreviousMatch()
        {
            var t = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
            MatchRecord before = Match("cs2", t, fps: 120);
            MatchRecord now = Match("cs2", t.AddHours(2), fps: 150, settings: Settings(calm: true));

            using (var form = new MatchReportForm(now.ToReport(), new AppConfig(), null, now, before))
            {
                string all = Texts(form);
                Assert.Contains("120 → 150", all);
                Assert.Contains(MatchComparison.SettingChanges(now.Settings, before.Settings)[0], all);
            }
            using (var form = new MatchReportForm(now.ToReport(), new AppConfig(), null))
                Assert.DoesNotContain("120 → 150", Texts(form)); // sem o histórico, sem comparação
        }

        [Fact]
        public void FirstMatchNoteOnlyForAMatchThatIsReallySaved()
        {
            string note = L.T("Primeira partida deste jogo no histórico", "First match of this game in the history");
            MatchRecord unsaved = Match("cs2", new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc));
            using (var form = new MatchReportForm(unsaved.ToReport(), new AppConfig(), null, unsaved, null))
                Assert.DoesNotContain(note, Texts(form)); // gravação que falhou, ou histórico apagado

            MatchHistory.Save(unsaved);
            using (var form = new MatchReportForm(unsaved.ToReport(), new AppConfig(), null, unsaved, null))
                Assert.Contains(note, Texts(form));
        }

        [Fact]
        public void ReportTitleKeepsLongGameNamesAndTheDateGoesBelow()
        {
            MatchRecord record = Match("VALORANT-Win64-Shipping", new DateTime(2026, 11, 16, 21, 40, 0, DateTimeKind.Utc), minutes: 65);
            using (var form = new MatchReportForm(record.ToReport(), new AppConfig(), null, record, null))
            {
                foreach (Control c in form.Controls)
                {
                    if (!(c is Label) || string.IsNullOrEmpty(c.Text)) continue;
                    Size needed = TextRenderer.MeasureText(c.Text, c.Font, new Size(c.Width, int.MaxValue), TextFormatFlags.WordBreak);
                    Assert.True(needed.Height <= c.Height + 2, "cortado: " + c.Text);
                }
                Assert.Contains(record.When(), Texts(form));
            }
        }

        private static string Texts(Control root)
        {
            var text = new System.Text.StringBuilder();
            foreach (Control c in root.Controls)
            {
                text.Append(c.Text).Append('\n');
                text.Append(Texts(c));
            }
            return text.ToString();
        }
    }
}
