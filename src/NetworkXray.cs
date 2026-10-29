using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseModes
{
    /// <summary>Um segundo de rede: ping ao roteador e à internet, em ms.</summary>
    public struct NetSample
    {
        public const int Lost = -1;        // sem resposta no tempo
        public const int NotMeasured = -2; // sem roteador conhecido

        /// <summary>Segundos desde o começo da partida.</summary>
        public double At;
        public int Gateway;
        public int Internet;

        public NetSample(double at, int gateway, int internet)
        {
            At = at;
            Gateway = gateway;
            Internet = internet;
        }
    }

    /// <summary>Um momento em que um programa leu/gravou muitos dados (para apontar quem estava usando na hora).</summary>
    public struct IoBurst
    {
        public double At;
        public string Name;
        public long Bytes;

        public IoBurst(double at, string name, long bytes)
        {
            At = at;
            Name = name;
            Bytes = bytes;
        }
    }

    /// <summary>Um pico de ping (ou perda) durante a partida.</summary>
    public class NetSpike
    {
        public double At { get; set; }
        public double Seconds { get; set; }
        /// <summary>Maior ping no pico, em ms (0 se só houve perda).</summary>
        public int PeakMs { get; set; }
        public bool Lost { get; set; }
        /// <summary>O roteador também piorou: o problema começou na rede de casa (Wi-Fi, cabo, roteador).</summary>
        public bool Local { get; set; }
        /// <summary>Quem mais leu/gravou (disco, quase sempre) na hora; null se ninguém se destacou.</summary>
        public string Busy { get; set; }
        public long BusyBytes { get; set; }
    }

    /// <summary>Unknown: houve problema, mas o roteador não responde a ping e não dá para separar Wi-Fi de internet.</summary>
    public enum NetVerdict { Stable, Local, Internet, Unknown }

    /// <summary>A rede durante a partida.</summary>
    public class NetworkReport
    {
        public int Samples { get; set; }
        public bool Wifi { get; set; }
        /// <summary>A internet saiu por uma VPN ou túnel: os picos depois do roteador podem ser dela.</summary>
        public bool Vpn { get; set; }
        public int InternetMedian { get; set; }
        public double Jitter { get; set; }
        public double LossPercent { get; set; }
        /// <summary>Ping mediano ao roteador; -1 se o roteador não responde a ping (comum) ou não é conhecido.</summary>
        public int GatewayMedian { get; set; }
        public NetVerdict Verdict { get; set; }
        /// <summary>Total de picos; a lista guarda só os piores (em ordem de tempo).</summary>
        public int SpikeCount { get; set; }
        public List<NetSpike> Spikes { get; set; }

        /// <summary>"ping 18 ms · jitter 2 ms · perda 0% · roteador 3 ms · Wi-Fi"</summary>
        public string Summary()
        {
            var culture = Culture();
            string text = "ping " + InternetMedian + " ms · jitter " + Jitter.ToString("0", culture) + " ms · " +
                          L.T("perda ", "loss ") + LossPercent.ToString(LossPercent < 10 && LossPercent > 0 ? "0.#" : "0", culture) + "%";
            if (GatewayMedian >= 0) text += L.T(" · roteador ", " · router ") + GatewayMedian + " ms";
            return text + (Wifi ? " · Wi-Fi" : L.T(" · cabo", " · cable")) + (Vpn ? " · VPN" : "");
        }

        public string VerdictText()
        {
            switch (Verdict)
            {
                case NetVerdict.Local:
                    return Wifi
                        ? L.T("Os problemas começaram no Wi-Fi, antes do roteador. Cabo ou a rede de 5 GHz costumam resolver.",
                              "The problems started on the Wi-Fi, before the router. A cable or the 5 GHz network usually fixes it.")
                        : L.T("Os problemas começaram na sua rede, antes do roteador: confira o cabo e o roteador.",
                              "The problems started on your network, before the router: check the cable and the router.");
                case NetVerdict.Unknown:
                    return L.T("Houve problemas, mas o seu roteador não responde a ping: não dá para saber se vieram do Wi-Fi ou da internet.",
                               "There were problems, but your router does not answer ping: there is no telling whether they came from the Wi-Fi or the internet.");
                case NetVerdict.Internet:
                    return Vpn
                        ? L.T("Até o roteador estava tudo bem: os problemas vieram depois dele, da VPN ligada ou do provedor. Vale testar sem a VPN.",
                              "Everything was fine up to the router: the problems came after it, from the VPN that is on or from your provider. Try without the VPN.")
                        : L.T("Até o roteador estava tudo bem: os problemas vieram da internet (provedor). Se for sempre, vale falar com ele.",
                              "Everything was fine up to the router: the problems came from the internet (your provider). If it keeps happening, talk to them.");
                default:
                    return SpikeCount == 0
                        ? L.T("Rede estável durante a partida.", "Network stable during the match.")
                        : L.T("Rede estável, com picos curtos.", "Network stable, with short spikes.");
            }
        }

        /// <summary>
        /// "aos 23 min · 140 ms por 6 s · nessa hora o OneDrive lia/gravava 120 MB". Os bytes de cada programa são quase
        /// só de disco (o Windows não conta o tráfego de socket ali), mas download e sincronização passam pelo disco.
        /// </summary>
        public static string Describe(NetSpike s)
        {
            int minute = (int)(s.At / 60);
            string text = L.T("aos ", "at ") + minute + " min · " +
                          (s.PeakMs > 0 ? s.PeakMs + " ms" : L.T("sem resposta", "no reply")) +
                          L.T(" por ", " for ") + Math.Max(1, (int)Math.Round(s.Seconds)) + " s";
            if (s.Lost && s.PeakMs > 0) text += L.T(" · com perda", " · with loss");
            if (s.Local) text += L.T(" · no roteador também", " · at the router too");
            if (s.Busy != null)
                text += L.T(" · nessa hora o ", " · meanwhile ") + s.Busy + L.T(" lia/gravava ", " read/wrote ") + MatchReport.FormatBytes(s.BusyBytes);
            return text;
        }

        private static System.Globalization.CultureInfo Culture()
        {
            return L.IsEnglish ? System.Globalization.CultureInfo.InvariantCulture : System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        }
    }

    /// <summary>As contas da rede, sem rede: recebe os pings de cada segundo e acha os picos.</summary>
    public static class NetworkStats
    {
        /// <summary>Menos que isso (meio minuto) não dá para dizer nada.</summary>
        internal const int MinSamples = 30;
        internal const int MaxSpikes = 3;
        /// <summary>Leituras ruins com até este intervalo entre elas são o mesmo pico.</summary>
        private const double SameSpikeGap = 2.5;
        /// <summary>Quem leu/gravou dados até 3 s antes ou depois do pico (a leitura de processos é a cada 3 s).</summary>
        private const double BusyWindow = 3.0;
        /// <summary>Um pico sozinho com pelo menos isso (s) já é problema: é uma queda, não um soluço.</summary>
        internal const double LongSpike = 5.0;

        public static NetworkReport Analyze(IList<NetSample> samples, bool wifi, IList<IoBurst> bursts)
        {
            if (samples == null || samples.Count < MinSamples) return null;

            var internet = new List<int>();
            var gateway = new List<int>();
            int lost = 0, lostAtGateway = 0;
            foreach (NetSample s in samples)
            {
                if (s.Internet >= 0) internet.Add(s.Internet); else lost++;
                if (s.Gateway >= 0) gateway.Add(s.Gateway);
                if (s.Internet == NetSample.Lost && s.Gateway == NetSample.Lost) lostAtGateway++;
            }
            if (internet.Count == 0) return null; // ping bloqueado ou sem internet: não dá para medir

            int median = Median(internet);
            double jitter = 0;
            int previous = -1, pairs = 0;
            foreach (NetSample s in samples)
            {
                if (s.Internet < 0) continue;
                if (previous >= 0) { jitter += Math.Abs(s.Internet - previous); pairs++; }
                previous = s.Internet;
            }
            jitter = pairs > 0 ? jitter / pairs : 0;

            // Roteador que nunca responde (muitos bloqueiam ping) não serve de referência.
            bool hasGateway = gateway.Count > 0;
            int gatewayMedian = hasGateway ? Median(gateway) : -1;
            int threshold = Math.Max(median + 50, median * 2);
            int gatewayThreshold = Math.Max(gatewayMedian + 30, gatewayMedian * 3);

            var spikes = new List<NetSpike>();
            NetSpike open = null;
            int openBad = 0;
            double lastBad = double.MinValue;
            foreach (NetSample s in samples)
            {
                bool bad = s.Internet == NetSample.Lost || s.Internet > threshold;
                if (!bad) continue;
                bool local = hasGateway && (s.Gateway == NetSample.Lost || s.Gateway > gatewayThreshold);
                if (open == null || s.At - lastBad > SameSpikeGap)
                {
                    Close(spikes, open, openBad);
                    open = new NetSpike { At = s.At };
                    openBad = 0;
                }
                openBad++;
                open.Seconds = s.At - open.At + 1;
                if (s.Internet > open.PeakMs) open.PeakMs = s.Internet;
                if (s.Internet == NetSample.Lost) open.Lost = true;
                if (local) open.Local = true;
                lastBad = s.At;
            }
            Close(spikes, open, openBad);

            foreach (NetSpike spike in spikes) Blame(spike, bursts);

            double lossPercent = lost * 100.0 / samples.Count;
            int localSpikes = spikes.FindAll(sp => sp.Local).Count;
            NetVerdict verdict;
            if (spikes.Count <= 1 && lossPercent < 1 && (spikes.Count == 0 || spikes[0].Seconds < LongSpike))
                verdict = NetVerdict.Stable; // um soluço curto só não é problema
            else if (!hasGateway)
                verdict = NetVerdict.Unknown;
            else
            {
                // Cada pico e cada pacote perdido conta para um lado: começou antes do roteador ou depois dele.
                int local = localSpikes + lostAtGateway;
                int beyond = spikes.Count - localSpikes + lost - lostAtGateway;
                verdict = local > 0 && local >= beyond ? NetVerdict.Local : NetVerdict.Internet;
            }

            // Os piores primeiro para escolher, depois em ordem de tempo para mostrar.
            var worst = new List<NetSpike>(spikes);
            worst.Sort((a, b) => Severity(b).CompareTo(Severity(a)));
            if (worst.Count > MaxSpikes) worst.RemoveRange(MaxSpikes, worst.Count - MaxSpikes);
            worst.Sort((a, b) => a.At.CompareTo(b.At));

            return new NetworkReport
            {
                Samples = samples.Count,
                Wifi = wifi,
                InternetMedian = median,
                Jitter = jitter,
                LossPercent = lossPercent,
                GatewayMedian = gatewayMedian,
                Verdict = verdict,
                SpikeCount = spikes.Count,
                Spikes = worst
            };
        }

        /// <summary>Um pacote perdido sozinho é ruído (conta na perda); pico de ping, mesmo de 1 s, aparece.</summary>
        private static void Close(List<NetSpike> spikes, NetSpike spike, int bad)
        {
            if (spike == null) return;
            if (bad == 1 && spike.PeakMs <= 0) return;
            spikes.Add(spike);
        }

        private static void Blame(NetSpike spike, IList<IoBurst> bursts)
        {
            if (bursts == null) return;
            foreach (IoBurst b in bursts)
            {
                if (b.At < spike.At - BusyWindow || b.At > spike.At + spike.Seconds + BusyWindow) continue;
                if (b.Bytes > spike.BusyBytes)
                {
                    spike.Busy = b.Name;
                    spike.BusyBytes = b.Bytes;
                }
            }
        }

        private static double Severity(NetSpike s)
        {
            return (s.Lost ? 1000 : 0) + s.PeakMs + s.Seconds * 10;
        }

        internal static int Median(List<int> values)
        {
            var sorted = new List<int>(values);
            sorted.Sort();
            return sorted[sorted.Count / 2];
        }
    }

    /// <summary>
    /// Mede a rede durante a partida: a cada segundo, um ping ao roteador e um ao 1.1.1.1 (Cloudflare), numa thread
    /// própria. Só ICMP de 32 bytes, sem dado nenhum do PC, sem administrador.
    /// </summary>
    public sealed class NetworkProbe
    {
        internal static readonly IPAddress InternetHost = IPAddress.Parse("1.1.1.1");
        private const int TimeoutMs = 1000;
        private const int RediscoverEvery = 60; // segundos: troca de Wi-Fi para cabo no meio da partida

        [DllImport("iphlpapi.dll")]
        private static extern int GetBestInterface(uint destination, out uint bestIfIndex);

        private readonly DateTime started;
        private readonly List<NetSample> samples = new List<NetSample>();
        private readonly Thread thread;
        private volatile bool running = true;
        private IPAddress gateway;
        private bool wifi;
        private bool vpn;

        public NetworkProbe(DateTime started)
        {
            this.started = started;
            thread = new Thread(Run) { IsBackground = true, Name = "MouseModes network X-ray", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        /// <summary>Para de medir e devolve o que foi medido.</summary>
        /// <param name="onWifi">O roteador é alcançado pelo Wi-Fi.</param>
        /// <param name="viaVpn">A internet sai por outra placa (VPN ou túnel), não pela do roteador.</param>
        public List<NetSample> Stop(out bool onWifi, out bool viaVpn)
        {
            // Sem Join: a thread é de fundo e termina sozinha depois do ping em andamento (até 2 s). Esperar travaria
            // a bandeja no fim da partida justo quando a rede não responde.
            running = false;
            onWifi = wifi;
            viaVpn = vpn;
            lock (samples) return new List<NetSample>(samples);
        }

        private void Run()
        {
            using (var ping = new Ping())
            {
                var clock = Stopwatch.StartNew();
                var buffer = new byte[32];
                int tick = 0;
                while (running)
                {
                    if (tick % RediscoverEvery == 0) Discover();
                    long begin = clock.ElapsedMilliseconds;
                    int toGateway = gateway == null ? NetSample.NotMeasured : Send(ping, gateway, buffer);
                    int toInternet = Send(ping, InternetHost, buffer);
                    lock (samples) samples.Add(new NetSample((DateTime.UtcNow - started).TotalSeconds, toGateway, toInternet));
                    tick++;
                    long wait = 1000 - (clock.ElapsedMilliseconds - begin);
                    for (; running && wait > 0; wait -= 100) Thread.Sleep((int)Math.Min(100, wait));
                }
            }
        }

        private static int Send(Ping ping, IPAddress host, byte[] buffer)
        {
            try
            {
                PingReply reply = ping.Send(host, TimeoutMs, buffer);
                return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : NetSample.Lost;
            }
            catch
            {
                return NetSample.Lost; // sem rede no momento
            }
        }

        /// <summary>
        /// O roteador de casa e se ele é alcançado pelo Wi-Fi. É o da placa por onde o Windows sai para a internet;
        /// com uma VPN ligada, essa placa não tem roteador, e vale a placa de verdade (Wi-Fi ou cabo) que tem um.
        /// </summary>
        private void Discover()
        {
            try
            {
                uint best;
                bool known = GetBestInterface(BitConverter.ToUInt32(InternetHost.GetAddressBytes(), 0), out best) == 0;
                NetworkInterface chosen = null;
                IPAddress chosenGateway = null;
                int chosenIndex = -1;
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.GigabitEthernet) continue;
                    IPInterfaceProperties props = nic.GetIPProperties();
                    IPv4InterfaceProperties v4 = props.GetIPv4Properties();
                    IPAddress g = Gateway(props);
                    if (v4 == null || g == null) continue;
                    // A placa da internet vence; se ela não tem roteador (VPN), fica a primeira de verdade que tem.
                    if (chosen == null || (known && v4.Index == best))
                    {
                        chosen = nic;
                        chosenGateway = g;
                        chosenIndex = v4.Index;
                    }
                }
                // Nenhuma placa com roteador agora (Wi-Fi caiu): fica o que já era conhecido, e os pings perdidos ao
                // roteador mostram que o problema é local.
                if (chosen == null) return;
                gateway = chosenGateway;
                wifi = chosen.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                vpn = known && chosenIndex != best;
            }
            catch
            {
                // Sem informação da placa: mede só a internet.
            }
        }

        private static IPAddress Gateway(IPInterfaceProperties props)
        {
            foreach (GatewayIPAddressInformation g in props.GatewayAddresses)
                if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))
                    return g.Address;
            return null;
        }
    }
}
