using System;
using System.Collections.Generic;

namespace MouseModes
{
    /// <summary>
    /// DPI real: a pessoa arrasta o mouse uma distância conhecida (numa régua) e o app conta as leituras do sensor
    /// pelo Raw Input. Contagens ÷ polegadas = DPI de verdade, que costuma ficar alguns por cento longe do anunciado.
    /// A velocidade e a aceleração do Windows não entram: o Raw Input entrega as contagens do sensor.
    /// </summary>
    public static class DpiMeter
    {
        public const double CmPerInch = 2.54;

        /// <summary>Menos que isso é arrasto curto demais para medir (ou um clique sem querer).</summary>
        public const long MinCounts = 100;

        /// <summary>Medições que variam mais que isso entre si pedem para conferir régua e linha reta.</summary>
        public const double MaxSpread = 0.05;

        /// <summary>
        /// DPI de um arrasto. Usa o deslocamento em linha reta (não o caminho): o vaivém de lado se anula.
        /// </summary>
        public static double Dpi(long sumX, long sumY, double inches)
        {
            if (inches <= 0) throw new ArgumentOutOfRangeException("inches");
            return Math.Sqrt((double)sumX * sumX + (double)sumY * sumY) / inches;
        }

        public static long Counts(long sumX, long sumY)
        {
            return (long)Math.Round(Math.Sqrt((double)sumX * sumX + (double)sumY * sumY));
        }

        /// <summary>A distância da régua em polegadas ("10" cm ou "4" pol).</summary>
        public static double Inches(double distance, bool inInches)
        {
            return inInches ? distance : distance / CmPerInch;
        }

        public static double Median(IList<double> values)
        {
            if (values == null || values.Count == 0) throw new ArgumentException("values");
            var sorted = new List<double>(values);
            sorted.Sort();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        /// <summary>Quanto as medições variam entre si: (maior − menor) ÷ mediana. 0 com uma medição só.</summary>
        public static double Spread(IList<double> values)
        {
            double min = double.MaxValue, max = double.MinValue;
            foreach (double v in values)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            return values.Count < 2 ? 0 : (max - min) / Median(values);
        }

        /// <summary>Diferença do real para o anunciado: −0,046 = o mouse anda 4,6% menos que o anunciado.</summary>
        public static double Deviation(double measured, int declared)
        {
            if (declared <= 0) throw new ArgumentOutOfRangeException("declared");
            return (measured - declared) / declared;
        }
    }
}
