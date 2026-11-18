using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace MouseModes
{
    /// <summary>Os ajustes que valiam numa partida: para mostrar, na seguinte, o que mudou entre uma e outra.</summary>
    public class MatchSettings
    {
        public int PointerSpeed { get; set; }
        public bool EnhancePointerPrecision { get; set; }
        public PowerPlanOption PowerPlan { get; set; }
        public bool DisableUsbSelectiveSuspend { get; set; }
        /// <summary>Resolução com o jogo aberto ("1280x960"); null = a nativa.</summary>
        public string Resolution { get; set; }
        /// <summary>Limite de FPS: 0 = sem, FpsCap.Auto = automático.</summary>
        public int FpsCap { get; set; }
        /// <summary>O jogo tinha configurações próprias (Ajustes → Jogos).</summary>
        public bool OwnProfile { get; set; }
        public bool CalmBackground { get; set; }
        public bool BoostGamePriority { get; set; }
        public bool CloseApps { get; set; }
        public bool SilenceNotifications { get; set; }

        /// <summary>O que valia para este jogo no Performance (o perfil próprio dele ou o do Performance).</summary>
        public static MatchSettings From(AppConfig cfg, string game)
        {
            MouseProfile p = cfg.PerformanceFor(game);
            return new MatchSettings
            {
                PointerSpeed = p.PointerSpeed,
                EnhancePointerPrecision = p.EnhancePointerPrecision,
                PowerPlan = p.PowerPlan,
                DisableUsbSelectiveSuspend = p.DisableUsbSelectiveSuspend,
                Resolution = p.Resolution,
                FpsCap = p.FpsCap,
                OwnProfile = !ReferenceEquals(p, cfg.Performance),
                CalmBackground = cfg.CalmBackground,
                BoostGamePriority = cfg.BoostGamePriority,
                CloseApps = cfg.CloseAppsOnPerformance && cfg.AppsToClose.Count > 0,
                SilenceNotifications = cfg.SilenceNotifications
            };
        }
    }

    /// <summary>
    /// Uma partida guardada no histórico: o resumo do Raio-X (sem os quadros um a um) e os ajustes que valiam nela.
    /// Gravada em XML; campos que uma versão antiga não conhecia ficam com o padrão ao ler.
    /// </summary>
    public class MatchRecord
    {
        /// <summary>2: com a partida no tempo (Timeline). Só informativo: ninguém decide nada por ele ao ler.</summary>
        internal const int CurrentSchema = 2;

        public MatchRecord()
        {
            Schema = CurrentSchema;
            Culprits = new List<Culprit>();
        }

        public int Schema { get; set; }
        public string AppVersion { get; set; }
        /// <summary>Quando a partida terminou, em UTC.</summary>
        public DateTime Ended { get; set; }
        public string Game { get; set; }
        public double DurationSeconds { get; set; }
        public List<Culprit> Culprits { get; set; }
        public FrameReport Frames { get; set; }
        public string FramesMissing { get; set; }
        public NetworkReport Network { get; set; }
        public CpuSpeedReport CpuSpeed { get; set; }
        /// <summary>null numa partida gravada sem os ajustes.</summary>
        public MatchSettings Settings { get; set; }
        /// <summary>A partida no tempo, para o gráfico; null numa partida gravada antes dela existir (schema 1).</summary>
        public MatchTimeline Timeline { get; set; }

        /// <summary>De onde foi lido (null se ainda não foi gravado).</summary>
        [XmlIgnore]
        public string FilePath { get; set; }

        public static MatchRecord From(MatchReport report, MatchSettings settings, DateTime endedUtc)
        {
            return new MatchRecord
            {
                AppVersion = typeof(MatchRecord).Assembly.GetName().Version.ToString(3),
                Ended = endedUtc,
                Game = report.Game,
                DurationSeconds = report.Duration.TotalSeconds,
                Culprits = report.Culprits ?? new List<Culprit>(),
                Frames = report.Frames,
                FramesMissing = report.FramesMissing,
                Network = report.Network,
                CpuSpeed = report.CpuSpeed,
                Settings = settings,
                Timeline = report.Timeline
            };
        }

        /// <summary>O Raio-X para mostrar na tela.</summary>
        public MatchReport ToReport()
        {
            return new MatchReport
            {
                Game = Game,
                Duration = TimeSpan.FromSeconds(DurationSeconds),
                Culprits = Culprits ?? new List<Culprit>(),
                Bursts = new List<IoBurst>(),
                CpuBursts = new List<CpuBurst>(),
                Frames = Fix(Frames),
                FramesMissing = FramesMissing,
                Network = Fix(Network),
                CpuSpeed = Fix(CpuSpeed),
                Timeline = Fix(Timeline)
            };
        }

        // Listas que o XML deixa null quando vêm vazias (ou de versão sem elas): a tela percorre todas.
        private static FrameReport Fix(FrameReport f)
        {
            if (f != null && f.Worst == null) f.Worst = new List<Stutter>();
            return f;
        }

        private static NetworkReport Fix(NetworkReport n)
        {
            if (n != null && n.Spikes == null) n.Spikes = new List<NetSpike>();
            return n;
        }

        private static CpuSpeedReport Fix(CpuSpeedReport c)
        {
            if (c != null && c.Worst == null) c.Worst = new List<CpuSlowdown>();
            return c;
        }

        // A série em si continua null quando não existe (partida antiga): só as listas de dentro viram vazias.
        private static MatchTimeline Fix(MatchTimeline t)
        {
            if (t == null) return null;
            if (t.Points == null) t.Points = new List<TimelinePoint>();
            if (t.Stutters == null) t.Stutters = new List<double>();
            if (t.Slowdowns == null) t.Slowdowns = new List<TimelineSpan>();
            return t;
        }

        /// <summary>"16/11 21:40" na hora local.</summary>
        public string When()
        {
            return Ended.ToLocalTime().ToString(L.IsEnglish ? "MM/dd HH:mm" : "dd/MM HH:mm", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// O histórico das partidas: um arquivo por partida em %APPDATA%\MouseModes\history, só neste PC. Guarda as últimas
    /// MaxRecords; as mais antigas são apagadas ao gravar uma nova.
    /// </summary>
    public static class MatchHistory
    {
        internal const int MaxRecords = 100;
        /// <summary>Pasta no lugar da de verdade (testes).</summary>
        internal static string FolderOverride;

        public static string Folder
        {
            get { return FolderOverride ?? Path.Combine(Path.GetDirectoryName(AppConfig.FilePath), "history"); }
        }

        /// <summary>Grava a partida (arquivo novo, de uma vez) e apaga as mais antigas. Devolve o caminho; null se não deu.</summary>
        public static string Save(MatchRecord record)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string path = UniquePath(FileName(record));
                string tmp = path + ".tmp";
                var serializer = new XmlSerializer(typeof(MatchRecord));
                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                {
                    serializer.Serialize(stream, record);
                    stream.Flush(true);
                }
                File.Move(tmp, path);
                record.FilePath = path;
                Prune();
                return path;
            }
            catch (Exception ex)
            {
                Log.Warn(L.T("Histórico das partidas: não deu para gravar (", "Match history: could not save (") + ex.Message + ")");
                return null;
            }
        }

        /// <summary>Todas as partidas guardadas, da mais nova para a mais antiga. Arquivo que não dá para ler fica de fora.</summary>
        public static List<MatchRecord> Load()
        {
            var list = new List<MatchRecord>();
            string[] files;
            try
            {
                if (!Directory.Exists(Folder)) return list;
                files = Directory.GetFiles(Folder, "*.xml");
            }
            catch { return list; }
            var serializer = new XmlSerializer(typeof(MatchRecord));
            foreach (string file in files)
            {
                try
                {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var record = (MatchRecord)serializer.Deserialize(stream);
                        if (record == null || string.IsNullOrEmpty(record.Game)) continue;
                        record.FilePath = file;
                        list.Add(record);
                    }
                }
                catch
                {
                    // Arquivo cortado ou de outro formato: não derruba o histórico inteiro.
                }
            }
            list.Sort((a, b) => b.Ended.CompareTo(a.Ended));
            return list;
        }

        /// <summary>A partida anterior do mesmo jogo, antes de <paramref name="current"/> (null se não há).</summary>
        public static MatchRecord Previous(IList<MatchRecord> newestFirst, MatchRecord current)
        {
            if (current == null || newestFirst == null) return null;
            foreach (MatchRecord r in newestFirst)
            {
                if (ReferenceEquals(r, current) || r.Ended >= current.Ended) continue;
                if (string.Equals(r.Game, current.Game, StringComparison.OrdinalIgnoreCase)) return r;
            }
            return null;
        }

        /// <summary>Apaga o histórico inteiro. Devolve quantas partidas apagou.</summary>
        public static int Clear()
        {
            int count = 0;
            try
            {
                if (!Directory.Exists(Folder)) return 0;
                foreach (string file in Directory.GetFiles(Folder))
                {
                    try
                    {
                        bool record = file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
                        File.Delete(file);
                        if (record) count++;
                    }
                    catch { }
                }
            }
            catch { }
            return count;
        }

        /// <summary>Apaga as mais antigas até sobrarem MaxRecords (pela data no nome do arquivo, que começa com ela).</summary>
        private static void Prune()
        {
            var files = new List<string>(Directory.GetFiles(Folder, "*.xml"));
            if (files.Count <= MaxRecords) return;
            files.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Count - MaxRecords; i++)
            {
                try { File.Delete(files[i]); } catch { }
            }
        }

        /// <summary>"20261116-214012-cs2.xml": a data em UTC primeiro, para a ordem dos nomes ser a ordem das partidas.</summary>
        internal static string FileName(MatchRecord record)
        {
            var name = new StringBuilder();
            foreach (char c in record.Game ?? "")
            {
                if (name.Length >= 40) break;
                name.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            }
            return record.Ended.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                   (name.Length > 0 ? name.ToString() : "game") + ".xml";
        }

        private static string UniquePath(string fileName)
        {
            string path = Path.Combine(Folder, fileName);
            for (int i = 2; File.Exists(path); i++)
                path = Path.Combine(Folder, Path.GetFileNameWithoutExtension(fileName) + "-" + i + ".xml");
            return path;
        }
    }

    /// <summary>Uma linha da comparação; Better = 1 melhorou, -1 piorou, 0 igual ou sem lado (um ajuste que mudou).</summary>
    public struct ComparisonLine
    {
        public string Text;
        public int Better;

        public ComparisonLine(string text, int better)
        {
            Text = text;
            Better = better;
        }
    }

    /// <summary>Antes e depois: a partida comparada com a anterior do mesmo jogo, nos números e nos ajustes.</summary>
    public static class MatchComparison
    {
        /// <summary>Mudança menor que isto (relativa) conta como igual: mapa e servidor já variam assim.</summary>
        internal const double SameRatio = 0.05;
        /// <summary>Travadinhas por hora só ganham lado com pelo menos isto de quadros medidos (s) nas duas partidas.</summary>
        internal const double MinStutterSeconds = 10 * 60;

        /// <summary>Os números que as duas partidas têm em comum, cada um com "antes → agora".</summary>
        public static List<ComparisonLine> Results(MatchRecord now, MatchRecord before)
        {
            var lines = new List<ComparisonLine>();
            if (now == null || before == null) return lines;
            if (now.Frames != null && before.Frames != null)
            {
                ComparisonLine fps = Number(L.T("FPS médio", "Average FPS"), before.Frames.AverageFps, now.Frames.AverageFps, true, "0", "", 2);
                // Com o limite de FPS mudado, o FPS médio mudou de propósito: nem melhor nem pior.
                if (before.Settings != null && now.Settings != null && before.Settings.FpsCap != now.Settings.FpsCap)
                    fps = new ComparisonLine(fps.Text + L.T(" (o limite de FPS mudou)", " (the FPS limit changed)"), 0);
                lines.Add(fps);
                lines.Add(Number("1% low", before.Frames.Low1Fps, now.Frames.Low1Fps, true, "0", "", 2));
                // Pelo tempo de quadros medidos (sem o aquecimento e as pausas), e sem lado com pouco tempo ou poucas travadinhas.
                double beforeSeconds = FrameSeconds(before.Frames), nowSeconds = FrameSeconds(now.Frames);
                ComparisonLine stutters = Number(L.T("Travadinhas por hora", "Stutters per hour"), PerHour(before.Frames.StutterCount, beforeSeconds),
                                                 PerHour(now.Frames.StutterCount, nowSeconds), false, "0.#", "", 1);
                if (beforeSeconds < MinStutterSeconds || nowSeconds < MinStutterSeconds ||
                    (before.Frames.StutterCount <= 2 && now.Frames.StutterCount <= 2))
                    stutters.Better = 0;
                lines.Add(stutters);
            }
            if (now.Network != null && before.Network != null)
            {
                lines.Add(Number("Ping", before.Network.InternetMedian, now.Network.InternetMedian, false, "0", " ms", 3));
                ComparisonLine loss = Number(L.T("Perda", "Loss"), before.Network.LossPercent, now.Network.LossPercent, false, "0.#", "%", 0.5);
                // Abaixo de 1% a rede é estável para o próprio Raio-X: a perda não pinta a linha.
                if (before.Network.LossPercent < 1 && now.Network.LossPercent < 1) loss.Better = 0;
                lines.Add(loss);
            }
            if (now.CpuSpeed != null && before.CpuSpeed != null)
                lines.Add(Number(L.T("Processador freado", "CPU throttled"), LimitedPercent(before.CpuSpeed), LimitedPercent(now.CpuSpeed), false, "0",
                                 L.T("% do tempo", "% of the time"), 2));
            lines.Add(Number(L.T("Programas pesando em segundo plano", "Programs weighing in the background"),
                             Count(before.Culprits), Count(now.Culprits), false, "0", "", 2));
            return lines;
        }

        /// <summary>Os ajustes que mudaram de uma partida para a outra ("Segundo plano calmo: desligado → ligado").</summary>
        public static List<string> SettingChanges(MatchSettings now, MatchSettings before)
        {
            var lines = new List<string>();
            if (now == null || before == null) return lines;
            Toggle(lines, L.T("Segundo plano calmo", "Calm background"), before.CalmBackground, now.CalmBackground);
            Toggle(lines, L.T("Prioridade alta do jogo", "High game priority"), before.BoostGamePriority, now.BoostGamePriority);
            Toggle(lines, L.T("Fechar programas ao jogar", "Close programs when gaming"), before.CloseApps, now.CloseApps);
            Toggle(lines, L.T("Silenciar notificações", "Silence notifications"), before.SilenceNotifications, now.SilenceNotifications);
            Toggle(lines, L.T("Configurações próprias do jogo", "Game's own settings"), before.OwnProfile, now.OwnProfile);
            if (before.FpsCap != now.FpsCap)
                lines.Add(L.T("Limite de FPS: ", "FPS limit: ") + Fps(before.FpsCap) + " → " + Fps(now.FpsCap));
            if (before.PowerPlan != now.PowerPlan)
                lines.Add(L.T("Plano de energia: ", "Power plan: ") + Plan(before.PowerPlan) + " → " + Plan(now.PowerPlan));
            if (!string.Equals(before.Resolution ?? "", now.Resolution ?? "", StringComparison.OrdinalIgnoreCase))
                lines.Add(L.T("Resolução: ", "Resolution: ") + Resolution(before.Resolution) + " → " + Resolution(now.Resolution));
            if (before.PointerSpeed != now.PointerSpeed)
                lines.Add(L.T("Velocidade do ponteiro: ", "Pointer speed: ") + before.PointerSpeed + " → " + now.PointerSpeed);
            Toggle(lines, L.T("Aceleração do Windows", "Windows acceleration"), before.EnhancePointerPrecision, now.EnhancePointerPrecision);
            Toggle(lines, L.T("Suspensão seletiva do USB desligada", "USB selective suspend off"), before.DisableUsbSelectiveSuspend, now.DisableUsbSelectiveSuspend);
            return lines;
        }

        /// <summary>Quanto tempo de jogo os quadros cobriram (s): quadros ÷ FPS médio, sem o aquecimento nem as pausas.</summary>
        internal static double FrameSeconds(FrameReport frames)
        {
            return frames != null && frames.AverageFps > 0 ? frames.Frames / frames.AverageFps : 0;
        }

        internal static double PerHour(int count, double seconds)
        {
            return seconds > 0 ? count * 3600.0 / seconds : 0;
        }

        private static double LimitedPercent(CpuSpeedReport cpu)
        {
            return cpu.MeasuredSeconds > 0 ? 100.0 * cpu.LimitedSeconds / cpu.MeasuredSeconds : 0;
        }

        private static int Count(List<Culprit> culprits)
        {
            return culprits != null ? culprits.Count : 0;
        }

        /// <summary>
        /// "FPS médio: 144 → 158 (+10%)"; o lado vem de <paramref name="higherIsBetter"/>. Fica igual (sem lado nem %)
        /// quando a diferença é menor que <paramref name="minDelta"/> ou que SameRatio, ou quando os dois números
        /// aparecem iguais. A porcentagem sai dos números como aparecem, para nunca ficar "0 → 1 (+50%)".
        /// </summary>
        internal static ComparisonLine Number(string label, double before, double now, bool higherIsBetter, string format, string unit,
                                              double minDelta = 0)
        {
            CultureInfo culture = L.IsEnglish ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("pt-BR");
            string b = before.ToString(format, culture), n = now.ToString(format, culture);
            string text = label + ": " + b + unit + " → " + n + unit;
            double shownBefore = double.Parse(b, culture), shownNow = double.Parse(n, culture);
            double scale = Math.Max(Math.Abs(shownBefore), Math.Abs(shownNow));
            if (b == n || scale == 0 || Math.Abs(shownNow - shownBefore) < minDelta || Math.Abs(shownNow - shownBefore) / scale < SameRatio)
                return new ComparisonLine(text, 0);
            if (shownBefore > 0)
            {
                double change = (shownNow - shownBefore) / shownBefore * 100;
                text += " (" + (change > 0 ? "+" : "") + change.ToString("0", culture) + "%)";
            }
            bool up = shownNow > shownBefore;
            return new ComparisonLine(text, up == higherIsBetter ? 1 : -1);
        }

        private static void Toggle(List<string> lines, string label, bool before, bool now)
        {
            if (before == now) return;
            lines.Add(label + ": " + OnOff(before) + " → " + OnOff(now));
        }

        private static string OnOff(bool on)
        {
            return on ? L.T("ligado", "on") : L.T("desligado", "off");
        }

        private static string Fps(int cap)
        {
            if (cap == FpsCap.Auto) return L.T("automático", "automatic");
            return cap <= 0 ? L.T("sem", "none") : cap.ToString(CultureInfo.InvariantCulture);
        }

        private static string Plan(PowerPlanOption plan)
        {
            switch (plan)
            {
                case PowerPlanOption.Equilibrado: return L.T("Equilibrado", "Balanced");
                case PowerPlanOption.AltoDesempenho: return L.T("Alto desempenho", "High performance");
                case PowerPlanOption.DesempenhoMaximo: return L.T("Desempenho máximo", "Ultimate performance");
                default: return L.T("manter o do Windows", "keep Windows' plan");
            }
        }

        private static string Resolution(string resolution)
        {
            return string.IsNullOrEmpty(resolution) ? L.T("nativa", "native") : GameResolution.Format(resolution);
        }
    }
}
