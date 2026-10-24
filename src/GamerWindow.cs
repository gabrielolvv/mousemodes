using System;
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
