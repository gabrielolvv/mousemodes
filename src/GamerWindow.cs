using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>Janela sem a borda do Windows, fundo escuro, barra de título própria e cantos arredondados (Win 11).</summary>
    public class GamerWindow : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int CS_DROPSHADOW = 0x00020000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWA_BORDER_COLOR = 34;

        /// <summary>O Windows 11 desenha a borda (na cor Theme.Border): a janela não pinta a sua.</summary>
        private bool systemBorder;

        protected readonly TitleBar TitleBar;

        /// <summary>A borda de 1 px é a do Windows 11 (a janela não pinta a sua). Vale depois de o handle existir.</summary>
        protected bool SystemBorder
        {
            get { return systemBorder; }
        }

        /// <summary>Esc fecha a janela (para diálogos).</summary>
        protected bool CloseOnEscape { get; set; }

        public GamerWindow(string title, bool showMinimize)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            DoubleBuffered = true;
            KeyPreview = true;

            TitleBar = new TitleBar
            {
                Title = title, // sentence case, como o resto da interface
                ShowMinimize = showMinimize,
                Dock = DockStyle.Top
            };
            TitleBar.CloseClicked += () => Close();
            TitleBar.MinimizeClicked += () => WindowState = FormWindowState.Minimized;
            Controls.Add(TitleBar);
        }

        /// <summary>Chamar no fim do construtor da janela filha (aplica a escala de DPI).</summary>
        protected void FinishLayout()
        {
            ResumeLayout(false);
            PerformLayout();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.Style |= WS_MINIMIZEBOX; // permite minimizar clicando na barra de tarefas
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                int round = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                // Borda de 1 px do próprio Windows 11, acompanhando o canto arredondado (COLORREF = 0x00BBGGRR).
                int border = Theme.Border.R | (Theme.Border.G << 8) | (Theme.Border.B << 16);
                systemBorder = DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref border, sizeof(int)) == 0;
            }
            catch { } // Windows 10 ou outro sistema: ignora (a janela pinta a borda)
            if (ScrollerBar != null) ScrollerBar.EdgeColor = systemBorder ? Color.Empty : Theme.Border;
        }

        /// <summary>Área de rolagem criada quando a janela não cabe na tela; null enquanto couber.</summary>
        internal Panel Scroller { get; private set; }

        /// <summary>Barra fina na margem direita que mostra e move a rolagem do Scroller; null enquanto couber.</summary>
        internal ThinScrollBar ScrollerBar { get; private set; }

        /// <summary>Largura (px a 96 dpi) da coluna da barra fina: o que a janela cresce quando passa a rolar.</summary>
        internal const int ScrollGutter = 12;

        /// <summary>A coluna da barra fina na escala atual da janela.</summary>
        internal int ScaledScrollGutter
        {
            get { return (int)Math.Round(ScrollGutter * DeviceDpi / 96f); }
        }

        /// <summary>
        /// Janela mais alta que a área útil da tela (escala do Windows alta num notebook): encaixa nela e rola.
        /// A barra de título fica fixa em cima, para arrastar e fechar continuarem à mão.
        /// </summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitToWorkingArea(Screen.FromControl(this).WorkingArea);
        }

        /// <summary>
        /// Se a janela passar da área útil: quando só a margem vazia de baixo sobra (alguns pixels, como 724 px numa
        /// área de 720), encosta a janela no alto e corta essa margem. Se os controles mesmo não couberem, passa o
        /// conteúdo (tudo menos a barra de título) para um painel com rolagem do tamanho da área útil.
        /// Retorna true se criou a rolagem.
        /// </summary>
        internal bool FitToWorkingArea(Rectangle area)
        {
            if (Scroller != null || Height <= area.Height) return false; // cabe (mesmo justa, como numa tela de 768 px)

            int bar = TitleBar.Height;
            int contentHeight = ClientSize.Height - bar;
            int nativeBar = SystemInformation.VerticalScrollBarWidth;
            int gutter = ScaledScrollGutter;
            var moving = new List<Control>();
            int lowest = bar;
            foreach (Control c in Controls)
            {
                if (c == TitleBar) continue;
                moving.Add(c);
                lowest = Math.Max(lowest, c.Bottom);
            }

            // O Windows limita a altura da janela (SystemInformation.MaxWindowTrackSize, a altura da área virtual + 20):
            // o Raio-X com a comparação pede mais que isso numa tela de 1080 px e os últimos controles (nota e Fechar)
            // ficam abaixo da área cliente. O painel de conteúdo vai até eles, com a margem de baixo de 16 px.
            if (lowest > ClientSize.Height - 1)
                contentHeight = lowest - bar + (int)Math.Round(16 * DeviceDpi / 96f);

            // O último controle cabe acima da borda de 1 px: basta perder parte da margem vazia, sem rolagem.
            if (lowest <= area.Height - 1)
            {
                Top = area.Top;
                Height = Math.Min(Height, area.Height);
                return false;
            }

            SuspendLayout();
            // Mais larga só pela coluna da barra fina: o conteúdo continua com a largura de antes.
            int contentWidth = ClientSize.Width - 2;
            Width += gutter;
            Left = Math.Max(area.Left, Math.Min(Left - gutter / 2, area.Right - Width));
            Height = Math.Max(bar * 3, area.Height);
            Top = area.Top;

            // Como as páginas dos Ajustes: o painel com rolagem vai além da coluna da barra fina, e a barra nativa dele
            // (a calha escura do Windows) fica nessa parte, recortada pela região do painel (OnLayout). Fica dentro da
            // borda de 1 px que a janela desenha em cima, à esquerda e embaixo.
            var panel = new Panel
            {
                AutoScroll = true,
                BackColor = BackColor,
                Bounds = new Rectangle(1, bar, contentWidth + nativeBar, ClientSize.Height - bar - 1),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            Theme.DarkScroll(panel); // se a barra nativa escapar da região (outro DPI), ao menos escura
            // Os controles vão num painel simples da largura de antes, e só ele entra na rolagem: o que passa da borda
            // direita de propósito (as páginas dos Ajustes, com a barra nativa escondida além dela) fica recortado nele
            // e não cria rolagem para o lado. A altura dele inclui a margem de baixo, que também rola.
            var content = new Panel
            {
                BackColor = BackColor,
                Bounds = new Rectangle(0, 0, contentWidth, contentHeight)
            };
            // A barra fina ocupa a coluna nova e a da borda direita (como a dos Ajustes: no Windows 10 ela pinta essa
            // borda, EdgeColor), e cobre a parte da barra nativa que ainda fica dentro da janela.
            var thin = new ThinScrollBar
            {
                BackColor = BackColor,
                TabStop = false,
                RightInset = 4,
                EdgeColor = SystemBorder || !IsHandleCreated ? Color.Empty : Theme.Border,
                Bounds = new Rectangle(1 + contentWidth, bar, ClientSize.Width - 1 - contentWidth, panel.Height),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right
            };
            panel.SuspendLayout();
            content.SuspendLayout();
            foreach (Control c in moving)
            {
                c.Location = new Point(c.Left - 1, c.Top - bar);
                content.Controls.Add(c);
            }
            panel.Controls.Add(content);
            Controls.Add(panel);
            // Na tela os dois não se sobrepõem (região do painel). Por último na ordem: quem imprime a janela
            // (DrawToBitmap, WM_PRINT) ignora a região e desenha os de baixo por cima, e a barra fina tem de ganhar.
            Controls.Add(thin);
            content.ResumeLayout(false);
            panel.ResumeLayout(true); // calcula já a rolagem pelo tamanho do painel de conteúdo
            Scroller = panel;
            ScrollerBar = thin;
            thin.Target = panel;
            // Qualquer rolagem (roda, teclado, foco num controle fora da vista, a própria barra) move o painel de
            // conteúdo: a barra fina relê a posição. O WinForms só atualiza a barra nativa depois de mover os controles,
            // então a leitura também vai para a fila de mensagens. O tamanho também (o histórico faz o conteúdo crescer).
            EventHandler sync = (s, e) =>
            {
                thin.Sync();
                if (thin.IsHandleCreated && !thin.IsDisposed) thin.BeginInvoke((MethodInvoker)thin.Sync);
            };
            content.LocationChanged += sync;
            content.SizeChanged += sync;
            panel.SizeChanged += sync;
            ResumeLayout(true);
            ClipScroller();
            return true;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            ClipScroller();
        }

        private Size scrollerClip;

        /// <summary>O painel com rolagem só aparece até a barra fina: a barra nativa dele fica recortada.</summary>
        private void ClipScroller()
        {
            if (Scroller == null || ScrollerBar == null) return;
            var visible = new Size(Math.Max(1, ScrollerBar.Left - Scroller.Left), Scroller.Height);
            if (visible == scrollerClip && Scroller.Region != null) return;
            scrollerClip = visible;
            Region old = Scroller.Region;
            Scroller.Region = new Region(new Rectangle(Point.Empty, visible));
            if (old != null) old.Dispose();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (CloseOnEscape && e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (systemBorder) return;
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        private readonly HashSet<Control> revealed = new HashSet<Control>();

        /// <summary>
        /// Mostra uma linha escondida (ex.: o aviso de Raw Input indisponível) no lugar em que ela já está e empurra
        /// para baixo o que vem depois, com a janela junto. As contas usam a altura atual da linha: valem antes e depois
        /// da escala de DPI. designHeight e designGap são a altura da linha e o espaço abaixo dela, em px a 96 dpi.
        /// </summary>
        protected void RevealPushingDown(Control row, int designHeight, int designGap)
        {
            if (row == null || !revealed.Add(row)) return;
            float k = designHeight > 0 ? row.Height / (float)designHeight : 1f;
            int delta = row.Height + (int)Math.Round(designGap * k);
            SuspendLayout();
            foreach (Control c in Controls)
                if (c != row && c != TitleBar && c.Top >= row.Top) c.Top += delta;
            row.Visible = true;
            // Height, não ClientSize: durante a criação da janela o ClientSize ainda pode vir com a borda padrão.
            Height += delta;
            ResumeLayout(false);
        }

        /// <summary>Largura (px a 96 dpi) de um botão para o texto mais longo da lista, com folga, e no mínimo min.</summary>
        protected static int ButtonWidth(int min, params string[] texts)
        {
            int widest = 0;
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                using (var format = new StringFormat(StringFormatFlags.NoWrap))
                {
                    float k = g.DpiX / 96f;
                    foreach (string t in texts)
                        widest = Math.Max(widest, (int)Math.Ceiling(g.MeasureString(t ?? "", Theme.UiSemibold(Theme.TypeScale.Button), PointF.Empty, format).Width / k));
                }
            }
            catch
            {
                foreach (string t in texts) widest = Math.Max(widest, (t ?? "").Length * 8);
            }
            return Math.Max(min, widest + 40);
        }

        /// <summary>
        /// Texto simples no tema (para notas e descrições). O Label desenha a letra ~2 px depois da borda dele (margem do
        /// GDI): ele começa 2 px antes de x, para o texto alinhar com a borda dos cartões e com os cabeçalhos em x.
        /// </summary>
        protected static Label Note(string text, int x, int y, int w, int h, Color color, float size = 8.5f)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x - TextInset, y),
                Size = new Size(w, h),
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = Theme.Ui(size)
            };
        }

        /// <summary>Margem esquerda (px a 96 dpi) que o GDI põe antes do texto de um Label.</summary>
        protected const int TextInset = 2;

        /// <summary>Como Note, com a altura medida para o texto caber inteiro na largura (quebrado nos espaços).</summary>
        protected static Label MeasuredNote(string text, int x, int y, int w, Color color, float size = 8.5f, int minHeight = 0)
        {
            Label label = Note(text, x, y, w, 10, color, size);
            label.UseMnemonic = false;
            label.Height = Math.Max(minHeight, Theme.TextHeight(text, label.Font, w));
            return label;
        }
    }
}
