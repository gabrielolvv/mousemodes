using System;
using System.Globalization;

namespace MouseModes
{
    /// <summary>Um grupo de jogos que gira o mesmo ângulo por contagem do mouse com a mesma sensibilidade.</summary>
    public sealed class GameYaw
    {
        public GameYaw(string name, double yaw)
        {
            Name = name;
            Yaw = yaw;
        }

        /// <summary>Nome curto, como aparece na tela (ex.: "CS2 · Apex").</summary>
        public string Name { get; private set; }

        /// <summary>Graus girados por contagem do mouse com sensibilidade 1 (o "m_yaw" dos jogos da Source).</summary>
        public double Yaw { get; private set; }
    }

    /// <summary>
    /// Conversor de sensibilidade entre jogos: mesma distância de mouse para uma volta completa (cm/360).
    /// Vale para a mira sem zoom em jogos com raw input, que ignoram a velocidade do ponteiro do Windows.
    /// </summary>
    public static class Sensitivity
    {
        /// <summary>
        /// Só jogos em que a sensibilidade é linear e o yaw é conhecido e estável entre versões.
        /// Para outro jogo, a tela aceita o yaw dele (costuma estar no arquivo de configuração ou no fórum do jogo).
        /// </summary>
        public static readonly GameYaw[] Games =
        {
            new GameYaw("CS2 · Apex · TF2", 0.022),
            new GameYaw("Valorant", 0.07),
            new GameYaw("Overwatch 2 · Call of Duty", 0.0066)
        };

        /// <summary>Centímetros de mouse para girar 360° no jogo.</summary>
        public static double Cm360(double sensitivity, double yaw, int dpi)
        {
            if (sensitivity <= 0 || yaw <= 0 || dpi <= 0) throw new ArgumentOutOfRangeException();
            return 360.0 / (sensitivity * yaw * dpi) * 2.54;
        }

        /// <summary>Sensibilidade no outro jogo (e com outro DPI) para manter o mesmo cm/360.</summary>
        public static double Convert(double sensitivity, double yawFrom, int dpiFrom, double yawTo, int dpiTo)
        {
            if (sensitivity <= 0 || yawFrom <= 0 || dpiFrom <= 0 || yawTo <= 0 || dpiTo <= 0)
                throw new ArgumentOutOfRangeException();
            return sensitivity * yawFrom * dpiFrom / (yawTo * dpiTo);
        }

        /// <summary>Lê "1.25" ou "1,25" (o teclado brasileiro usa vírgula; os jogos, ponto). false se não for positivo.</summary>
        public static bool TryParse(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string normalized = text.Trim().Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   && value > 0 && !double.IsInfinity(value);
        }

        /// <summary>Como digitar no jogo: com ponto e as casas que fazem diferença (3 abaixo de 10, 2 acima).</summary>
        public static string Format(double sensitivity)
        {
            return sensitivity.ToString(sensitivity < 10 ? "0.###" : "0.##", CultureInfo.InvariantCulture);
        }
    }
}
