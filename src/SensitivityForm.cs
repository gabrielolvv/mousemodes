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
        private const int OtherIndex = 3; // "Outro": o yaw vem do campo

        private readonly AppConfig cfg;
        private readonly Action save;
        private readonly Action measureDpi;
        private readonly Segmented game;
        private readonly TextBox sensBox;
        private readonly TextBox dpiBox;
        private readonly TextBox newDpiBox;
        private readonly TextBox yawBox;
        private readonly BigReadout readout;
        private readonly List<Label[]> rows = new List<Label[]>();
        private readonly Label hint;
        private readonly Card results;
        private readonly EmptyHint emptyResults;
        /// <summary>O DPI do config que a caixa está mostrando: outra tela pode mudar o config enquanto esta está aberta.</summary>
        private int shownDpi;

        /// <param name="save">Grava o config (a tela lembra o jogo, a sensibilidade e o DPI da última vez).</param>
        /// <param name="measureDpi">Abre a medição do DPI real.</param>
        public SensitivityForm(AppConfig cfg, Action save, Action measureDpi) : base(L.T("Conversor de sensibilidade", "Sensitivity converter"), true)
        {
            this.cfg = cfg;
            this.save = save;
            this.measureDpi = measureDpi;
            CloseOnEscape = true;
            TitleBar.AccentA = Theme.PerfA;
            TitleBar.AccentB = Theme.PerfB;

            Controls.Add(new SectionLabel { Text = L.T("Seu jogo agora", "Your game now"), Location = new Point(20, 56), Size = new Size(440, 20) });
            game = new Segmented
            {
                Options = new[] { "CS2 · Apex", "Valorant", "OW2 · CoD", L.T("Outro", "Other") },
                Location = new Point(20, 84),
                Size = new Size(440, 36),
                AccentA = Theme.PerfA,
                AccentB = Theme.PerfB
            };
            Controls.Add(game);

            const int fieldsY = 132;
            sensBox = AddField(L.T("Sensibilidade", "Sensitivity"), 0, fieldsY, 7, L.T("ex.: 1,25", "e.g. 1.25"));
            dpiBox = AddField("DPI", 1, fieldsY, 5);
            newDpiBox = AddField(L.T("DPI novo", "New DPI"), 2, fieldsY, 5);
            yawBox = AddField(L.T("Yaw (outro)", "Yaw (other)"), 3, fieldsY, 9, L.T("ex.: 0,022", "e.g. 0.022"));
            foreach (TextBox box in new[] { dpiBox, newDpiBox })
                box.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };

            readout = new BigReadout { Location = new Point(12, fieldsY + 62), Size = new Size(456, 146), Unit = "cm/360", AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            Controls.Add(readout);

            var sameAim = new SectionLabel { Text = L.T("A mesma mira em cada jogo", "The same aim in each game"), Location = new Point(20, readout.Bottom + 8), Size = new Size(440, 20) };
            Controls.Add(sameAim);
            // Um cartão com uma linha por jogo; sem sensibilidade, o estado vazio no lugar dele.
            const int rowH = 32;
            int count = Sensitivity.Games.Length + 1;
            results = new Card { Location = new Point(20, sameAim.Bottom + 8), Size = new Size(440, rowH * count + 12) };
            for (int i = 0; i < count; i++)
            {
                int ry = 6 + i * rowH;
                if (i > 0) results.Controls.Add(new Panel { BackColor = Theme.Border, Bounds = new Rectangle(16, ry, 408, 1) });
                var name = new Label { Location = new Point(16, ry), Size = new Size(262, rowH), ForeColor = Theme.Text, BackColor = Theme.Surface, Font = Theme.Ui(9.5f), TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
                var value = new Label { Location = new Point(278, ry), Size = new Size(146, rowH), ForeColor = Theme.Text, BackColor = Theme.Surface, Font = Theme.Ui(11f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false };
                results.Controls.Add(name);
                results.Controls.Add(value);
                rows.Add(new[] { name, value });
            }
            Controls.Add(results);
            emptyResults = new EmptyHint
            {
                Glyph = GlyphKind.Target,
                Framed = true,
                Text = L.T("Digite a sensibilidade para ver a equivalência em cada jogo", "Type your sensitivity to see the match in each game"),
                Bounds = results.Bounds
            };
            Controls.Add(emptyResults);

            // A dica mais longa (com o "DPI novo" diferente) cabe inteira: a altura é medida com ela.
            string longestHint = Hint() + L.T(" Com 88888 DPI, use os valores acima para a mira não mudar.", " At 88888 DPI, use the values above so your aim does not change.");
            hint = MeasuredNote(longestHint, 20, results.Bottom + 12, 440, Theme.TextSecondary, 8.5f);
            Controls.Add(hint);

            var measure = new NeonButton { Text = L.T("Medir o DPI real", "Measure the real DPI"), Glyph = GlyphKind.Ruler, Location = new Point(20, hint.Bottom + 12), Size = new Size(440, 36), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            measure.Click += (s, e) => { if (this.measureDpi != null) this.measureDpi(); };
            Controls.Add(measure);
            ClientSize = new Size(480, measure.Bottom + 20);
            // Volta da medição do DPI real: a volta passa a usar o valor medido.
            Activated += (s, e) => SyncDpi();

            // Última conversa: jogo, sensibilidade e yaw daqui; o DPI é o mesmo do teste de mira.
            game.SelectedIndex = cfg.ConverterGame >= 0 && cfg.ConverterGame <= OtherIndex ? cfg.ConverterGame : 0;
            sensBox.Text = cfg.ConverterSensitivity ?? "";
            yawBox.Text = cfg.ConverterYaw ?? "";
            shownDpi = cfg.AimTestDpi;
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
            // Só grava se a pessoa mudou aqui: senão, um DPI informado em outra tela ficaria por cima.
            if (int.TryParse(dpiBox.Text, out dpi) && dpi > 0 && dpi != shownDpi) cfg.AimTestDpi = dpi;
            if (save != null) save();
            base.OnFormClosed(e);
        }

        /// <summary>Campo da coluna (0 a 3) de quatro: rótulo em sentence case e, 6 px abaixo, a caixa de 36 px.</summary>
        private TextBox AddField(string label, int column, int y, int maxLength, string example = null)
        {
            const int width = 101, gap = 12;
            int x = 20 + column * (width + gap);
            Controls.Add(new FieldLabel { Text = label, Location = new Point(x, y), Size = new Size(width, 16) });
            // Números na fonte de números (Bahnschrift), como as leituras; o exemplo do campo vazio em TextTertiary.
            var box = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.NumField(12f), TextAlign = HorizontalAlignment.Center, MaxLength = maxLength };
            var host = new FieldHost(box) { Location = new Point(x, y + 22), Size = new Size(width, 36), Accent = Theme.PerfB };
            if (example != null)
            {
                host.PlaceholderFont = Theme.Ui(9f);
                host.PlaceholderAlign = ContentAlignment.MiddleCenter;
                host.Placeholder = example;
                Theme.CueBanner(box, example); // com o foco no campo vazio, o exemplo nativo (o cursor fica visível)
            }
            Controls.Add(host);
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
                results.Visible = false;
                emptyResults.Visible = true;
                hint.Text = Hint();
                readout.Invalidate();
                return;
            }

            results.Visible = true;
            emptyResults.Visible = false;
            if (!int.TryParse(newDpiBox.Text, out newDpi) || newDpi <= 0) newDpi = dpi; // vazio: o mesmo DPI
            double yaw = fromOther ? otherYaw : Sensitivity.Games[game.SelectedIndex].Yaw;
            double cm = Sensitivity.Cm360(sens, yaw, dpi);

            // Distância na língua da tela (43,3 cm / 43.3 cm); a sensibilidade sempre com ponto, como nos jogos.
            CultureInfo culture = L.IsEnglish ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("pt-BR");
            int real = RealDpi(dpi);
            if (real > 0)
            {
                // O sensor entrega outro DPI: a volta de verdade é a do DPI real; a anunciada fica de referência.
                double realCm = Sensitivity.Cm360(sens, yaw, real);
                readout.Value = realCm.ToString(realCm < 100 ? "0.0" : "0", culture);
                readout.Caption = L.T("com o DPI real (", "with the real DPI (") + real + L.T(") · anunciado: ", ") · at the set DPI: ") +
                                  cm.ToString(cm < 100 ? "0.0" : "0", culture) + " cm";
            }
            else
            {
                readout.Value = cm.ToString(cm < 100 ? "0.0" : "0", culture);
                readout.Caption = L.T("uma volta completa no jogo · ", "one full turn in the game · ") +
                                  (cm / 2.54).ToString(cm < 254 ? "0.0" : "0", culture) + L.T(" pol", " in");
            }

            for (int i = 0; i < rows.Count; i++)
            {
                Label[] row = rows[i];
                bool isOther = i == Sensitivity.Games.Length;
                if (isOther && !hasOther)
                {
                    row[0].Text = L.T("Outro jogo: informe o yaw", "Other game: enter its yaw");
                    row[0].ForeColor = Theme.TextTertiary;
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

        /// <summary>Volta da medição do DPI real ou do teste de mira: a caixa passa a mostrar o DPI que eles gravaram.</summary>
        private void SyncDpi()
        {
            if (cfg.AimTestDpi > 0 && cfg.AimTestDpi != shownDpi)
            {
                shownDpi = cfg.AimTestDpi;
                dpiBox.Text = cfg.AimTestDpi.ToString(); // TextChanged recalcula
            }
            else
            {
                Recalculate();
            }
        }

        /// <summary>O DPI real medido para este DPI anunciado; 0 se não houver (ou se der o mesmo).</summary>
        private int RealDpi(int declared)
        {
            return cfg.MeasuredDpi > 0 && cfg.MeasuredDpiFor == declared && cfg.MeasuredDpi != declared ? cfg.MeasuredDpi : 0;
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
