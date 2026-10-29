using System;
using System.Globalization;

namespace MouseModes
{
    /// <summary>Quem troca a resolução de verdade (o monitor principal). Os testes usam um de mentira.</summary>
    public interface IScreenModes
    {
        /// <summary>Põe o monitor principal nesta resolução, na maior taxa que ele aceita nela.</summary>
        bool Set(int width, int height, out int hz, out string error);

        /// <summary>Volta à resolução salva no Windows (a de sempre).</summary>
        void Reset();
    }

    /// <summary>
    /// Resolução por jogo: com um jogo da lista aberto no Performance, o monitor principal vai para a resolução do
    /// perfil dele (ex.: 1280×960 esticado no CS2); ao fechar o jogo, sair do Performance ou fechar o app, volta.
    /// A troca é temporária (não vai para o registro do Windows): voltar é pedir a resolução salva, e reiniciar o PC
    /// também desfaz. Se o app cair com a resolução trocada, desfaz ao abrir de novo.
    /// </summary>
    public class GameResolution
    {
        /// <summary>As opções da tela de Jogos: as mais usadas para 4:3 e 5:4 esticado. null = não muda.</summary>
        public static readonly string[] Options = { null, "1280x960", "1440x1080", "1024x768", "1280x1024" };

        private readonly AppConfig cfg;
        private readonly Action save;
        private readonly IScreenModes screen;
        private string applied;   // a que está na tela agora (null = a de sempre)
        private string failed;    // a que o monitor recusou: não tenta de novo a cada 3 s

        public GameResolution(AppConfig cfg, Action save = null, IScreenModes screen = null)
        {
            this.cfg = cfg;
            this.save = save ?? cfg.Save;
            this.screen = screen ?? new PrimaryScreenModes();
        }

        public string Applied { get { return applied; } }

        /// <summary>"1280x960" ou "1280×960" → 1280 e 960. false se não for uma resolução.</summary>
        public static bool TryParse(string text, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Trim().ToLowerInvariant().Replace('×', 'x').Split('x');
            return parts.Length == 2 &&
                   int.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out width) &&
                   int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out height) &&
                   width >= 640 && height >= 480 && width <= 16384 && height <= 16384;
        }

        /// <summary>"1280×960" para a tela.</summary>
        public static string Format(string resolution)
        {
            int w, h;
            return TryParse(resolution, out w, out h) ? w + "×" + h : resolution;
        }

        /// <summary>
        /// Deixa a tela na resolução pedida (null = a de sempre). Chamado a cada checagem de jogos e ao trocar de modo;
        /// só mexe no monitor quando o pedido muda.
        /// </summary>
        public void Ensure(string wanted)
        {
            int width, height;
            if (!TryParse(wanted, out width, out height)) wanted = null;
            else wanted = width + "x" + height;

            // Sem pedido (o jogo fechou): esquece a recusa, para tentar de novo na próxima partida.
            if (wanted == null) failed = null;
            if (wanted == applied) return;
            if (wanted == null)
            {
                Restore();
                return;
            }
            if (wanted == failed) return;

            int hz;
            string error;
            if (!screen.Set(width, height, out hz, out error))
            {
                failed = wanted;
                Log.Warn(L.T("Resolução ", "Resolution ") + Format(wanted) + L.T(" não aplicada: ", " not applied: ") + error);
                return;
            }
            applied = wanted;
            failed = null;
            cfg.ResolutionChanged = true;
            Save(save);
            Log.Info(L.T("Resolução do jogo: ", "Game resolution: ") + Format(wanted) + L.T(" a ", " at ") + hz + " Hz");
        }

        /// <summary>Volta à resolução de sempre, se ela foi trocada pelo app.</summary>
        public void Restore()
        {
            if (applied == null && !cfg.ResolutionChanged) return;
            screen.Reset();
            Log.Info(L.T("Resolução de volta à de sempre (era ", "Resolution back to the usual one (was ") + Format(applied ?? "?") + ")");
            applied = null;
            cfg.ResolutionChanged = false;
            Save(save);
        }

        /// <summary>O app caiu com a resolução de um jogo na tela: volta à de sempre.</summary>
        public static void Recover(AppConfig cfg, Action save = null, IScreenModes screen = null)
        {
            if (!cfg.ResolutionChanged) return;
            (screen ?? new PrimaryScreenModes()).Reset();
            Log.Warn(L.T("O app foi fechado com a resolução de um jogo; voltou à de sempre",
                         "The app closed with a game's resolution on; went back to the usual one"));
            cfg.ResolutionChanged = false;
            Save(save ?? cfg.Save);
        }

        private static void Save(Action save)
        {
            try { save(); } catch (Exception ex) { Log.Error(L.T("Salvar configuração", "Save settings"), ex); }
        }
    }

    /// <summary>O monitor principal de verdade, pelo ChangeDisplaySettingsEx.</summary>
    internal sealed class PrimaryScreenModes : IScreenModes
    {
        public bool Set(int width, int height, out int hz, out string error)
        {
            return Displays.SetPrimaryResolution(width, height, out hz, out error);
        }

        public void Reset()
        {
            Displays.ResetPrimary();
        }
    }
}
