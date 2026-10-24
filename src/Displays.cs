using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MouseModes
{
    public class MonitorStatus
    {
        public string Label;       // "Monitor 1"
        public string DeviceName;  // \\.\DISPLAY1
        public int Width;
        public int Height;
        public int CurrentHz;
        public int MaxHz;

        public bool BelowMax { get { return CurrentHz > 0 && MaxHz > CurrentHz; } }
    }

    /// <summary>Lê a taxa de atualização de cada monitor e a maior que ele aceita na mesma resolução.</summary>
    public static class Displays
    {
        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
        private const int DM_DISPLAYFREQUENCY = 0x400000;
        private const int DM_INTERLACED = 0x2;
        private const int CDS_UPDATEREGISTRY = 0x1;
        private const int CDS_TEST = 0x2;
        private const int DISP_CHANGE_SUCCESSFUL = 0;
        private const int DISP_CHANGE_RESTART = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr lParam);

        private static DEVMODE NewDevMode()
        {
            var dm = new DEVMODE();
            dm.dmDeviceName = new string('\0', 32);
            dm.dmFormName = new string('\0', 32);
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            return dm;
        }

        public static List<MonitorStatus> Scan()
        {
            var result = new List<MonitorStatus>();
            try
            {
                var dev = new DISPLAY_DEVICE();
                dev.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                int number = 0;
                for (uint i = 0; EnumDisplayDevices(null, i, ref dev, 0); i++)
                {
                    if ((dev.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                    {
                        DEVMODE current = NewDevMode();
                        if (EnumDisplaySettings(dev.DeviceName, ENUM_CURRENT_SETTINGS, ref current))
                        {
                            number++;
                            var status = new MonitorStatus
                            {
                                Label = "Monitor " + number,
                                DeviceName = dev.DeviceName,
                                Width = current.dmPelsWidth,
                                Height = current.dmPelsHeight,
                                CurrentHz = current.dmDisplayFrequency,
                                MaxHz = current.dmDisplayFrequency
                            };

                            DEVMODE mode = NewDevMode();
                            for (int m = 0; EnumDisplaySettings(dev.DeviceName, m, ref mode); m++)
                            {
                                bool sameRes = mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight;
                                bool sameDepth = mode.dmBitsPerPel == current.dmBitsPerPel;
                                bool interlaced = (mode.dmDisplayFlags & DM_INTERLACED) != 0;
                                if (sameRes && sameDepth && !interlaced && mode.dmDisplayFrequency > status.MaxHz)
                                    status.MaxHz = mode.dmDisplayFrequency;
                                mode = NewDevMode();
                            }
                            result.Add(status);
                        }
                    }
                    dev = new DISPLAY_DEVICE();
                    dev.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                }
            }
            catch
            {
                // Sem acesso à API de vídeo: sem informação, sem aviso.
            }
            return result;
        }

        /// <summary>Coloca o monitor na maior taxa disponível (testa antes de aplicar).</summary>
        public static bool SetMaxRefresh(MonitorStatus monitor, out string message)
        {
            message = null;
            try
            {
                DEVMODE dm = NewDevMode();
                if (!EnumDisplaySettings(monitor.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                {
                    message = "não foi possível ler a configuração atual";
                    return false;
                }
                dm.dmDisplayFrequency = monitor.MaxHz;
                dm.dmFields = DM_DISPLAYFREQUENCY;

                int test = ChangeDisplaySettingsEx(monitor.DeviceName, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
                if (test != DISP_CHANGE_SUCCESSFUL)
                {
                    message = "o driver recusou " + monitor.MaxHz + " Hz (código " + test + ")";
                    return false;
                }

                int result = ChangeDisplaySettingsEx(monitor.DeviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
                if (result == DISP_CHANGE_RESTART)
                {
                    message = "vale depois de reiniciar o PC";
                    return true;
                }
                if (result != DISP_CHANGE_SUCCESSFUL)
                {
                    message = "falhou (código " + result + ")";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }
    }
}
