using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>Partidas anteriores: o histórico do Raio-X, da mais nova para a mais antiga; abrir uma mostra o relatório dela.</summary>
    public class MatchHistoryForm : GamerWindow
    {
        private readonly AppConfig cfg;
        private readonly Action changed;
        private readonly Action cleared;
        private readonly ListBox list;
        private readonly Label status;
        private readonly NeonButton open;
        private readonly NeonButton clear;
        private List<MatchRecord> records = new List<MatchRecord>();
        private MatchReportForm reportForm;

        /// <summary>A pergunta antes de apagar (null = a caixa de confirmação de verdade). Os testes trocam.</summary>
        internal Func<string, bool> ConfirmClear;

        /// <param name="changed">Chamado quando a pessoa muda algo num relatório (para gravar o config).</param>
        /// <param name="cleared">Chamado depois de apagar o histórico.</param>
        public MatchHistoryForm(AppConfig cfg, Action changed, Action cleared) : base(L.T("Partidas anteriores", "Previous matches"), false)
        {
            this.cfg = cfg;
            this.changed = changed;
            this.cleared = cleared;
            CloseOnEscape = true;
            TitleBar.AccentA = Theme.PerfA;
            TitleBar.AccentB = Theme.PerfB;
            ClientSize = new Size(480, 560);

            Controls.Add(Note(L.T("O Raio-X das últimas " + MatchHistory.MaxRecords + " partidas, guardado só neste PC. Abra uma para ver o " +
                                  "relatório e a comparação com a anterior do mesmo jogo.",
                                  "The X-ray of the last " + MatchHistory.MaxRecords + " matches, kept only on this PC. Open one to see the " +
                                  "report and the comparison with the previous match of the same game."),
                              20, 56, 440, 36, Theme.Muted, 9f));

            list = new ListBox
            {
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 30,
                Font = Theme.Ui(10f)
            };
            list.DrawItem += DrawItem;
            list.DoubleClick += (s, e) => OpenSelected();
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; OpenSelected(); } };
            list.SelectedIndexChanged += (s, e) => { list.Invalidate(); open.Enabled = list.SelectedIndex >= 0; open.Invalidate(); };
            Controls.Add(new FieldHost(list) { Location = new Point(20, 98), Size = new Size(440, 330), Accent = Theme.PerfB });

            status = Note("", 20, 434, 440, 48, Theme.Faint, 8.5f); // até 3 linhas: vazio e histórico desligado
            Controls.Add(status);

            open = new NeonButton { Text = L.T("Abrir", "Open"), Style = ButtonStyle.Primary, Location = new Point(20, 486), Size = new Size(212, 44), AccentA = Theme.PerfA, AccentB = Theme.PerfB, Enabled = false };
            open.Click += (s, e) => OpenSelected();
            clear = new NeonButton { Text = L.T("Apagar o histórico", "Delete the history"), Style = ButtonStyle.Danger, Location = new Point(248, 486), Size = new Size(212, 44) };
            clear.Click += (s, e) => ClearAll();
            Controls.Add(open);
            Controls.Add(clear);

            Fill(MatchHistory.Load());
            FinishLayout();
        }

        /// <summary>Mostra as partidas e a linha de baixo (quantas, e se o histórico está desligado).</summary>
        internal void Fill(List<MatchRecord> newestFirst)
        {
            records = newestFirst ?? new List<MatchRecord>();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (MatchRecord r in records) list.Items.Add(r);
            list.EndUpdate();
            if (records.Count > 0) list.SelectedIndex = 0;
            clear.Enabled = records.Count > 0;
            clear.Invalidate();

            string text = records.Count == 0
                ? L.T("Nenhuma partida guardada ainda: o Raio-X entra aqui ao fim de cada partida de pelo menos 2 minutos no Performance.",
                      "No match saved yet: the X-ray comes here at the end of each match of at least 2 minutes in Performance.")
                : records.Count == 1 ? L.T("1 partida guardada.", "1 match saved.")
                                     : L.T(records.Count + " partidas guardadas.", records.Count + " matches saved.");
            if (!cfg.KeepMatchHistory)
                text += L.T(" O histórico está desligado: partidas novas não entram (Ajustes → Privacidade).",
                            " The history is off: new matches are not added (Settings → Privacy).");
            status.Text = text;
        }

        /// <summary>"16/11 21:40 · cs2 · 47 min" e, na etiqueta, o FPS médio (ou nada, sem quadros).</summary>
        internal static string ItemText(MatchRecord r)
        {
            return r.When() + "  ·  " + r.Game + "  ·  " + MatchReport.FormatDuration(TimeSpan.FromSeconds(r.DurationSeconds));
        }

        internal static string ItemTag(MatchRecord r)
        {
            return r.Frames != null ? Math.Round(r.Frames.AverageFps).ToString(CultureInfo.InvariantCulture) + " FPS" : null;
        }

        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= records.Count) return;
            MatchRecord r = records[e.Index];
            ListPainter.Draw(e, ItemText(r), (e.State & DrawItemState.Selected) != 0, ItemTag(r), Theme.PerfA, Theme.PerfB);
        }

        private void OpenSelected()
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= records.Count) return;
            MatchRecord record = records[i];
            if (reportForm != null && !reportForm.IsDisposed) reportForm.Close();
            reportForm = new MatchReportForm(record.ToReport(), cfg, changed, record, MatchHistory.Previous(records, record));
            reportForm.Show(this);
            reportForm.Activate();
        }

        private void ClearAll()
        {
            if (records.Count == 0) return;
            string question = L.T("Apagar as " + records.Count + " partidas do histórico? Não dá para desfazer.",
                                  "Delete the " + records.Count + " matches in the history? This cannot be undone.");
            bool yes = ConfirmClear != null
                ? ConfirmClear(question)
                : ConfirmDialog.Ask(this, L.T("Apagar o histórico", "Delete the history"), question, L.T("Apagar", "Delete"));
            if (!yes) return;
            int deleted = MatchHistory.Clear();
            Log.Info(L.T("Histórico das partidas apagado (", "Match history deleted (") + deleted + ")");
            if (reportForm != null && !reportForm.IsDisposed) reportForm.Close();
            Fill(MatchHistory.Load()); // o que não deu para apagar (arquivo em uso) continua aparecendo
            if (records.Count > 0)
                status.Text = L.T(records.Count + " partida(s) não puderam ser apagadas agora; tente de novo.",
                                  records.Count + " match(es) could not be deleted now; try again.");
            if (cleared != null) cleared();
        }
    }
}
