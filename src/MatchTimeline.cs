using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace MouseModes
{
    /// <summary>
    /// Um balde da partida (alguns segundos): FPS, 1% low e ping. Atributos curtos porque são centenas por partida e o
    /// histórico lê todos os arquivos ao abrir.
    /// </summary>
    public class TimelinePoint
    {
        /// <summary>Início do balde, em s desde o começo da partida.</summary>
        [XmlAttribute("t")] public double At;
        /// <summary>FPS médio do balde; NaN = sem quadros (aquecimento, pausa, sem permissão).</summary>
        [XmlAttribute("f")] public float Fps = float.NaN;
        /// <summary>1% low do balde (média do 1% de quadros mais lentos); NaN = sem quadros.</summary>
        [XmlAttribute("l")] public float Low1Fps = float.NaN;
        /// <summary>Ping mediano à internet no balde, em ms; -1 = sem medida ou tudo perdido.</summary>
        [XmlAttribute("p")] public int Ping = NoPing;
        /// <summary>Pings perdidos no balde.</summary>
        [XmlAttribute("x")] public int Lost;

        public const int NoPing = -1;

        public bool HasFps { get { return !float.IsNaN(Fps); } }
        public bool HasPing { get { return Ping >= 0; } }
    }

    /// <summary>Um trecho da partida (processador freado): começo e duração, em s.</summary>
    public class TimelineSpan
    {
        [XmlAttribute("t")] public double At;
        [XmlAttribute("s")] public double Seconds;
    }

    /// <summary>
    /// A partida no tempo, para o gráfico do Raio-X: FPS, 1% low e ping em baldes de tamanho fixo, mais o instante de
    /// cada travadinha e os trechos de processador freado. Gravada no histórico; partida antiga não tem (null).
    /// </summary>
    public class MatchTimeline
    {
        /// <summary>Balde mínimo, e todo balde é múltiplo dele.</summary>
        internal const double MinBucketSeconds = 5;
        /// <summary>Mais baldes que isso não cabem no gráfico (408 px) nem valem o tamanho do arquivo.</summary>
        internal const int MaxPoints = 300;
        /// <summary>Travadinhas marcadas no máximo; passando disso, ficam as mais fortes.</summary>
        internal const int MaxStutters = 300;
        internal const int MaxSlowdowns = 300;
        /// <summary>Um balde com menos de 1 s de quadros (o jogo voltando de uma pausa) não dá um FPS confiável.</summary>
        private const double MinFrameMsPerBucket = 1000;

        public MatchTimeline()
        {
            Points = new List<TimelinePoint>();
            Stutters = new List<double>();
            Slowdowns = new List<TimelineSpan>();
        }

        public double BucketSeconds { get; set; }
        [XmlArrayItem("P")]
        public List<TimelinePoint> Points { get; set; }
        /// <summary>Instante de cada travadinha, em s, em ordem de tempo.</summary>
        [XmlArrayItem("At")]
        public List<double> Stutters { get; set; }
        /// <summary>Trechos de processador freado, em ordem de tempo.</summary>
        [XmlArrayItem("Span")]
        public List<TimelineSpan> Slowdowns { get; set; }

        /// <summary>Tem algum balde com FPS ou com ping (o que o gráfico precisa para ter o que desenhar).</summary>
        public bool HasData
        {
            get
            {
                if (Points == null) return false;
                foreach (TimelinePoint p in Points)
                    if (p.HasFps || p.HasPing) return true;
                return false;
            }
        }

        /// <summary>Balde de 5 s, ou o múltiplo de 5 s que faz a partida caber em MaxPoints baldes.</summary>
        internal static double BucketFor(double seconds)
        {
            double perPoint = seconds / MaxPoints;
            return Math.Max(MinBucketSeconds, Math.Ceiling(perPoint / MinBucketSeconds) * MinBucketSeconds);
        }

        /// <summary>
        /// Monta a série a partir do que a partida mediu. Qualquer entrada pode ser null. Os quadros passam pelo mesmo
        /// filtro do FrameStats.Analyze (aquecimento e pausas ficam de fora, viram lacuna). null se não há FPS nem ping.
        /// </summary>
        /// <param name="frames">Os quadros do jogo.</param>
        /// <param name="net">Os pings de cada segundo.</param>
        /// <param name="stutters">Todas as travadinhas (FrameReport.All).</param>
        /// <param name="slowdowns">Todos os trechos de freio (CpuSpeedReport.All).</param>
        /// <param name="durationSeconds">Duração da partida.</param>
        public static MatchTimeline Build(IList<FrameSample> frames, IList<NetSample> net, IList<Stutter> stutters,
                                          IList<CpuSlowdown> slowdowns, double durationSeconds)
        {
            // Os quadros e o ping passam uns segundos do último tick (que dá a duração): o fim é o que vier por último.
            double end = Math.Max(0, durationSeconds);
            if (frames != null)
                foreach (FrameSample f in frames)
                    if (Used(f) && f.At > end) end = f.At;
            if (net != null)
                foreach (NetSample n in net)
                    if (n.Internet != NetSample.NotMeasured && n.At > end) end = n.At;
            if (end <= 0) return null;

            double bucket = BucketFor(end);
            int count = Math.Max(1, (int)Math.Ceiling(end / bucket));

            // Os quadros de todos os baldes num vetor só, um segmento por balde (contados antes): uma partida longa tem
            // milhões de quadros, e uma lista por balde deixaria dezenas de MB de lixo para trás.
            var start = new int[count + 1];
            if (frames != null)
                foreach (FrameSample f in frames)
                {
                    if (!Used(f)) continue;
                    int i = Index(f.At, bucket, count);
                    if (i >= 0) start[i + 1]++;
                }
            for (int i = 0; i < count; i++) start[i + 1] += start[i];
            var ms = new float[start[count]];
            if (frames != null)
            {
                var fill = new int[count];
                Array.Copy(start, fill, count);
                foreach (FrameSample f in frames)
                {
                    if (!Used(f)) continue;
                    int i = Index(f.At, bucket, count);
                    if (i >= 0) ms[fill[i]++] = f.Ms;
                }
            }

            var pings = new List<int>[count];
            var lost = new int[count];
            if (net != null)
                foreach (NetSample n in net)
                {
                    int i = Index(n.At, bucket, count);
                    if (i < 0) continue;
                    if (n.Internet == NetSample.Lost) lost[i]++;
                    else if (n.Internet >= 0)
                    {
                        if (pings[i] == null) pings[i] = new List<int>();
                        pings[i].Add(n.Internet);
                    }
                }

            var timeline = new MatchTimeline { BucketSeconds = bucket };
            bool any = false, anyPing = false;
            for (int i = 0; i < count; i++)
            {
                var point = new TimelinePoint { At = i * bucket, Lost = lost[i] };
                int first = start[i], n = start[i + 1] - first;
                if (n > 0)
                {
                    double total = 0;
                    for (int j = first; j < first + n; j++) total += ms[j];
                    if (total >= MinFrameMsPerBucket)
                    {
                        point.Fps = Round(n / (total / 1000.0));
                        point.Low1Fps = Round(LowFps(ms, first, n, 0.01));
                    }
                }
                if (pings[i] != null)
                {
                    pings[i].Sort();
                    point.Ping = pings[i][pings[i].Count / 2];
                    anyPing = true;
                }
                if (point.HasFps || point.HasPing) any = true;
                timeline.Points.Add(point);
            }
            if (!any) return null;
            // Nenhum ping respondeu (bloqueado ou sem internet): a rede não foi medida, então também não há "perdas".
            if (!anyPing)
                foreach (TimelinePoint p in timeline.Points) p.Lost = 0;

            if (stutters != null)
            {
                var kept = new List<Stutter>();
                foreach (Stutter s in stutters)
                    if (s != null && s.At >= 0 && s.At <= end) kept.Add(s);
                if (kept.Count > MaxStutters)
                {
                    kept.Sort((a, b) => b.Ms.CompareTo(a.Ms));
                    kept.RemoveRange(MaxStutters, kept.Count - MaxStutters);
                }
                kept.Sort((a, b) => a.At.CompareTo(b.At));
                foreach (Stutter s in kept) timeline.Stutters.Add(Math.Round(s.At, 1));
            }

            if (slowdowns != null)
            {
                var kept = new List<CpuSlowdown>();
                foreach (CpuSlowdown s in slowdowns)
                    if (s != null && s.Seconds > 0 && s.At <= end) kept.Add(s);
                if (kept.Count > MaxSlowdowns)
                {
                    kept.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
                    kept.RemoveRange(MaxSlowdowns, kept.Count - MaxSlowdowns);
                }
                kept.Sort((a, b) => a.At.CompareTo(b.At));
                foreach (CpuSlowdown s in kept)
                {
                    double at = Math.Max(0, s.At);
                    timeline.Slowdowns.Add(new TimelineSpan { At = Math.Round(at, 1), Seconds = Math.Round(Math.Min(s.Seconds, end - at), 1) });
                }
            }
            return timeline;
        }

        /// <summary>O mesmo filtro do FrameStats.Analyze: sem o aquecimento e sem as pausas.</summary>
        private static bool Used(FrameSample f)
        {
            return f.At >= FrameStats.WarmupSeconds && f.Ms > 0 && f.Ms < FrameStats.PauseMs;
        }

        /// <summary>
        /// A mesma conta do FrameStats.LowFps (FPS da média do pior fraction dos quadros) num segmento do vetor, sem
        /// ordená-lo: só os k mais lentos interessam, e um heap mínimo de tamanho k os separa em O(n log k).
        /// </summary>
        internal static double LowFps(float[] ms, int first, int n, double fraction)
        {
            int k = Math.Max(1, (int)Math.Round(n * fraction));
            if (k > n) k = n;
            var heap = new float[k];
            int size = 0;
            for (int j = first; j < first + n; j++)
            {
                float v = ms[j];
                if (size < k)
                {
                    // Sobe o novo valor até o lugar dele.
                    int c = size++;
                    while (c > 0)
                    {
                        int parent = (c - 1) / 2;
                        if (heap[parent] <= v) break;
                        heap[c] = heap[parent];
                        c = parent;
                    }
                    heap[c] = v;
                }
                else if (v > heap[0])
                {
                    // Troca o menor dos k maiores e desce até o lugar.
                    int c = 0;
                    while (true)
                    {
                        int child = 2 * c + 1;
                        if (child >= k) break;
                        if (child + 1 < k && heap[child + 1] < heap[child]) child++;
                        if (heap[child] >= v) break;
                        heap[c] = heap[child];
                        c = child;
                    }
                    heap[c] = v;
                }
            }
            double sum = 0;
            for (int j = 0; j < k; j++) sum += heap[j];
            return 1000.0 / (sum / k);
        }

        private static int Index(double at, double bucket, int count)
        {
            if (at < 0 || double.IsNaN(at)) return -1;
            return Math.Min(count - 1, (int)(at / bucket));
        }

        /// <summary>Uma casa decimal basta para o gráfico e deixa o XML curto.</summary>
        private static float Round(double value)
        {
            return (float)Math.Round(value, 1);
        }
    }
}
