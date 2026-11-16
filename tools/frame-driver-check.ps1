param([string]$Exe = (Join-Path $env:LOCALAPPDATA 'Programs\MouseModes\MouseModes.exe'), [int]$Seconds = 15, [int]$Fps = 144,
      [switch]$OnlyWindow)
# Teste do Caça-travadinhas em Vulkan e OpenGL (Stutter finder test for Vulkan and OpenGL).
#
# Abre uma janela OpenGL que desenha a 144 FPS (-Fps 0 = sem limite) por 15 s e lê os quadros com o leitor do app, a
# mesma sessão de rastreamento do Windows da partida. No fim mostra, por processo, quantos quadros vieram do DirectX e
# de cada evento do driver de vídeo e qual lista o app usaria. Deixe um jogo em Vulkan ou OpenGL aberto em janela
# durante o teste para ele também entrar na conta. Só lê: não muda nada no Windows e a sessão fecha no fim.
# Com o MouseModes fechado ou fora de partida (a sessão tem o mesmo nome), rode no PowerShell como administrador:
#   powershell -ExecutionPolicy Bypass -File .\frame-driver-check.ps1
# e mande o texto que aparecer (ele também fica em frame-driver-check.txt, na Área de Trabalho).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

public static class FrameCheck
{
    [StructLayout(LayoutKind.Sequential)]
    struct Pfd
    {
        public ushort nSize, nVersion; public uint dwFlags; public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits,
        cGreenShift, cBlueBits, cBlueShift, cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits,
        cAccumAlphaBits, cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved; public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern int ChoosePixelFormat(IntPtr hdc, ref Pfd pfd);
    [DllImport("gdi32.dll")] static extern bool SetPixelFormat(IntPtr hdc, int format, ref Pfd pfd);
    [DllImport("gdi32.dll")] static extern bool SwapBuffers(IntPtr hdc);
    [DllImport("opengl32.dll")] static extern IntPtr wglCreateContext(IntPtr hdc);
    [DllImport("opengl32.dll")] static extern bool wglMakeCurrent(IntPtr hdc, IntPtr ctx);
    [DllImport("opengl32.dll")] static extern bool wglDeleteContext(IntPtr ctx);
    [DllImport("opengl32.dll")] static extern void glClearColor(float r, float g, float b, float a);
    [DllImport("opengl32.dll")] static extern void glClear(uint mask);
    [DllImport("opengl32.dll")] static extern IntPtr glGetString(uint name);

