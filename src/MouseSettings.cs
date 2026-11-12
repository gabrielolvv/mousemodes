using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MouseModes
{
    /// <summary>Lê e grava as configurações de mouse do Windows (as mesmas do Painel de Controle).</summary>
    public static class MouseSettings
    {
        private const uint SPI_GETMOUSE = 0x0003;
        private const uint SPI_SETMOUSE = 0x0004;
        private const uint SPI_GETWHEELSCROLLLINES = 0x0068;
        private const uint SPI_SETWHEELSCROLLLINES = 0x0069;
        private const uint SPI_GETMOUSESPEED = 0x0070;
        private const uint SPI_SETMOUSESPEED = 0x0071;

        // "Uma tela por vez" no Painel de Controle: o Windows guarda 0xFFFFFFFF no lugar das linhas.
        private const uint WHEEL_PAGESCROLL = 0xFFFFFFFF;

        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        private static extern bool SpiPtr(uint action, uint uiParam, IntPtr pvParam, uint winIni);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        private static extern bool SpiArray(uint action, uint uiParam, [In, Out] int[] pvParam, uint winIni);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        private static extern bool SpiRefInt(uint action, uint uiParam, ref int pvParam, uint winIni);

        public static MouseProfile Read()
        {
            var p = new MouseProfile();

            int speed = 10;
            if (SpiRefInt(SPI_GETMOUSESPEED, 0, ref speed, 0) && speed >= 1 && speed <= 20)
                p.PointerSpeed = speed;

            var accel = new int[3];
            if (SpiArray(SPI_GETMOUSE, 0, accel, 0))
                p.EnhancePointerPrecision = accel[2] != 0;

            int lines = 3;
            if (SpiRefInt(SPI_GETWHEELSCROLLLINES, 0, ref lines, 0))
            {
                int? read = ScrollFromWindows(lines);
                if (read != null) p.ScrollLines = read.Value;
            }

            return p;
        }

        public static void Apply(MouseProfile p)
        {
            Apply(p, true);
        }

        /// <param name="persist">
        /// false = só nesta sessão, sem gravar no registro nem avisar as outras janelas. É a troca rápida
        /// a cada alt-tab: não engasga o jogo, e o registro continua com o modo aplicado de verdade.
        /// </param>
        public static void Apply(MouseProfile p, bool persist)
        {
            uint flags = persist ? SPIF_UPDATEINIFILE | SPIF_SENDCHANGE : 0;

            int speed = Math.Max(1, Math.Min(20, p.PointerSpeed));
            Check(SpiPtr(SPI_SETMOUSESPEED, 0, new IntPtr(speed), flags), L.T("a velocidade do ponteiro", "the pointer speed"));

            // Valores padrão do Windows quando a precisão está ligada: 6, 10, 1.
            int[] accel = p.EnhancePointerPrecision ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 };
            Check(SpiArray(SPI_SETMOUSE, 0, accel, flags), L.T("a precisão do ponteiro", "the pointer precision"));

            Check(SpiPtr(SPI_SETWHEELSCROLLLINES, ScrollToWindows(p.ScrollLines), IntPtr.Zero, flags), L.T("as linhas de rolagem", "the scroll lines"));
        }

        /// <summary>
        /// Linhas lidas do Windows → ScrollLines do perfil. "Uma tela por vez" vira MouseProfile.PageScroll;
        /// fora de 1–100 (ou a tela por vez) é null: fica o valor que o perfil já tinha.
        /// </summary>
        internal static int? ScrollFromWindows(int raw)
        {
            if (unchecked((uint)raw) == WHEEL_PAGESCROLL) return MouseProfile.PageScroll;
            return raw >= 1 && raw <= 100 ? raw : (int?)null;
        }

        /// <summary>ScrollLines do perfil → o que o Windows grava. A tela por vez continua tela por vez.</summary>
        internal static uint ScrollToWindows(int lines)
        {
            if (lines == MouseProfile.PageScroll) return WHEEL_PAGESCROLL;
            return (uint)Math.Max(1, Math.Min(100, lines));
        }

        /// <summary>Texto da rolagem para o log e o Diagnóstico ("3" ou "uma tela").</summary>
        public static string DescribeScroll(int lines)
        {
            return lines == MouseProfile.PageScroll ? L.T("uma tela", "one screen") : lines.ToString();
        }

        private static void Check(bool ok, string what)
        {
            if (!ok)
                throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Não foi possível ajustar ", "Could not set ") + what + ".");
        }
    }
}
