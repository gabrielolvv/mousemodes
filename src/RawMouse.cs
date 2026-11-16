using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>
    /// Leitura direta do mouse via Raw Input (o mesmo caminho que os jogos usam).
    ///
    /// O Windows guarda um registro de Raw Input por processo, não por janela: se cada tela se registrasse, a última a
    /// abrir roubaria o mouse das outras, e fechar uma desligaria todas. Por isso uma janela invisível só recebe o mouse
    /// e entrega cada leitura a todas as telas abertas (medidor de polling, teste de clique, DPI real).
    ///
    /// A janela invisível roda numa thread própria, de prioridade alta, e marca a hora de cada leitura assim que o
    /// Windows a entrega. Na thread da tela, uma pausa (a checagem de jogos, um redesenho) juntava as leituras e
    /// inflava a variação do polling ou acusava clique duplo falso. As telas continuam recebendo na thread delas, em
    /// lotes, cada leitura com a hora em que chegou.
    /// </summary>
    public sealed class RawMouse : IDisposable
    {
        private const int WM_INPUT = 0x00FF;
        private const int WM_APP_STOP = 0x8000 + 0x4D4D; // WM_APP + "MM": a thread do mouse fecha a janela e sai
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

        /// <summary>Uma leitura com a hora em que o Windows a entregou (Stopwatch.GetTimestamp).</summary>
        public delegate void TimedInputHandler(int dx, int dy, ushort buttons, long timestamp);

        private struct Reading
        {
            public int Dx, Dy;
            public ushort Buttons;
            public long Timestamp;
        }

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

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>Tempo para a thread do mouse criar a janela e registrar o mouse, ou fechar.</summary>
        private static readonly TimeSpan ThreadWait = TimeSpan.FromSeconds(5);

        private static readonly uint HeaderSize = (uint)(8 + 2 * IntPtr.Size);
        private static readonly object Gate = new object(); // a lista é lida na thread do mouse e mudada na da tela
        private static readonly List<RawMouse> listeners = new List<RawMouse>();
        private static InputThread pump;

        private Form owner;
        private IntPtr ownerHandle; // guardado no registro: a thread do mouse não pode ler Form.Handle
        private bool sink;
        private TimedInputHandler handler;
        private readonly List<Reading> pending = new List<Reading>();
        private bool drainPosted;

        /// <summary>
        /// Começa a entregar as leituras do mouse para <paramref name="onInput"/>. sink = recebe mesmo com a tela atrás
        /// de outra; sem sink, só enquanto <paramref name="form"/> estiver em primeiro plano.
        /// </summary>
        public bool Register(Form form, bool sink, InputHandler onInput, out string error)
        {
            InputHandler h = onInput;
            return Register(form, sink, (dx, dy, buttons, timestamp) => h(dx, dy, buttons), out error);
        }

        /// <summary>Como Register, com a hora de cada leitura (para medir intervalos sem as pausas da tela).</summary>
        public bool Register(Form form, bool sink, TimedInputHandler onInput, out string error)
        {
            error = null;
            Unregister();
            lock (Gate)
            {
                if (pump == null)
                {
                    var started = new InputThread();
                    if (!started.Start(out error)) return false;
                    pump = started;
                }
                owner = form;
                ownerHandle = form.IsHandleCreated ? form.Handle : IntPtr.Zero;
                this.sink = sink;
                handler = onInput;
                listeners.Add(this);
            }
            return true;
        }

        /// <summary>Para de entregar a esta tela. Quando a última sair, a thread e a janela invisível também saem.</summary>
        public void Unregister()
        {
            InputThread stopping = null;
            lock (Gate)
            {
                if (!listeners.Remove(this)) return;
                handler = null;
                pending.Clear();
                if (listeners.Count == 0 && pump != null)
                {
                    stopping = pump;
                    pump = null;
                }
            }
            if (stopping != null) stopping.Stop(); // fora do lock: a thread do mouse pode estar esperando por ele
        }

        public void Dispose()
        {
            Unregister();
        }

        /// <summary>Quantas telas estão ouvindo e se a thread do mouse existe (para os testes).</summary>
        internal static int ListenerCount { get { lock (Gate) return listeners.Count; } }
        internal static bool Listening { get { lock (Gate) return pump != null; } }

        /// <summary>Entrega uma leitura a todas as telas na hora, nesta thread (para os testes).</summary>
        internal static void Dispatch(int dx, int dy, ushort buttons)
        {
            long now = Stopwatch.GetTimestamp();
            RawMouse[] copy;
            lock (Gate) copy = listeners.ToArray();
            foreach (RawMouse listener in copy)
                if (listener.Wants()) listener.Invoke(new Reading { Dx = dx, Dy = dy, Buttons = buttons, Timestamp = now });
        }

        /// <summary>
        /// Guarda uma leitura para cada tela e pede à thread dela que entregue o lote (um pedido por lote, não por
        /// leitura: a 8000 Hz seriam 8000 pedidos por segundo). A thread do mouse chama a cada WM_INPUT; os testes também.
        /// </summary>
        internal static void Post(int dx, int dy, ushort buttons, long timestamp)
        {
            var reading = new Reading { Dx = dx, Dy = dy, Buttons = buttons, Timestamp = timestamp };
            lock (Gate)
            {
                foreach (RawMouse listener in listeners)
                {
                    if (!listener.Wants()) continue;
                    listener.pending.Add(reading);
                    if (listener.drainPosted) continue;
                    listener.drainPosted = true;
                    RawMouse target = listener;
                    try { listener.owner.BeginInvoke((MethodInvoker)target.Drain); }
                    catch (InvalidOperationException) { listener.drainPosted = false; listener.pending.Clear(); } // tela fechando
                }
            }
        }

        /// <summary>Esta tela quer leituras agora (aberta e, sem sink, em primeiro plano).</summary>
        private bool Wants()
        {
            if (handler == null || owner == null || owner.IsDisposed) return false;
            if (sink) return true;
            return ownerHandle != IntPtr.Zero && GetForegroundWindow() == ownerHandle;
        }

        /// <summary>Na thread da tela: entrega o lote guardado, na ordem em que chegou.</summary>
        private void Drain()
        {
            Reading[] batch;
            lock (Gate)
            {
                drainPosted = false;
                batch = pending.ToArray();
                pending.Clear();
            }
            foreach (Reading r in batch) Invoke(r);
        }

        private void Invoke(Reading r)
        {
            TimedInputHandler h = handler;
            if (h != null) h(r.Dx, r.Dy, r.Buttons, r.Timestamp);
        }

        /// <summary>A thread do mouse: cria a janela invisível, roda o laço de mensagens dela e fecha quando pedido.</summary>
        private sealed class InputThread
        {
            private readonly ManualResetEvent ready = new ManualResetEvent(false);
            private Thread thread;
            private Receiver window;
            private string error;

            public bool Start(out string startError)
            {
                thread = new Thread(Run) { IsBackground = true, Name = "MouseModes Raw Input", Priority = ThreadPriority.Highest };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                if (!ready.WaitOne(ThreadWait))
                {
                    startError = L.T("a leitura do mouse não começou a tempo", "mouse reading did not start in time");
                    return false;
                }
                startError = error;
                return error == null;
            }

            private void Run()
            {
                try
                {
                    window = new Receiver();
                    string listenError;
                    if (!window.Listen(out listenError))
                    {
                        error = listenError;
                        window.DestroyHandle();
                        window = null;
                        ready.Set();
                        return;
                    }
                    ready.Set();
                    Application.Run(); // até a janela pedir ExitThread
                }
                catch (Exception ex)
                {
                    if (error == null) error = ex.Message;
                    ready.Set();
                }
            }

            public void Stop()
            {
                Receiver w = window;
                if (w != null && w.Handle != IntPtr.Zero) PostMessage(w.Handle, WM_APP_STOP, IntPtr.Zero, IntPtr.Zero);
                if (thread != null && !thread.Join(ThreadWait))
                    Log.Warn(L.T("A leitura do mouse não fechou a tempo", "Mouse reading did not close in time"));
                ready.Close();
            }
        }

        /// <summary>Janela só de mensagens (sem tela) que recebe o WM_INPUT do processo inteiro, na thread do mouse.</summary>
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

            private void Stop()
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
                // Erro aqui não pode subir: viraria a caixa de "Erro inesperado" a cada movimento do mouse.
                try
                {
                    if (m.Msg == WM_INPUT)
                    {
                        long now = Stopwatch.GetTimestamp(); // a hora antes de qualquer outra coisa
                        int dx, dy;
                        ushort buttons;
                        if (Read(m.LParam, out dx, out dy, out buttons)) Post(dx, dy, buttons, now);
                    }
                    else if (m.Msg == WM_APP_STOP)
                    {
                        Stop();
                        Application.ExitThread();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(L.T("Leitura do mouse", "Mouse reading"), ex);
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
