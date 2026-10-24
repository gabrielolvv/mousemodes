using System;
using System.Runtime.InteropServices;

namespace MouseModes
{
    /// <summary>Leitura direta do mouse via Raw Input (o mesmo caminho que os jogos usam).</summary>
    public sealed class RawMouse : IDisposable
    {
        public const int WM_INPUT = 0x00FF;

        private const uint RID_INPUT = 0x10000003;
        private const uint RIM_TYPEMOUSE = 0;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_REMOVE = 0x00000001;

        // Bits de usButtonFlags
        public const ushort LeftDown = 0x0001, LeftUp = 0x0002;
        public const ushort RightDown = 0x0004, RightUp = 0x0008;
        public const ushort MiddleDown = 0x0010, MiddleUp = 0x0020;
        public const ushort Button4Down = 0x0040, Button4Up = 0x0080;
        public const ushort Button5Down = 0x0100, Button5Up = 0x0200;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

        private static readonly uint HeaderSize = (uint)(8 + 2 * IntPtr.Size);

        private IntPtr buffer = IntPtr.Zero;
        private uint bufferSize;

        /// <summary>Começa a receber WM_INPUT do mouse na janela. sink = recebe mesmo sem foco.</summary>
        public bool Register(IntPtr hwnd, bool sink, out string error)
        {
            error = null;
            try
            {
                var device = new RAWINPUTDEVICE
                {
                    usUsagePage = 0x01, // Generic Desktop
                    usUsage = 0x02,     // Mouse
                    dwFlags = sink ? RIDEV_INPUTSINK : 0,
                    hwndTarget = hwnd
                };
                if (RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                    return true;
                error = "erro " + Marshal.GetLastWin32Error();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            return false;
        }

        public void Unregister()
        {
            try
            {
                var device = new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero };
                RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            }
            catch { }
        }

        /// <summary>Lê um WM_INPUT. Retorna false se não for do mouse.</summary>
        public bool Read(IntPtr hRawInput, out int dx, out int dy, out ushort buttonFlags)
        {
            dx = 0;
            dy = 0;
            buttonFlags = 0;

            uint size = 0;
            GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, HeaderSize);
            if (size == 0) return false;

            if (size > bufferSize)
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal((int)size);
                bufferSize = size;
            }

            if (GetRawInputData(hRawInput, RID_INPUT, buffer, ref size, HeaderSize) != size) return false;
            if ((uint)Marshal.ReadInt32(buffer, 0) != RIM_TYPEMOUSE) return false;

            // RAWMOUSE logo após o cabeçalho: usFlags(2) pad(2) usButtonFlags(2) usButtonData(2)
            // ulRawButtons(4) lLastX(4) lLastY(4)
            int b = (int)HeaderSize;
            buttonFlags = (ushort)Marshal.ReadInt16(buffer, b + 4);
            dx = Marshal.ReadInt32(buffer, b + 12);
            dy = Marshal.ReadInt32(buffer, b + 16);
            return true;
        }

        public void Dispose()
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
            }
        }
    }
}
