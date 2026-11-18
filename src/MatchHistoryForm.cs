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
        private readonly EmptyHint empty;
        private readonly int statusWidth;
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
            const int width = 440, itemHeight = 34, visibleItems = 9;

            Label intro = MeasuredNote(L.T("O Raio-X das últimas " + MatchHistory.MaxRecords + " partidas, guardado só neste PC. Abra uma para ver o " +
                                           "relatório e a comparação com a anterior do mesmo jogo.",
                                           "The X-ray of the last " + MatchHistory.MaxRecords + " matches, kept only on this PC. Open one to see the " +
                                           "report and the comparison with the previous match of the same game."),
                                       20, 56, width, Theme.TextSecondary, 9f);
            Controls.Add(intro);

            // Mesma lista das Configurações: itens de 34 px, só itens inteiros, seleção em tinta com a barra de 3 px.
            list = new ListBox
            {
                BorderStyle = BorderStyle.None,
                IntegralHeight = true,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = itemHeight,
                Font = Theme.Ui(10f)
            };
            // A altura do item não acompanha a escala da janela sozinha.
            list.HandleCreated += (s, e) => list.ItemHeight = Math.Max(itemHeight, (int)Math.Round(itemHeight * list.DeviceDpi / 96f));
            list.DrawItem += DrawItem;
            list.DoubleClick += (s, e) => OpenSelected();
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; OpenSelected(); } };
            list.SelectedIndexChanged += (s, e) => { list.Invalidate(); open.Enabled = list.SelectedIndex >= 0; open.Invalidate(); };
            var listHost = new FieldHost(list) { Location = new Point(20, intro.Bottom + 12), Size = new Size(width, itemHeight * visibleItems + 20), Accent = Theme.PerfB };
            // Sem partidas: o estado vazio cobre a lista (o texto de baixo explica).
            empty = new EmptyHint
            {
                Glyph = GlyphKind.Clock,
                Text = L.T("Nenhuma partida guardada ainda", "No match saved yet"),
                BackColor = Theme.Surface2,
                Bounds = new Rectangle(2, 2, width - 4, listHost.Height - 4),
                Visible = false
            };
            listHost.Controls.Add(empty);
            empty.BringToFront();
            Controls.Add(listHost);

            // Começa com uma linha; SetStatus cresce para o texto que entrar (até 3 linhas: vazio e histórico desligado).
            statusWidth = width;
            status = MeasuredNote("", 20, listHost.Bottom + 8, width, Theme.TextSecondary, 8.5f);
            Controls.Add(status);

            // Abrir é a ação da tela (primária, à direita); apagar tudo é destrutivo e fica à esquerda, em vermelho.
            int buttonsY = status.Bottom + 12;
            string clearText = L.T("Apagar o histórico", "Delete the history");
            open = new NeonButton { Text = L.T("Abrir", "Open"), Style = ButtonStyle.Primary, Location = new Point(20 + width - 128, buttonsY), Size = new Size(128, 36), AccentA = Theme.PerfA, AccentB = Theme.PerfB, Enabled = false };
            open.Click += (s, e) => OpenSelected();
            clear = new NeonButton { Text = clearText, Style = ButtonStyle.Danger, Location = new Point(20, buttonsY), Size = new Size(ButtonWidth(150, clearText), 36) };
            clear.Click += (s, e) => ClearAll();
            Controls.Add(open);
            Controls.Add(clear);
            ClientSize = new Size(480, buttonsY + 36 + 20);

            Fill(MatchHistory.Load()); // ajusta a linha de status (e os botões e a janela) ao texto de agora
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
            open.Enabled = list.SelectedIndex >= 0; // esvaziar a lista nem sempre avisa a troca de seleção
            open.Invalidate();
            clear.Enabled = records.Count > 0;
            clear.Invalidate();
            empty.Visible = records.Count == 0;
            list.Visible = records.Count > 0;

            string text = records.Count == 0
                ? L.T("Nenhuma partida guardada ainda: o Raio-X entra aqui ao fim de cada partida de pelo menos 2 minutos no Performance.",
                      "No match saved yet: the X-ray comes here at the end of each match of at least 2 minutes in Performance.")
                : records.Count == 1 ? L.T("1 partida guardada.", "1 match saved.")
                                     : L.T(records.Count + " partidas guardadas.", records.Count + " matches saved.");
            if (!cfg.KeepMatchHistory)
                text += L.T(" O histórico está desligado: partidas novas não entram (Ajustes → Privacidade).",
                            " The history is off: new matches are not added (Settings → Privacy).");
            SetStatus(text);
        }

        /// <summary>
        /// Troca o texto de baixo com a linha da altura dele: os botões vêm logo abaixo e a janela acompanha. Com a janela
        /// aberta a linha só cresce (nunca encolhe): o status muda quando chega uma partida, quando o histórico é apagado ou
        /// ligado/desligado, e a janela não fica sobe-e-desce a cada troca. A medida é a 96 dpi, na escala atual da linha.
        /// </summary>
        private void SetStatus(string text)
        {
            status.Text = text;
            float k = status.Width / (float)statusWidth;
            int needed = (int)Math.Ceiling(Theme.TextHeight(text, status.Font, statusWidth) * k);
            int delta = needed - status.Height;
            if (delta <= 0) return;

            // Com a rolagem de tela pequena, os controles estão no painel de conteúdo: ele cresce, e a rolagem junto.
            Control host = status.Parent;
            SuspendLayout();
            status.Height = needed;
            open.Top += delta;
            clear.Top += delta;
            if (host == null || host == this) Height += delta; // Height: no construtor o ClientSize ainda pode vir com a borda padrão
            else host.Height += delta;
            ResumeLayout(false);
            if (IsHandleCreated && Visible && WindowState == FormWindowState.Normal) KeepOnScreen();
        }

        /// <summary>Janela que cresceu para baixo da área útil: sobe o que der e, se nem assim couber, passa a rolar.</summary>
        private void KeepOnScreen()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            if (Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
            FitToWorkingArea(area);
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
                SetStatus(L.T(records.Count + " partida(s) não puderam ser apagadas agora; tente de novo.",
                              records.Count + " match(es) could not be deleted now; try again."));
            if (cleared != null) cleared();
        }
    }
}