    public static string Run(string exe, int seconds, int fps, bool onlyWindow)
    {
        var sb = new StringBuilder();
        Assembly asm = Assembly.LoadFrom(exe);
        BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        Type probeType = asm.GetType("MouseModes.FrameProbe");
        if (probeType.GetProperty("DriverError") == null)
            return "esta versão do MouseModes ainda não lê Vulkan/OpenGL / this MouseModes version does not read Vulkan/OpenGL yet";
        object[] startArgs = { null };
        object probe = onlyWindow ? null : probeType.GetMethod("TryStart").Invoke(null, startArgs);
        if (probe == null && !onlyWindow) return "a leitura dos quadros não abriu: " + startArgs[0] + " (rode como administrador / run as administrator)";
        object driverError = onlyWindow ? "só a janela" : probeType.GetProperty("DriverError").GetValue(probe, null);
        sb.AppendLine("driver de vídeo: " + (driverError == null ? "lido" : "NÃO lido (" + driverError + ")"));

        var form = new Form { Text = "MouseModes - teste OpenGL", Width = 640, Height = 360, StartPosition = FormStartPosition.CenterScreen };
        form.Show();
        IntPtr hdc = GetDC(form.Handle);
        var pfd = new Pfd { nSize = (ushort)Marshal.SizeOf(typeof(Pfd)), nVersion = 1, dwFlags = 0x4 | 0x20 | 0x1, cColorBits = 32, cDepthBits = 24 };
        int format = ChoosePixelFormat(hdc, ref pfd);
        SetPixelFormat(hdc, format, ref pfd);
        IntPtr ctx = wglCreateContext(hdc);
        wglMakeCurrent(hdc, ctx);
        sb.AppendLine("OpenGL: " + Marshal.PtrToStringAnsi(glGetString(0x1F01)) + " / " + Marshal.PtrToStringAnsi(glGetString(0x1F02)));
        int myPid = Process.GetCurrentProcess().Id;
        sb.AppendLine("a janela OpenGL é deste processo: pid " + myPid);

        var watch = Stopwatch.StartNew();
        int swaps = 0;
        MethodInfo refresh = probeType.GetMethod("RefreshNames");
        double nextRefresh = 3;
        while (watch.Elapsed.TotalSeconds < seconds && form.Visible)
        {
            float k = (float)(watch.Elapsed.TotalSeconds % 1.0);
            glClearColor(k, 0.2f, 1 - k, 1);
            glClear(0x4000);
            SwapBuffers(hdc);
            swaps++;
            if (fps > 0)
            {
                double next = swaps / (double)fps;
                while (watch.Elapsed.TotalSeconds < next)
                {
                    if (next - watch.Elapsed.TotalSeconds > 0.002) System.Threading.Thread.Sleep(1);
                }
            }
            Application.DoEvents();
            if (!onlyWindow && watch.Elapsed.TotalSeconds >= nextRefresh) { refresh.Invoke(probe, new object[] { null }); nextRefresh += 3; }
        }
        double elapsed = watch.Elapsed.TotalSeconds;
        sb.AppendLine(string.Format("SwapBuffers chamado {0} vezes em {1:0.0} s ({2:0} por segundo)", swaps, elapsed, swaps / elapsed));
        if (onlyWindow)
        {
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            wglDeleteContext(ctx);
            ReleaseDC(form.Handle, hdc);
            form.Close();
            return sb.ToString();
        }

        // Antes de parar: quantos quadros cada fonte viu, por processo (para conferir a escolha).
        var names = (Dictionary<int, string>)probeType.GetProperty("Names").GetValue(probe, null);
        var tracks = (IDictionary)probeType.GetField("tracks", all).GetValue(probe);
        var counts = new Dictionary<int, int[]>();
        lock (probeType.GetField("gate", all).GetValue(probe))
            foreach (DictionaryEntry e in tracks)
            {
                var arr = (Array)e.Value;
                var c = new int[arr.Length];
                for (int s = 0; s < arr.Length; s++)
                {
                    object track = arr.GetValue(s);
                    if (track == null) continue;
                    var frames = (ICollection)track.GetType().GetField("Frames").GetValue(track);
                    c[s] = frames == null ? 0 : frames.Count;
                }
                counts[(int)e.Key] = c;
            }
        HashSet<int> fromDriver = null;
        object[] stopArgs = { null };
        MethodInfo stop = null;
        foreach (MethodInfo m in probeType.GetMethods()) if (m.Name == "Stop" && m.GetParameters().Length == 1) stop = m;
        var chosen = (IDictionary)stop.Invoke(probe, stopArgs);
        fromDriver = (HashSet<int>)stopArgs[0];

        wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        wglDeleteContext(ctx);
        ReleaseDC(form.Handle, hdc);
        form.Close();

        sb.AppendLine();
        sb.AppendLine("processo (pid): DirectX / driver PresentHistory / driver Flip / driver FlipMPO -> escolhida, mediana");
        var pids = new List<int>(counts.Keys);
        pids.Sort((a, b) => Max(counts[b]).CompareTo(Max(counts[a])));
        foreach (int pid in pids)
        {
            int[] c = counts[pid];
            if (Max(c) == 0) continue;
            string name; names.TryGetValue(pid, out name);
            string pick = "-";
            string median = "";
            if (chosen.Contains(pid))
            {
                pick = fromDriver.Contains(pid) ? "driver" : "DirectX";
                var list = new List<float>();
                foreach (object f in (IEnumerable)chosen[pid]) list.Add((float)f.GetType().GetField("Ms").GetValue(f));
                list.Sort();
                if (list.Count > 0) median = string.Format(", {0:0.00} ms (~{1:0} FPS)", list[list.Count / 2], 1000 / list[list.Count / 2]);
            }
            sb.AppendLine(string.Format("  {0} ({1}){2}: {3} / {4} / {5} / {6} -> {7}{8}", name ?? "?", pid, pid == myPid ? " [janela OpenGL]" : "",
                                        c[0], c[1], c[2], c[3], pick, median));
        }
        return sb.ToString();
    }

    static int Max(int[] c) { int m = 0; foreach (int v in c) if (v > m) m = v; return m; }
}
'@
$lines = @()
function Say([string]$text) { $script:lines += $text; Write-Host $text }
try {
  $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
  Say ("MouseModes " + [Reflection.AssemblyName]::GetAssemblyName($Exe).Version + " · admin: " +
       $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) + " · 64 bits: " + [Environment]::Is64BitProcess)
  Get-CimInstance Win32_VideoController | ForEach-Object { Say ("placa / card: " + $_.Name + " (driver " + $_.DriverVersion + ")") }
  Say ([FrameCheck]::Run((Resolve-Path $Exe).Path, $Seconds, $Fps, $OnlyWindow.IsPresent))
}
catch { Say ("erro / error: " + $_.Exception.ToString()) }
$out = Join-Path ([Environment]::GetFolderPath('Desktop')) 'frame-driver-check.txt'
$lines | Out-File -Encoding utf8 $out
Write-Host ("salvo em / saved to: " + $out)
