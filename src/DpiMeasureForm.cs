using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>
    /// DPI real do mouse: com uma régua na mesa, a pessoa segura o botão esquerdo no quadro, arrasta o mouse em linha
    /// reta pela distância escolhida e solta. As contagens vêm do Raw Input (as mesmas que os jogos leem).
    /// </summary>
    public class DpiMeasureForm : GamerWindow
    {
        private const int MaxRuns = 10;

        private readonly AppConfig cfg;
        private readonly Action save;
        private readonly TextBox dpiBox;
        private readonly TextBox distanceBox;
        private readonly BigReadout readout;
        private readonly ChipRow chips;
        private readonly DragPad pad;
        private readonly Label status;
        private readonly RawMouse raw = new RawMouse();
        private readonly List<double> runs = new List<double>();
        private readonly CultureInfo culture = L.IsEnglish ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("pt-BR");
        private bool measuring;
        private long sumX, sumY;

        /// <param name="save">Grava o config (o DPI real medido vai para o conversor de sensibilidade).</param>
        public DpiMeasureForm(AppConfig cfg, Action save) : base(L.T("DPI real do mouse", "Real mouse DPI"), true)
        {
            this.cfg = cfg;
            this.save = save;
            CloseOnEscape = true;
            ClientSize = new Size(480, 628);
            TitleBar.AccentA = Theme.PerfA;
            TitleBar.AccentB = Theme.PerfB;

            Controls.Add(Note(L.T("Ponha uma régua na mesa. Segure o botão esquerdo no quadro abaixo, arraste o mouse em linha reta " +
                                  "pela distância e solte. Faça umas 3 vezes, sem levantar o mouse.",
                                  "Put a ruler on the desk. Hold the left button on the box below, drag the mouse in a straight line " +
                                  "over the distance and let go. Do it about 3 times, without lifting the mouse."),
                              20, 50, 440, 56, Theme.Muted, 9f));

            dpiBox = AddField(L.T("DPI DO MOUSE", "MOUSE DPI"), 20, 5);
            distanceBox = AddField(L.IsEnglish ? "DISTANCE (IN)" : "DISTÂNCIA (CM)", 152, 5);
            dpiBox.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            dpiBox.Text = (cfg.AimTestDpi > 0 ? cfg.AimTestDpi : 800).ToString();
            distanceBox.Text = L.IsEnglish ? "4" : "10";
            Controls.Add(Note(L.T("O DPI que está no programa do mouse e o quanto você vai arrastar na régua.",
                                  "The DPI set in your mouse software and how far you will drag along the ruler."),
                              284, 130, 176, 44, Theme.Faint, 8f));
            dpiBox.TextChanged += (s, e) => UpdateView();
            distanceBox.TextChanged += (s, e) => Reset();

            readout = new BigReadout { Location = new Point(12, 174), Size = new Size(456, 150), Unit = "DPI", AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            chips = new ChipRow
            {
                Location = new Point(20, 330),
                Size = new Size(440, 48),
                Labels = new[] { L.T("MEDIÇÕES", "RUNS"), L.T("ÚLTIMA", "LAST"), L.T("VARIAÇÃO", "SPREAD"), L.T("DIFERENÇA", "OFF BY") }
            };
            pad = new DragPad { Location = new Point(20, 392), Size = new Size(440, 120) };
            pad.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) Begin(); };
            pad.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) End(); };
            status = Note("", 20, 520, 440, 40, Theme.Faint, 8.5f);

            var reset = new NeonButton { Text = L.T("Zerar", "Reset"), Location = new Point(20, 568), Size = new Size(440, 44), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            reset.Click += (s, e) => Reset();

            Controls.Add(readout);
            Controls.Add(chips);
            Controls.Add(pad);
            Controls.Add(status);
            Controls.Add(reset);

            FinishLayout();
            UpdateView();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            string error;
            if (!raw.Register(Handle, true, out error))
            {
                status.Text = L.T("Raw Input indisponível: ", "Raw Input unavailable: ") + error;
                Log.Warn(L.T("DPI real: Raw Input indisponível (", "Real DPI: Raw Input unavailable (") + error + ")");
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            raw.Unregister();
            raw.Dispose();
            int declared = Declared();
            if (declared > 0) cfg.AimTestDpi = declared;
            if (runs.Count > 0 && declared > 0)
            {
                // O conversor de sensibilidade mostra a volta com o DPI real enquanto o mouse estiver nesse DPI.
                cfg.MeasuredDpi = (int)Math.Round(DpiMeter.Median(runs));
                cfg.MeasuredDpiFor = declared;
                Log.Info(L.T("DPI real medido: ", "Real DPI measured: ") + cfg.MeasuredDpi + L.T(" (anunciado ", " (set to ") + declared +
                         ", " + runs.Count + L.T(" medição(ões))", " run(s))"));
            }
            if (save != null) save();
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == RawMouse.WM_INPUT && measuring)
            {
                int dx, dy;
                ushort buttons;
                if (raw.Read(m.LParam, out dx, out dy, out buttons))
                {
                    sumX += dx;
                    sumY += dy;
                    pad.Live = DpiMeter.Counts(sumX, sumY).ToString("N0", culture) + L.T(" contagens", " counts");
                    pad.Invalidate();
                }
            }
            base.WndProc(ref m);
        }

        private TextBox AddField(string label, int x, int maxLength)
        {
            Controls.Add(Note(label, x, 110, 124, 18, Theme.Faint, 8f));
            var box = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Ui(12f), TextAlign = HorizontalAlignment.Center, MaxLength = maxLength };
            Controls.Add(new FieldHost(box) { Location = new Point(x, 130), Size = new Size(124, 40), Accent = Theme.PerfB });
            return box;
        }

        private int Declared()
        {
            int dpi;
            return int.TryParse(dpiBox.Text, out dpi) && dpi > 0 ? dpi : 0;
        }

        private void Begin()
        {
            double distance;
            if (!Sensitivity.TryParse(distanceBox.Text, out distance))
            {
                status.Text = L.T("Informe a distância que você vai arrastar na régua.", "Enter the distance you will drag along the ruler.");
                return;
            }
            sumX = 0;
            sumY = 0;
            measuring = true;
            pad.Active = true;
            pad.Live = L.T("arraste até o fim da distância e solte", "drag to the end of the distance and let go");
            pad.Invalidate();
        }

        private void End()
        {
            if (!measuring) return;
            measuring = false;
            pad.Active = false;
            pad.Live = "";
            pad.Invalidate();

            double distance;
            if (!Sensitivity.TryParse(distanceBox.Text, out distance)) return;
            if (DpiMeter.Counts(sumX, sumY) < DpiMeter.MinCounts)
            {
                status.Text = L.T("Arrasto curto demais: segure o botão e arraste a distância toda da régua.",
                                  "Drag too short: hold the button and drag the whole distance on the ruler.");
                return;
            }
            runs.Add(DpiMeter.Dpi(sumX, sumY, DpiMeter.Inches(distance, L.IsEnglish)));
            if (runs.Count > MaxRuns) runs.RemoveAt(0);
            UpdateView();
        }

        private void Reset()
        {
            runs.Clear();
            measuring = false;
            pad.Active = false;
            pad.Live = "";
            pad.Invalidate();
            UpdateView();
        }

        private void UpdateView()
        {
            int declared = Declared();
            if (runs.Count == 0)
            {
                readout.Value = "—";
                readout.Caption = L.T("segure no quadro abaixo e arraste pela régua", "hold on the box below and drag along the ruler");
                chips.Values = new[] { "0", "—", "—", "—" };
                status.Text = L.T("O DPI anunciado é o do programa do mouse; o real é o que o sensor entrega. A velocidade do Windows não entra.",
                                  "The set DPI is the one in your mouse software; the real one is what the sensor delivers. The Windows speed does not count.");
            }
            else
            {
                double median = DpiMeter.Median(runs);
                double spread = DpiMeter.Spread(runs);
                readout.Value = Math.Round(median).ToString("0", culture);
                chips.Values = new[]
                {
                    runs.Count.ToString(),
                    Math.Round(runs[runs.Count - 1]).ToString("0", culture),
                    runs.Count < 2 ? "—" : (spread * 100).ToString("0.0", culture) + "%",
                    declared > 0 ? Percent(DpiMeter.Deviation(median, declared)) : "—"
                };
                readout.Caption = declared > 0
                    ? L.T("anunciado ", "set to ") + declared + " · " + Gap(DpiMeter.Deviation(median, declared))
                    : L.T("informe o DPI do programa do mouse para comparar", "enter the DPI from your mouse software to compare");
                if (runs.Count < 3)
                    status.Text = L.T("Faça mais uma medição para confirmar.", "Do one more run to confirm.");
                else if (spread > DpiMeter.MaxSpread)
                    status.Text = L.T("As medições variaram muito: confira a distância, arraste em linha reta e não levante o mouse.",
                                      "The runs varied a lot: check the distance, drag in a straight line and do not lift the mouse.");
                else
                    status.Text = L.T("Pronto. Ao fechar, o conversor de sensibilidade passa a mostrar a volta com o DPI real.",
                                      "Done. When you close this, the sensitivity converter shows the turn with the real DPI.");
            }
            readout.Invalidate();
            chips.Invalidate();
        }

        private string Percent(double deviation)
        {
            return (deviation > 0 ? "+" : "") + (deviation * 100).ToString("0.0", culture) + "%";
        }

        /// <summary>"4,6% a menos que o anunciado" / "igual ao anunciado".</summary>
        private string Gap(double deviation)
        {
            double pct = Math.Abs(deviation * 100);
            if (pct < 0.5) return L.T("igual ao anunciado", "same as set");
            return pct.ToString("0.0", culture) + (deviation < 0 ? L.T("% a menos", "% lower") : L.T("% a mais", "% higher"));
        }
    }

    /// <summary>O quadro onde a pessoa segura o botão para começar a medir.</summary>
    public class DragPad : GControl
    {
        public bool Active { get; set; }
        public string Live { get; set; }

        public DragPad()
        {
            Live = "";
            Cursor = Cursors.SizeAll;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            var card = new RectangleF(1, 1, Width - 3, Height - 3);
            Theme.FillRounded(g, card, 14, Active ? Theme.Panel2 : Theme.Panel);
            using (var pen = new Pen(Active ? Theme.PerfB : Theme.Border, 1.5f) { DashStyle = DashStyle.Dash })
            using (GraphicsPath path = Theme.Rounded(card, 14))
                g.DrawPath(pen, path);

            Theme.DrawText(g, Active ? L.T("MEDINDO", "MEASURING") : L.T("SEGURE AQUI E ARRASTE", "HOLD HERE AND DRAG"),
                Theme.Display(12f), Active ? Theme.PerfB : Theme.Text,
                new RectangleF(0, Height / 2f - 26, Width, 24), StringAlignment.Center, StringAlignment.Center, false);
            Theme.DrawText(g, Live.Length > 0 ? Live : L.T("o mouse pode sair do quadro: o que conta é a régua",
                                                           "the mouse may leave the box: the ruler is what counts"),
                Theme.Ui(9f), Theme.Muted, new RectangleF(0, Height / 2f + 4, Width, 22), StringAlignment.Center, StringAlignment.Center, false);
        }
    }
}
