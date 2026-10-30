using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MouseModes
{
    /// <summary>Limitador de FPS do driver da placa de vídeo, por executável (o Windows não tem um próprio).</summary>
    public interface IFpsLimiter
    {
        /// <summary>"NVIDIA" ou "Intel".</summary>
        string Name { get; }

        /// <summary>Limite gravado para o executável (0 = sem limite). null se leu; senão, o motivo.</summary>
        string Read(string exe, out int fps);

        /// <summary>Grava o limite (0 tira). null se gravou; needsAdmin quando o driver só aceita como administrador.</summary>
        string Write(string exe, int fps, out bool needsAdmin);
    }

    /// <summary>Uma mudança no driver: o limite de um executável (0 = tirar).</summary>
    public class FpsCapChange
    {
        public string Exe;
        public int Fps;
    }

    /// <summary>
    /// Limite de FPS por jogo, gravado no limitador do próprio driver (o "Taxa máxima de quadros" do painel da NVIDIA, o
    /// limitador da Intel Arc). Fica no perfil do jogo no driver: vale a partir da próxima vez que o jogo abrir, mesmo
    /// com o MouseModes fechado. O app grava só ao salvar os Ajustes, nunca no meio da partida.
    /// </summary>
    public static class FpsCap
    {
        /// <summary>Taxa do monitor menos 3: fica dentro do G-SYNC/FreeSync, sem o atraso do V-Sync.</summary>
        public const int Auto = -1;

        /// <summary>Opções da tela de jogos: sem limite, automático e valores comuns.</summary>
        public static readonly int[] Options = { 0, Auto, 60, 120, 144, 240 };

        public const int MaxFps = 1000;

        /// <summary>Argumento da cópia que roda como administrador só para gravar no driver.</summary>
        internal const string HelperArg = "--fps-caps";

        private const int ErrorCancelled = 1223;

        private static readonly Regex ExeName = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 ._()+\-]{0,118}\.exe$", RegexOptions.IgnoreCase);

        /// <summary>O limite em FPS de uma escolha da tela (0 = sem limite; automático sem taxa conhecida também).</summary>
        public static int Resolve(int choice, int refreshHz)
        {
            if (choice == Auto) return refreshHz > 33 ? Math.Min(refreshHz - 3, MaxFps) : 0;
            return choice <= 0 ? 0 : Math.Min(choice, MaxFps);
        }

        /// <summary>Taxa atual do monitor principal (0 se não deu para ler).</summary>
        public static int PrimaryRefresh()
        {
            try
            {
                string primary = System.Windows.Forms.Screen.PrimaryScreen.DeviceName;
                MonitorStatus monitor = Displays.Scan().Find(m => string.Equals(m.DeviceName, primary, StringComparison.OrdinalIgnoreCase));
                return monitor != null ? monitor.CurrentHz : 0;
            }
            catch { return 0; }
        }

        /// <summary>O executável que o driver procura: o nome do jogo na lista com ".exe" ("cs2" → "cs2.exe").</summary>
        public static string ExeFor(string game)
        {
            string name = (game ?? "").Trim().ToLowerInvariant();
            return name.EndsWith(".exe", StringComparison.Ordinal) ? name : name + ".exe";
        }

        /// <summary>Só um nome de arquivo .exe, sem pasta nem aspas (é o que vai para a cópia administradora).</summary>
        public static bool ValidExe(string exe)
        {
            return exe != null && ExeName.IsMatch(exe);
        }

        /// <summary>Os limites pedidos na tela: jogos da lista com configurações próprias e um limite escolhido.</summary>
        public static Dictionary<string, int> Wanted(AppConfig cfg, int refreshHz)
        {
            var wanted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (GameProfile gp in cfg.GameProfiles)
            {
                if (gp == null || gp.Profile == null || gp.Profile.FpsCap == 0) continue;
                if (!cfg.Games.Exists(g => string.Equals(g, gp.Process, StringComparison.OrdinalIgnoreCase))) continue;
                string exe = ExeFor(gp.Process);
                int fps = Resolve(gp.Profile.FpsCap, refreshHz);
                if (fps > 0 && ValidExe(exe)) wanted[exe] = fps;
            }
            return wanted;
        }

        /// <summary>O que falta mudar no driver: limites novos ou diferentes, e os que saíram da tela (0).</summary>
        public static List<FpsCapChange> Plan(Dictionary<string, int> wanted, IEnumerable<string> applied)
        {
            Dictionary<string, int> done = Parse(applied);
            var changes = new List<FpsCapChange>();
            foreach (KeyValuePair<string, int> w in wanted)
            {
                int now;
                if (!done.TryGetValue(w.Key, out now) || now != w.Value) changes.Add(new FpsCapChange { Exe = w.Key, Fps = w.Value });
            }
            foreach (KeyValuePair<string, int> d in done)
                if (!wanted.ContainsKey(d.Key)) changes.Add(new FpsCapChange { Exe = d.Key, Fps = 0 });
            changes.Sort((a, b) => string.CompareOrdinal(a.Exe, b.Exe));
            return changes;
        }

        /// <summary>
        /// Grava as mudanças, relendo cada uma no driver para confirmar. O que o driver só aceita como administrador vai
        /// junto, num único pedido (elevate). applied fica só com o que foi confirmado. Devolve os problemas, um por linha.
        /// </summary>
        public static List<string> Apply(List<FpsCapChange> changes, IFpsLimiter limiter, List<string> applied,
                                         Func<List<FpsCapChange>, string> elevate)
        {
            var problems = new List<string>();
            var pending = new List<FpsCapChange>();
            foreach (FpsCapChange c in changes)
            {
                bool needsAdmin;
                string error = limiter.Write(c.Exe, c.Fps, out needsAdmin);
                if (error == null) Confirm(c, limiter, applied, problems);
                else if (needsAdmin) pending.Add(c);
                else problems.Add(c.Exe + ": " + error);
            }
            if (pending.Count == 0) return problems;

            string elevated = elevate(pending);
            foreach (FpsCapChange c in pending)
            {
                if (elevated != null) problems.Add(c.Exe + ": " + elevated);
                else Confirm(c, limiter, applied, problems);
            }
            return problems;
        }

        private static void Confirm(FpsCapChange c, IFpsLimiter limiter, List<string> applied, List<string> problems)
        {
            int now;
            string error = limiter.Read(c.Exe, out now);
            if (error != null)
            {
                problems.Add(c.Exe + ": " + error);
                return;
            }
            if (now != c.Fps)
            {
                problems.Add(c.Exe + L.T(": o driver ficou com ", ": the driver kept ") + (now == 0 ? L.T("nenhum limite", "no limit") : now + " FPS"));
                return;
            }
            applied.RemoveAll(a => string.Equals(KeyOf(a), c.Exe, StringComparison.OrdinalIgnoreCase));
            if (c.Fps > 0) applied.Add(Format(c.Exe, c.Fps));
        }

        internal static string Format(string exe, int fps)
        {
            return exe + "=" + fps;
        }

        private static string KeyOf(string entry)
        {
            int eq = (entry ?? "").LastIndexOf('=');
            return eq > 0 ? entry.Substring(0, eq) : entry;
        }

        /// <summary>"cs2.exe=141" → { cs2.exe: 141 }. Entradas estragadas ficam de fora.</summary>
        internal static Dictionary<string, int> Parse(IEnumerable<string> entries)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (entries == null) return result;
            foreach (string entry in entries)
            {
                int eq = (entry ?? "").LastIndexOf('=');
                int fps;
                if (eq <= 0 || !int.TryParse(entry.Substring(eq + 1), out fps) || fps < 0 || fps > MaxFps) continue;
                string exe = entry.Substring(0, eq);
                if (ValidExe(exe)) result[exe] = fps;
            }
            return result;
        }

        /// <summary>Os argumentos da cópia administradora, conferidos um a um. null se algum não for válido.</summary>
        internal static List<FpsCapChange> ParseHelperArgs(string[] args)
        {
            int start = Array.FindIndex(args, a => string.Equals(a, HelperArg, StringComparison.OrdinalIgnoreCase));
            if (start < 0 || start == args.Length - 1) return null;
            var changes = new List<FpsCapChange>();
            for (int i = start + 1; i < args.Length; i++)
            {
                Dictionary<string, int> one = Parse(new[] { args[i] });
                if (one.Count != 1) return null;
                foreach (KeyValuePair<string, int> kv in one) changes.Add(new FpsCapChange { Exe = kv.Key, Fps = kv.Value });
            }
            return changes;
        }

        /// <summary>A placa que tem limitador que o app consegue usar: NVIDIA primeiro, depois Intel. null se nenhuma.</summary>
        public static IFpsLimiter Detect()
        {
            return (IFpsLimiter)NvidiaLimiter.TryCreate() ?? IntelLimiter.TryCreate();
        }

        /// <summary>Roda esta mesma cópia como administrador só para gravar as mudanças. null se deu certo.</summary>
        public static string RunElevated(List<FpsCapChange> changes)
        {
            var args = new StringBuilder(HelperArg);
            foreach (FpsCapChange c in changes)
            {
                if (!ValidExe(c.Exe)) return L.T("nome de executável inválido", "invalid executable name");
                args.Append(" \"").Append(Format(c.Exe, c.Fps)).Append('"');
            }
            try
            {
                var info = new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, args.ToString())
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(info))
                {
                    p.WaitForExit(60000);
                    if (!p.HasExited) return L.T("o driver não respondeu a tempo", "the driver did not answer in time");
                    return p.ExitCode == 0 ? null : L.T("o driver recusou a gravação (código ", "the driver refused to save it (code ") + p.ExitCode + ")";
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return L.T("cancelado no pedido de administrador", "cancelled at the administrator prompt");
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>A cópia administradora: grava e sai. 0 = tudo gravado; 1 = algum erro; 2 = argumentos ou placa.</summary>
        internal static int RunHelper(string[] args)
        {
            List<FpsCapChange> changes = ParseHelperArgs(args);
            if (changes == null) return 2;
            IFpsLimiter limiter = Detect();
            if (limiter == null) return 2;
            int result = 0;
            foreach (FpsCapChange c in changes)
            {
                bool needsAdmin;
                if (limiter.Write(c.Exe, c.Fps, out needsAdmin) != null) result = 1;
            }
            return result;
        }
    }

    /// <summary>
    /// NVIDIA: o perfil de aplicativo do driver (DRS, o mesmo do Painel de Controle e do NVIDIA Profile Inspector), com a
    /// configuração "Frame Rate Limiter". Se o jogo já tem perfil da NVIDIA (cs2.exe está no "Counter-Strike 2"), o limite
    /// entra nele; se não, o app cria um perfil "MouseModes - jogo.exe". Tirar o limite devolve o valor padrão do perfil
    /// da NVIDIA, ou apaga o perfil que o app criou.
    /// </summary>
    internal sealed class NvidiaLimiter : IFpsLimiter
    {
        internal const uint FrameRateLimiterId = 0x10835002; // FRL_FPS_ID, NvApiDriverSettings.h
        internal const string ProfilePrefix = "MouseModes - ";

        // Tamanhos das estruturas do nvapi.h (pack 4 na NVDRS_SETTING; NvAPI_UnicodeString = 2048 NvU16).
        internal const int UnicodeBytes = 4096;
        internal const int SettingSize = 0x3020;     // NVDRS_SETTING_V1
        internal const int ApplicationSize = 0x500C; // NVDRS_APPLICATION_V4
        internal const int ProfileSize = 0x1014;     // NVDRS_PROFILE_V1
        internal const uint SettingVersion = SettingSize | (1u << 16);
        internal const uint ApplicationVersion = ApplicationSize | (4u << 16);
        internal const uint ProfileVersion = ProfileSize | (1u << 16);

        // Posições dentro das estruturas.
        internal const int SettingIdOffset = 4 + UnicodeBytes;            // settingId
        internal const int SettingTypeOffset = SettingIdOffset + 4;       // settingType (0 = DWORD)
        internal const int CurrentValueOffset = SettingIdOffset + 20 + 4100; // depois do union do valor padrão
        internal const int AppNameOffset = 8;                             // depois de version e isPredefined
        internal const int ProfileNameOffset = 4;

        private const int Ok = 0;
        private const int InvalidUserPrivilege = -137;
        private const int SettingNotFound = -160;
        private const int ProfileNotFound = -163;
        private const int ExecutableNotFound = -166;

        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Query64(uint id);

        [DllImport("nvapi.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Query32(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitializeFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateSessionFn(out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SessionFn(IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FindApplicationFn(IntPtr session, IntPtr appName, out IntPtr profile, IntPtr application);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FindProfileFn(IntPtr session, IntPtr profileName, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateProfileFn(IntPtr session, IntPtr profileInfo, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateApplicationFn(IntPtr session, IntPtr profile, IntPtr application);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeleteProfileFn(IntPtr session, IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSettingFn(IntPtr session, IntPtr profile, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSettingFn(IntPtr session, IntPtr profile, uint id, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeleteSettingFn(IntPtr session, IntPtr profile, uint id);

        private CreateSessionFn createSession;
        private SessionFn destroySession, loadSettings, saveSettings;
        private FindApplicationFn findApplication;
        private FindProfileFn findProfile;
        private CreateProfileFn createProfile;
        private CreateApplicationFn createApplication;
        private DeleteProfileFn deleteProfile;
        private SetSettingFn setSetting;
        private GetSettingFn getSetting;
        private DeleteSettingFn deleteSetting;

        public string Name { get { return "NVIDIA"; } }

        private NvidiaLimiter() { }

        /// <summary>null sem placa NVIDIA (ou sem o nvapi do driver).</summary>
        internal static NvidiaLimiter TryCreate()
        {
            try
            {
                var nv = new NvidiaLimiter();
                InitializeFn initialize = nv.Get<InitializeFn>(0x0150E828);
                if (initialize == null || initialize() != Ok) return null;
                nv.createSession = nv.Get<CreateSessionFn>(0x0694D52E);
                nv.destroySession = nv.Get<SessionFn>(0xDAD9CFF8);
                nv.loadSettings = nv.Get<SessionFn>(0x375DBD6B);
                nv.saveSettings = nv.Get<SessionFn>(0xFCBC7E14);
                nv.findApplication = nv.Get<FindApplicationFn>(0xEEE566B2);
                nv.findProfile = nv.Get<FindProfileFn>(0x7E4A9A0B);
                nv.createProfile = nv.Get<CreateProfileFn>(0xCC176068);
                nv.createApplication = nv.Get<CreateApplicationFn>(0x4347A9DE);
                nv.deleteProfile = nv.Get<DeleteProfileFn>(0x17093206);
                nv.setSetting = nv.Get<SetSettingFn>(0x577DD202);
                nv.getSetting = nv.Get<GetSettingFn>(0x73BF8338);
                nv.deleteSetting = nv.Get<DeleteSettingFn>(0xE4A26362);
                bool complete = nv.createSession != null && nv.destroySession != null && nv.loadSettings != null &&
                                nv.saveSettings != null && nv.findApplication != null && nv.findProfile != null &&
                                nv.createProfile != null && nv.createApplication != null && nv.deleteProfile != null && nv.setSetting != null &&
                                nv.getSetting != null && nv.deleteSetting != null;
                return complete ? nv : null;
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
            catch (BadImageFormatException) { return null; }
        }

        private T Get<T>(uint id) where T : class
        {
            IntPtr fn = Environment.Is64BitProcess ? Query64(id) : Query32(id);
            return fn == IntPtr.Zero ? null : (T)(object)Marshal.GetDelegateForFunctionPointer(fn, typeof(T));
        }

        public string Read(string exe, out int fps)
        {
            fps = 0;
            int found = 0;
            int status = InSession(false, (session) =>
            {
                IntPtr profile;
                int s = FindApplication(session, exe, out profile);
                if (s == ExecutableNotFound) return Ok;
                if (s != Ok) return s;
                using (var setting = new NativeBuffer(SettingSize))
                {
                    setting.WriteUInt32(0, SettingVersion);
                    s = getSetting(session, profile, FrameRateLimiterId, setting.Pointer);
                    if (s == SettingNotFound) return Ok;
                    if (s == Ok) found = (int)setting.ReadUInt32(CurrentValueOffset);
                    return s;
                }
            });
            fps = found;
            return status == Ok ? null : Describe(status);
        }

        public string Write(string exe, int fps, out bool needsAdmin)
        {
            int status = InSession(true, (session) =>
            {
                IntPtr profile;
                int s = FindApplication(session, exe, out profile);
                if (s == ExecutableNotFound)
                {
                    if (fps == 0) return Ok; // sem perfil, já não tem limite
                    s = OwnProfile(session, exe, out profile);
                }
                if (s != Ok) return s;
                if (fps == 0)
                {
                    // O perfil que o app criou só existe para o limite: sai inteiro. O da NVIDIA só perde o limite.
                    IntPtr own;
                    using (var name = NativeBuffer.Unicode(ProfilePrefix + exe, UnicodeBytes))
                        if (findProfile(session, name.Pointer, out own) == Ok && own == profile) return deleteProfile(session, own);
                    s = deleteSetting(session, profile, FrameRateLimiterId);
                    return s == SettingNotFound ? Ok : s;
                }
                using (var setting = new NativeBuffer(SettingSize))
                {
                    setting.WriteUInt32(0, SettingVersion);
                    setting.WriteUInt32(SettingIdOffset, FrameRateLimiterId);
                    setting.WriteUInt32(SettingTypeOffset, 0); // NVDRS_DWORD_TYPE
                    setting.WriteUInt32(CurrentValueOffset, (uint)fps);
                    return setSetting(session, profile, setting.Pointer);
                }
            });
            needsAdmin = status == InvalidUserPrivilege;
            return status == Ok ? null : Describe(status);
        }

        /// <summary>Abre uma sessão do DRS, carrega os perfis, roda a ação e (se gravar) salva. Devolve o código da NVAPI.</summary>
        private int InSession(bool save, Func<IntPtr, int> action)
        {
            IntPtr session;
            int s = createSession(out session);
            if (s != Ok) return s;
            try
            {
                s = loadSettings(session);
                if (s == Ok) s = action(session);
                if (s == Ok && save) s = saveSettings(session); // sem salvar, a sessão descartada não muda nada
                return s;
            }
            finally { destroySession(session); }
        }

        private int FindApplication(IntPtr session, string exe, out IntPtr profile)
        {
            using (var name = NativeBuffer.Unicode(exe, UnicodeBytes))
            using (var app = new NativeBuffer(ApplicationSize))
            {
                app.WriteUInt32(0, ApplicationVersion);
                return findApplication(session, name.Pointer, out profile, app.Pointer);
            }
        }

        /// <summary>O perfil "MouseModes - jogo.exe", criado se ainda não existe, com o executável dentro.</summary>
        private int OwnProfile(IntPtr session, string exe, out IntPtr profile)
        {
            string profileName = ProfilePrefix + exe;
            int s;
            using (var name = NativeBuffer.Unicode(profileName, UnicodeBytes)) s = findProfile(session, name.Pointer, out profile);
            if (s == ProfileNotFound)
            {
                using (var info = new NativeBuffer(ProfileSize))
                {
                    info.WriteUInt32(0, ProfileVersion);
                    info.WriteUnicode(ProfileNameOffset, profileName, UnicodeBytes);
                    s = createProfile(session, info.Pointer, out profile);
                }
            }
            if (s != Ok) return s;
            using (var app = new NativeBuffer(ApplicationSize))
            {
                app.WriteUInt32(0, ApplicationVersion);
                app.WriteUnicode(AppNameOffset, exe, UnicodeBytes);
                return createApplication(session, profile, app.Pointer);
            }
        }

        private static string Describe(int status)
        {
            if (status == InvalidUserPrivilege) return L.T("o driver da NVIDIA pede administrador", "the NVIDIA driver asks for the administrator");
            return L.T("o driver da NVIDIA recusou (NVAPI ", "the NVIDIA driver refused (NVAPI ") + status + ")";
        }
    }

    /// <summary>
    /// Intel: o limitador de quadros da biblioteca de controle do driver (IGCL, a mesma do Intel Graphics Software), por
    /// aplicativo. Só as placas Arc e os processadores Core Ultra têm; nos gráficos UHD/Iris o driver diz "não suportado".
    /// </summary>
    internal sealed class IntelLimiter : IFpsLimiter
    {
        private const int FeatureFrameLimit = 2;   // CTL_3D_FEATURE_FRAME_LIMIT
        private const int ValueTypeInt32 = 2;      // CTL_PROPERTY_VALUE_TYPE_INT32
        private const uint InsufficientPermissions = 0x40000006;
        private const uint UnsupportedFeature = 0x4000000A;

        [StructLayout(LayoutKind.Sequential)]
        internal struct InitArgs
        {
            public uint Size;
            public byte Version;
            public uint AppVersion;
            public uint Flags;
            public uint SupportedVersion;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] ApplicationUid;
        }

        /// <summary>ctl_3d_feature_getset_t com o valor int32 (o union ctl_property_t: Enable e Value).</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct FeatureGetSet
        {
            public uint Size;
            public byte Version;
            public int FeatureType;
            public IntPtr ApplicationName;
            public sbyte ApplicationNameLength;
            public byte IsSet;
            public int ValueType;
            public byte Enable;
            public int Value;
            public int CustomValueSize;
            public IntPtr CustomValue;
        }

        [DllImport("ControlLib.dll", EntryPoint = "ctlInit", CallingConvention = CallingConvention.Cdecl)] private static extern uint Init64(ref InitArgs args, out IntPtr api);
        [DllImport("ControlLib.dll", EntryPoint = "ctlClose", CallingConvention = CallingConvention.Cdecl)] private static extern uint Close64(IntPtr api);
        [DllImport("ControlLib.dll", EntryPoint = "ctlEnumerateDevices", CallingConvention = CallingConvention.Cdecl)] private static extern uint Devices64(IntPtr api, ref uint count, [Out] IntPtr[] devices);
        [DllImport("ControlLib.dll", EntryPoint = "ctlGetSet3DFeature", CallingConvention = CallingConvention.Cdecl)] private static extern uint GetSet64(IntPtr device, ref FeatureGetSet feature);
        [DllImport("ControlLib32.dll", EntryPoint = "ctlInit", CallingConvention = CallingConvention.Cdecl)] private static extern uint Init32(ref InitArgs args, out IntPtr api);
        [DllImport("ControlLib32.dll", EntryPoint = "ctlClose", CallingConvention = CallingConvention.Cdecl)] private static extern uint Close32(IntPtr api);
        [DllImport("ControlLib32.dll", EntryPoint = "ctlEnumerateDevices", CallingConvention = CallingConvention.Cdecl)] private static extern uint Devices32(IntPtr api, ref uint count, [Out] IntPtr[] devices);
        [DllImport("ControlLib32.dll", EntryPoint = "ctlGetSet3DFeature", CallingConvention = CallingConvention.Cdecl)] private static extern uint GetSet32(IntPtr device, ref FeatureGetSet feature);

        public string Name { get { return "Intel"; } }

        private IntelLimiter() { }

        /// <summary>null sem placa Intel com limitador por aplicativo (ou sem a biblioteca do driver).</summary>
        internal static IntelLimiter TryCreate()
        {
            try
            {
                var intel = new IntelLimiter();
                int fps;
                return intel.Read("mousemodes.exe", out fps) == null ? intel : null;
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
            catch (BadImageFormatException) { return null; }
        }

        public string Read(string exe, out int fps)
        {
            int found = 0;
            uint status = OnDevices(exe, false, 0, (f) => found = f.Enable != 0 ? f.Value : 0);
            fps = found;
            return status == 0 ? null : Describe(status);
        }

        public string Write(string exe, int fps, out bool needsAdmin)
        {
            uint status = OnDevices(exe, true, fps, null);
            needsAdmin = status == InsufficientPermissions;
            return status == 0 ? null : Describe(status);
        }

        /// <summary>Roda o get/set na primeira placa Intel que aceitar. Devolve o código da IGCL (0 = certo).</summary>
        private uint OnDevices(string exe, bool set, int fps, Action<FeatureGetSet> read)
        {
            bool x64 = Environment.Is64BitProcess;
            var init = new InitArgs { Size = (uint)Marshal.SizeOf(typeof(InitArgs)), AppVersion = (1u << 16) | 1u, ApplicationUid = new byte[16] };
            IntPtr api;
            uint status = x64 ? Init64(ref init, out api) : Init32(ref init, out api);
            if (status != 0) return status;
            IntPtr name = Marshal.StringToHGlobalAnsi(exe);
            try
            {
                uint count = 0;
                status = x64 ? Devices64(api, ref count, null) : Devices32(api, ref count, null);
                if (status != 0) return status;
                if (count == 0) return UnsupportedFeature;
                var devices = new IntPtr[count];
                status = x64 ? Devices64(api, ref count, devices) : Devices32(api, ref count, devices);
                if (status != 0) return status;
                status = UnsupportedFeature;
                foreach (IntPtr device in devices)
                {
                    var feature = new FeatureGetSet
                    {
                        Size = (uint)Marshal.SizeOf(typeof(FeatureGetSet)),
                        FeatureType = FeatureFrameLimit,
                        ApplicationName = name,
                        ApplicationNameLength = (sbyte)Math.Min(exe.Length, sbyte.MaxValue),
                        IsSet = (byte)(set ? 1 : 0),
                        ValueType = ValueTypeInt32,
                        Enable = (byte)(fps > 0 ? 1 : 0),
                        Value = fps
                    };
                    status = x64 ? GetSet64(device, ref feature) : GetSet32(device, ref feature);
                    if (status != 0) continue;
                    if (read != null) read(feature);
                    return 0;
                }
                return status;
            }
            finally
            {
                Marshal.FreeHGlobal(name);
                if (x64) Close64(api); else Close32(api);
            }
        }

        private static string Describe(uint status)
        {
            if (status == InsufficientPermissions) return L.T("o driver da Intel pede administrador", "the Intel driver asks for the administrator");
            if (status == UnsupportedFeature) return L.T("o driver da Intel não tem limitador nesta placa", "the Intel driver has no limiter on this card");
            return L.T("o driver da Intel recusou (código 0x", "the Intel driver refused (code 0x") + status.ToString("X") + ")";
        }
    }

    /// <summary>Memória zerada para as estruturas da NVAPI (grandes demais para declarar campo a campo).</summary>
    internal sealed class NativeBuffer : IDisposable
    {
        public readonly IntPtr Pointer;
        private readonly int size;

        public NativeBuffer(int size)
        {
            this.size = size;
            Pointer = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i++) Marshal.WriteByte(Pointer, i, 0);
        }

        public static NativeBuffer Unicode(string text, int bytes)
        {
            var b = new NativeBuffer(bytes);
            b.WriteUnicode(0, text, bytes);
            return b;
        }

        public void WriteUInt32(int offset, uint value)
        {
            Check(offset, 4);
            Marshal.WriteInt32(Pointer, offset, unchecked((int)value));
        }

        public uint ReadUInt32(int offset)
        {
            Check(offset, 4);
            return unchecked((uint)Marshal.ReadInt32(Pointer, offset));
        }

        /// <summary>Texto UTF-16 terminado em zero, cortado para caber em bytes.</summary>
        public void WriteUnicode(int offset, string text, int bytes)
        {
            Check(offset, bytes);
            char[] chars = (text ?? "").ToCharArray();
            int max = Math.Min(chars.Length, bytes / 2 - 1);
            for (int i = 0; i < max; i++) Marshal.WriteInt16(Pointer, offset + i * 2, (short)chars[i]);
        }

        private void Check(int offset, int length)
        {
            if (offset < 0 || offset + length > size) throw new ArgumentOutOfRangeException("offset");
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Pointer);
        }
    }
}
