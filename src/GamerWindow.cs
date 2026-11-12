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

        protected readonly TitleBar TitleBar;

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
                Title = title.ToUpperInvariant(),
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
            }
            catch { } // Windows 10 ou outro sistema: ignora
        }

        /// <summary>Área de rolagem criada quando a janela não cabe na tela; null enquanto couber.</summary>
        internal Panel Scroller { get; private set; }

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
            int scrollBar = SystemInformation.VerticalScrollBarWidth;
            var moving = new List<Control>();
            int lowest = bar;
            foreach (Control c in Controls)
            {
                if (c == TitleBar) continue;
                moving.Add(c);
                lowest = Math.Max(lowest, c.Bottom);
            }

            // O último controle cabe acima da borda de 1 px: basta perder parte da margem vazia, sem rolagem.
            if (lowest <= area.Height - 1)
            {
                Top = area.Top;
                Height = Math.Min(Height, area.Height);
                return false;
            }

            SuspendLayout();
            // Mais larga pela barra de rolagem: o conteúdo continua com a largura de antes.
            Width += scrollBar;
            Left = Math.Max(area.Left, Math.Min(Left - scrollBar / 2, area.Right - Width));
            Height = Math.Max(bar * 3, area.Height);
            Top = area.Top;

            // Dentro da borda de 1 px que a janela desenha.
            var panel = new Panel
            {
                AutoScroll = true,
                BackColor = BackColor,
                Bounds = new Rectangle(1, bar, ClientSize.Width - 2, ClientSize.Height - bar - 1),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                // A margem de baixo também rola: não para colado no último controle.
                AutoScrollMinSize = new Size(0, contentHeight)
            };
            panel.SuspendLayout();
            foreach (Control c in moving)
            {
                c.Location = new Point(c.Left - 1, c.Top - bar);
                panel.Controls.Add(c);
            }
            Controls.Add(panel);
            panel.ResumeLayout(false);
            ResumeLayout(true);
            Scroller = panel;
            return true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (CloseOnEscape && e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        /// <summary>Texto simples no tema (para notas e descrições).</summary>
        protected static Label Note(string text, int x, int y, int w, int h, Color color, float size = 8.5f)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = Theme.Ui(size)
            };
        }
    }
}
