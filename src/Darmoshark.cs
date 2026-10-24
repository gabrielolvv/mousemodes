using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MouseModes
{
    /// <summary>Estágios de DPI do mouse e qual está ativo.</summary>
    public class DpiStages
    {
        public int[] Values = new int[0];   // até 5 estágios (DPI real, ex.: 800)
        public int ActiveIndex;            // 0-based
        public string Source = "";         // de onde vieram os valores (para o Diagnóstico)

        public int Count { get { return Values.Length; } }
    }

    /// <summary>
    /// Troca o estágio de DPI de mouses Darmoshark da família M3/N3/M3S (chip VID 248A).
    ///
    /// Protocolo descoberto no programa oficial (DeviceDriver.exe 1.8.3.1), comando "0x51 0x40: definir DPI":
    ///   feature report de 21 bytes na interface de configuração (usage page 0x008C):
    ///   [0]=0x51 [1]=0x40 [2]=estágio (cabo) [3]=estágio (2.4G) [4]=0xFF
    ///   [5..14] = 5 estágios em DPI, 16 bits little-endian   [15]=quantidade de estágios   [16..20]=0
    ///   O byte de estágio que não se aplica ao modo de conexão fica 0xFF.
    /// O MouseModes manda os MESMOS estágios que estão no programa da Darmoshark e só muda o ativo.
    /// </summary>
    public static class Darmoshark
    {
        private const ushort Vendor = 0x248A;
        private const ushort ConfigUsagePage = 0x008C;

        // PIDs do config.xml do programa oficial (modo cabo USB e receptores 2.4G).
        private static readonly Dictionary<ushort, string> WiredModels = new Dictionary<ushort, string>
        {
            { 0xFF12, "M3" }, { 0xFF10, "N3" }, { 0xFF18, "M3S" }
        };
        private static readonly Dictionary<ushort, string> DongleModels = new Dictionary<ushort, string>
        {
            { 0xFF30, "receptor 2.4G" }, { 0xFF31, "receptor 2.4G" }
        };

        public static readonly int[] DefaultStages = { 400, 800, 1600, 3200, 4800 };

        // ---------------------------------------------------------------- dispositivo

        public class Device
        {
            public string Path;
            public string Model;
            public bool Wireless;
            public ushort ProductId;
        }

        /// <summary>Procura a interface de configuração de um Darmoshark conectado (ou null).</summary>
        public static Device Find()
        {
            foreach (HidInfo h in EnumerateHid())
            {
                if (h.Vendor != Vendor || h.UsagePage != ConfigUsagePage) continue;
                string model;
                if (WiredModels.TryGetValue(h.Product, out model))
                    return new Device { Path = h.Path, Model = model, Wireless = false, ProductId = h.Product };
                if (DongleModels.TryGetValue(h.Product, out model))
                    return new Device { Path = h.Path, Model = model, Wireless = true, ProductId = h.Product };
            }
            return null;
        }

        /// <summary>Monta o comando "definir DPI" (exposto para testes).</summary>
        public static byte[] BuildSetDpi(DpiStages stages, int activeIndex, bool wireless)
        {
            if (stages.Count < 1 || stages.Count > 5) throw new ArgumentException("o mouse aceita de 1 a 5 estágios");
            if (activeIndex < 0 || activeIndex >= stages.Count) throw new ArgumentOutOfRangeException("activeIndex");

            var b = new byte[21];
            b[0] = 0x51;
            b[1] = 0x40;
            b[2] = 0xFF;
            b[3] = 0xFF;
            b[4] = 0xFF;
            if (wireless) b[3] = (byte)activeIndex; else b[2] = (byte)activeIndex;
            for (int i = 0; i < 5; i++)
            {
                // Estágios não usados repetem o último valor (o mouse só usa os "quantidade" primeiros).
                int dpi = stages.Values[Math.Min(i, stages.Count - 1)];
                if (dpi < 50 || dpi > 26000) throw new ArgumentOutOfRangeException("DPI fora do intervalo: " + dpi);
                b[5 + i * 2] = (byte)(dpi & 0xFF);
                b[6 + i * 2] = (byte)(dpi >> 8);
            }
            b[15] = (byte)stages.Count;
            return b;
        }

        /// <summary>Troca o estágio ativo. Retorna null se deu certo, ou a mensagem de erro.</summary>
        public static string SetActiveStage(Device dev, DpiStages stages, int activeIndex)
        {
            byte[] report;
            try { report = BuildSetDpi(stages, activeIndex, dev.Wireless); }
            catch (Exception ex) { return ex.Message; }

            using (SafeFileHandle h = CreateFile(dev.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                       IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (h.IsInvalid)
                    return "não foi possível abrir o mouse (erro " + Marshal.GetLastWin32Error() + ")";
                if (!HidD_SetFeature(h, report, report.Length))
                    return "o mouse recusou o comando (erro " + Marshal.GetLastWin32Error() + ")";
            }
            Log.Info(string.Format("DPI: {0} → estágio {1} de {2} ({3} DPI) [{4}]", dev.Model, activeIndex + 1, stages.Count,
                stages.Values[activeIndex], BitConverter.ToString(report, 0, 16)));
            return null;
        }

        // ---------------------------------------------------------------- estágios do programa oficial

        /// <summary>
        /// Lê os estágios do perfil ativo no programa da Darmoshark
        /// (Documentos\Darmoshark Mouse Files\db\Darmoshark-*_data.db). Retorna null se não achar.
        /// </summary>
        public static DpiStages ReadOfficialStages(string model)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Darmoshark Mouse Files", "db");
                if (!Directory.Exists(folder)) return null;
                string file = Path.Combine(folder, "Darmoshark-" + (model ?? "m3").ToLowerInvariant() + "_data.db");
                if (!File.Exists(file))
                {
                    string[] any = Directory.GetFiles(folder, "Darmoshark-*_data.db");
                    if (any.Length == 0) return null;
                    file = any[0];
                }

                var sql = new SqliteMini(file);
                long activeProfile = 1;
                foreach (object[] r in sql.ReadTable("t_profile_data"))
                {
                    // profile, name, status, app, type
                    if (r.Length >= 3 && r[2] is long && (long)r[2] == 1 && r[0] is long) activeProfile = (long)r[0];
                }

                var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (object[] r in sql.ReadTable("t_configdata"))
                {
                    // config_id, profile, key, value
                    if (r.Length >= 4 && r[1] is long && (long)r[1] == activeProfile && r[2] is string)
                        cfg[(string)r[2]] = Convert.ToString(r[3]);
                }

                int count;
                if (!cfg.ContainsKey("dpi_count") || !int.TryParse(cfg["dpi_count"], out count)) return null;
                count = Math.Max(1, Math.Min(5, count));
                var values = new int[count];
                for (int i = 0; i < count; i++)
                {
                    string v;
                    if (!cfg.TryGetValue("dpi_group" + i + "_value", out v) || !int.TryParse(v, out values[i])) return null;
                }
                int index;
                int.TryParse(cfg.ContainsKey("dpi_index") ? cfg["dpi_index"] : "0", out index);
                return new DpiStages
                {
                    Values = values,
                    ActiveIndex = Math.Max(0, Math.Min(count - 1, index)),
                    Source = "programa da Darmoshark, Profile " + activeProfile
                };
            }
            catch (Exception ex)
            {
                Log.Warn("Não foi possível ler os estágios do programa da Darmoshark: " + ex.Message);
                return null;
            }
        }

        // ---------------------------------------------------------------- HID

        private class HidInfo
        {
            public string Path;
            public ushort Vendor, Product, UsagePage, Usage;
        }

        private static IEnumerable<HidInfo> EnumerateHid()
        {
            var list = new List<HidInfo>();
            Guid g;
            HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return list;
            try
            {
                for (int i = 0; ; i++)
                {
                    var d = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                    int req;
                    SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    if (req <= 0) continue;
                    IntPtr buf = Marshal.AllocHGlobal(req);
                    string path = null;
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero))
                            path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                    if (path == null || path.IndexOf("vid_248a", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // Acesso 0: só consulta, funciona mesmo com o Windows usando o mouse.
                    using (SafeFileHandle h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                    {
                        if (h.IsInvalid) continue;
                        var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                        if (!HidD_GetAttributes(h, ref a)) continue;
                        var info = new HidInfo { Path = path, Vendor = a.VendorID, Product = a.ProductID };
                        IntPtr pp;
                        if (HidD_GetPreparsedData(h, out pp))
                        {
                            var caps = new HIDP_CAPS();
                            if (HidP_GetCaps(pp, ref caps) == HIDP_STATUS_SUCCESS)
                            {
                                info.UsagePage = caps.UsagePage;
                                info.Usage = caps.Usage;
                            }
                            HidD_FreePreparsedData(pp);
                        }
                        list.Add(info);
                    }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        private const int DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
        private const int HIDP_STATUS_SUCCESS = 0x110000;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr p, ref HIDP_CAPS c);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetFeature(SafeFileHandle h, byte[] report, int length);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr info);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    }
}
