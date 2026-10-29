using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>Conversor de sensibilidade: a mesma mira (cm/360) em outro jogo ou com outro DPI.</summary>
    public class SensitivityForm : GamerWindow
    {
        private const int OtherIndex = 3; // "OUTRO": o yaw vem do campo

        private readonly AppConfig cfg;
        private readonly Action save;
        private readonly Segmented game;
        private readonly TextBox sensBox;
        private readonly TextBox dpiBox;
        private readonly TextBox newDpiBox;
        private readonly TextBox yawBox;
        private readonly BigReadout readout;
        private readonly List<Label[]> rows = new List<Label[]>();
        private readonly Label hint;

        /// <param name="save">Grava o config (a tela lembra o jogo, a sensibilidade e o DPI da última vez).</param>
        public SensitivityForm(AppConfig cfg, Action save) : base(L.T("Conversor de sensibilidade", "Sensitivity converter"), true)
        {
            this.cfg = cfg;
            this.save = save;
            CloseOnEscape = true;
            ClientSize = new Size(480, 580);
            TitleBar.AccentA = Theme.PerfA;
            TitleBar.AccentB = Theme.PerfB;

            Controls.Add(new SectionLabel { Text = L.T("Seu jogo agora", "Your game now"), Location = new Point(20, 56), Size = new Size(440, 20) });
            game = new Segmented
            {
                Options = new[] { "CS2 · APEX", "VALORANT", "OW2 · COD", L.T("OUTRO", "OTHER") },
                Location = new Point(20, 80),
                Size = new Size(440, 40),
                AccentA = Theme.PerfA,
                AccentB = Theme.PerfB
            };
            Controls.Add(game);

            sensBox = AddField(L.T("SENSIBILIDADE", "SENSITIVITY"), 20, 7);
            dpiBox = AddField("DPI", 132, 5);
            newDpiBox = AddField(L.T("DPI NOVO", "NEW DPI"), 244, 5);
            yawBox = AddField(L.T("YAW (OUTRO)", "YAW (OTHER)"), 356, 9);
            foreach (TextBox box in new[] { dpiBox, newDpiBox })
                box.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };

            readout = new BigReadout { Location = new Point(12, 202), Size = new Size(456, 150), Unit = "cm/360", AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            Controls.Add(readout);

            Controls.Add(new SectionLabel { Text = L.T("A mesma mira em cada jogo", "The same aim in each game"), Location = new Point(20, 362), Size = new Size(440, 20) });
            int y = 388;
            for (int i = 0; i <= Sensitivity.Games.Length; i++)
            {
                var name = Note("", 20, y, 300, 26, Theme.Text, 10f);
                name.TextAlign = ContentAlignment.MiddleLeft;
                var value = Note("", 320, y, 140, 26, Theme.PerfB, 12f);
                value.Font = Theme.Ui(12f, FontStyle.Bold);
                value.TextAlign = ContentAlignment.MiddleRight;
                Controls.Add(name);
                Controls.Add(value);
                rows.Add(new[] { name, value });
                y += 30;
            }

            hint = Note("", 20, 512, 440, 54, Theme.Faint, 8.5f);
            Controls.Add(hint);

            // Última conversa: jogo, sensibilidade e yaw daqui; o DPI é o mesmo do teste de mira.
            game.SelectedIndex = cfg.ConverterGame >= 0 && cfg.ConverterGame <= OtherIndex ? cfg.ConverterGame : 0;
            sensBox.Text = cfg.ConverterSensitivity ?? "";
            yawBox.Text = cfg.ConverterYaw ?? "";
            dpiBox.Text = (cfg.AimTestDpi > 0 ? cfg.AimTestDpi : 800).ToString();
            newDpiBox.Text = dpiBox.Text;

            game.SelectedIndexChanged += Recalculate;
            foreach (TextBox box in new[] { sensBox, dpiBox, yawBox, newDpiBox })
                box.TextChanged += (s, e) => Recalculate();
            dpiBox.TextChanged += (s, e) => { if (!newDpiBox.Focused) newDpiBox.Text = dpiBox.Text; };

            FinishLayout();
            Recalculate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            sensBox.Focus();
            sensBox.SelectAll();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            cfg.ConverterGame = game.SelectedIndex;
            cfg.ConverterSensitivity = sensBox.Text.Trim();
            cfg.ConverterYaw = yawBox.Text.Trim();
            int dpi;
            if (int.TryParse(dpiBox.Text, out dpi) && dpi > 0) cfg.AimTestDpi = dpi;
            if (save != null) save();
            base.OnFormClosed(e);
        }

        private TextBox AddField(string label, int x, int maxLength)
        {
            Controls.Add(Note(label, x, 128, 104, 18, Theme.Faint, 8f));
            var box = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Ui(12f), TextAlign = HorizontalAlignment.Center, MaxLength = maxLength };
            Controls.Add(new FieldHost(box) { Location = new Point(x, 148), Size = new Size(104, 40), Accent = Theme.PerfB });
            return box;
        }

        private void Recalculate()
        {
            double otherYaw;
            bool hasOther = Sensitivity.TryParse(yawBox.Text, out otherYaw);
            bool fromOther = game.SelectedIndex == OtherIndex;

            double sens = 0;
            int dpi = 0, newDpi;
            bool ok = Sensitivity.TryParse(sensBox.Text, out sens)
                      && int.TryParse(dpiBox.Text, out dpi) && dpi > 0
                      && (!fromOther || hasOther);

            if (!ok)
            {
                readout.Value = "—";
                readout.Caption = fromOther && !hasOther
                    ? L.T("informe o yaw do seu jogo", "enter your game's yaw")
                    : L.T("digite a sensibilidade que você usa", "type the sensitivity you use");
                foreach (Label[] row in rows) { row[0].Text = ""; row[1].Text = ""; }
                hint.Text = Hint();
                readout.Invalidate();
                return;
            }

            if (!int.TryParse(newDpiBox.Text, out newDpi) || newDpi <= 0) newDpi = dpi; // vazio: o mesmo DPI
            double yaw = fromOther ? otherYaw : Sensitivity.Games[game.SelectedIndex].Yaw;
            double cm = Sensitivity.Cm360(sens, yaw, dpi);

            // Distância na língua da tela (43,3 cm / 43.3 cm); a sensibilidade sempre com ponto, como nos jogos.
            CultureInfo culture = L.IsEnglish ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("pt-BR");
            readout.Value = cm.ToString(cm < 100 ? "0.0" : "0", culture);
            readout.Caption = L.T("uma volta completa no jogo · ", "one full turn in the game · ") +
                              (cm / 2.54).ToString(cm < 254 ? "0.0" : "0", culture) + L.T(" pol", " in");

            for (int i = 0; i < rows.Count; i++)
            {
                Label[] row = rows[i];
                bool isOther = i == Sensitivity.Games.Length;
                if (isOther && !hasOther)
                {
                    row[0].Text = L.T("Outro jogo: informe o yaw", "Other game: enter its yaw");
                    row[0].ForeColor = Theme.Faint;
                    row[1].Text = "";
                    continue;
                }
                double targetYaw = isOther ? otherYaw : Sensitivity.Games[i].Yaw;
                bool mine = isOther ? fromOther : i == game.SelectedIndex;
                row[0].Text = (isOther ? L.T("Outro jogo (yaw ", "Other game (yaw ") + yawBox.Text.Trim() + ")" : Sensitivity.Games[i].Name) +
                              (mine && newDpi == dpi ? L.T("  · seu jogo", "  · your game") : "");
                row[0].ForeColor = Theme.Text;
                row[1].Text = Sensitivity.Format(Sensitivity.Convert(sens, yaw, dpi, targetYaw, newDpi));
            }
            hint.Text = Hint() + (newDpi != dpi
                ? L.T(" Com " + newDpi + " DPI, use os valores acima para a mira não mudar.", " At " + newDpi + " DPI, use the values above so your aim does not change.")
                : "");
            readout.Invalidate();
        }

        private static string Hint()
        {
            return L.T("Vale para a mira sem zoom em jogos com raw input (a velocidade do Windows não conta). " +
                       "Para outro jogo, informe o yaw dele: costuma estar no fórum ou no arquivo de configuração.",
                       "Applies to unscoped aim in games with raw input (the Windows speed does not count). " +
                       "For another game, enter its yaw: it is usually on the game's forum or in its config file.");
        }
    }
}
