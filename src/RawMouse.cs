using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>
    /// Leitura direta do mouse via Raw Input (o mesmo caminho que os jogos usam).
    ///
    /// O Windows guarda um registro de Raw Input por processo, não por janela: se cada tela se registrasse, a última a
    /// abrir roubaria o mouse das outras, e fechar uma desligaria todas. Por isso uma janela invisível só recebe o mouse
    /// e entrega cada leitura a todas as telas abertas (medidor de polling, teste de clique, DPI real).
    /// </summary>
    public sealed class RawMouse : IDisposable
    {
        private const int WM_INPUT = 0x00FF;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIM_TYPEMOUSE = 0;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_REMOVE = 0x00000001;
        private const ushort MOUSE_MOVE_ABSOLUTE = 0x0001;

        // Bits de usButtonFlags
        public const ushort LeftDown = 0x0001, LeftUp = 0x0002;
        public const ushort RightDown = 0x0004, RightUp = 0x0008;
        public const ushort MiddleDown = 0x0010, MiddleUp = 0x0020;
        public const ushort Button4Down = 0x0040, Button4Up = 0x0080;
        public const ushort Button5Down = 0x0100, Button5Up = 0x0200;

        /// <summary>Uma leitura: movimento relativo em contagens do sensor e os bits de botão.</summary>
        public delegate void InputHandler(int dx, int dy, ushort buttons);

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

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private static readonly uint HeaderSize = (uint)(8 + 2 * IntPtr.Size);
        private static readonly List<RawMouse> listeners = new List<RawMouse>();
        private static Receiver receiver;

        private Form owner;
        private bool sink;
        private InputHandler handler;

        /// <summary>
        /// Começa a entregar as leituras do mouse para <paramref name="onInput"/>. sink = recebe mesmo com a tela atrás
        /// de outra; sem sink, só enquanto <paramref name="form"/> estiver em primeiro plano.
        /// </summary>
        public bool Register(Form form, bool sink, InputHandler onInput, out string error)
        {
            error = null;
            Unregister();
            if (receiver == null)
            {
                try
                {
                    var created = new Receiver();
                    if (!created.Listen(out error))
                    {
                        created.DestroyHandle();
                        return false;
                    }
                    receiver = created;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
            owner = form;
            this.sink = sink;
            handler = onInput;
            listeners.Add(this);
            return true;
        }

        /// <summary>Para de entregar a esta tela. Quando a última sair, a janela invisível também sai.</summary>
        public void Unregister()
        {
            if (!listeners.Remove(this)) return;
            handler = null;
            if (listeners.Count > 0 || receiver == null) return;
            receiver.Stop();
            receiver = null;
        }

        public void Dispose()
        {
            Unregister();
        }

        /// <summary>Quantas telas estão ouvindo e se a janela invisível existe (para os testes).</summary>
        internal static int ListenerCount { get { return listeners.Count; } }
        internal static bool Listening { get { return receiver != null; } }

        /// <summary>Entrega uma leitura a todas as telas (a janela invisível chama a cada WM_INPUT; os testes também).</summary>
        internal static void Dispatch(int dx, int dy, ushort buttons)
        {
            foreach (RawMouse listener in listeners.ToArray()) listener.Deliver(dx, dy, buttons);
        }

        private void Deliver(int dx, int dy, ushort buttons)
        {
            if (handler == null || owner == null || owner.IsDisposed) return;
            if (!sink && (!owner.IsHandleCreated || GetForegroundWindow() != owner.Handle)) return;
            handler(dx, dy, buttons);
        }

        /// <summary>Janela só de mensagens (sem tela) que recebe o WM_INPUT do processo inteiro.</summary>
        private sealed class Receiver : NativeWindow
        {
            private static readonly IntPtr HwndMessage = new IntPtr(-3);
            private IntPtr buffer = IntPtr.Zero;
            private uint bufferSize;

            public Receiver()
            {
                CreateHandle(new CreateParams { Parent = HwndMessage });
            }

            public bool Listen(out string error)
            {
                error = null;
                var device = new RAWINPUTDEVICE
                {
                    usUsagePage = 0x01, // Generic Desktop
                    usUsage = 0x02,     // Mouse
                    dwFlags = RIDEV_INPUTSINK,
                    hwndTarget = Handle
                };
                if (RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)))) return true;
                error = "erro " + Marshal.GetLastWin32Error();
                return false;
            }

            public void Stop()
            {
                try
                {
                    var device = new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero };
                    RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
                }
                catch { }
                DestroyHandle();
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                    buffer = IntPtr.Zero;
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_INPUT)
                {
                    int dx, dy;
                    ushort buttons;
                    if (Read(m.LParam, out dx, out dy, out buttons)) Dispatch(dx, dy, buttons);
                }
                base.WndProc(ref m);
            }

            /// <summary>Lê um WM_INPUT. Retorna false se não for do mouse.</summary>
            private bool Read(IntPtr hRawInput, out int dx, out int dy, out ushort buttonFlags)
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
                ushort moveFlags = (ushort)Marshal.ReadInt16(buffer, b);
                buttonFlags = (ushort)Marshal.ReadInt16(buffer, b + 4);
                // Área de trabalho remota, máquina virtual e mesa digitalizadora mandam a posição na tela (0 a 65535),
                // não contagens do sensor: o movimento não conta, os botões sim.
                if ((moveFlags & MOUSE_MOVE_ABSOLUTE) != 0) return true;
                dx = Marshal.ReadInt32(buffer, b + 12);
                dy = Marshal.ReadInt32(buffer, b + 16);
                return true;
            }
        }
    }
}
