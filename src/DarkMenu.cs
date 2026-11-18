using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>
    /// Menu da bandeja no tema escuro: o renderer, a fonte, a altura dos itens e os cantos arredondados do Windows 11.
    /// </summary>
    public static class DarkMenu
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWCP_ROUNDSMALL = 3;

        /// <summary>Altura de cada item, em px a 96 dpi.</summary>
        public const int ItemHeight = 32;

        /// <summary>Escala do Windows (1 = 100%), para os ícones e espaços que não acompanham a fonte sozinhos.</summary>
        internal static float Scale
        {
            get
            {
                try
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiY / 96f);
                }
                catch { return 1f; }
            }
        }

        /// <summary>Aplica o tema ao menu e aos submenus que ele já tem. Retorna o renderer (o modo atual vai nele).</summary>
        public static DarkMenuRenderer Apply(ToolStripDropDown menu)
        {
            var renderer = new DarkMenuRenderer();
            float k = Scale;
            int icon = (int)Math.Round(16 * k);
            Style(menu, renderer, k, new Size(icon, icon));
            return renderer;
        }

        private static void Style(ToolStripDropDown dropDown, DarkMenuRenderer renderer, float k, Size iconSize)
        {
            dropDown.Renderer = renderer;
            dropDown.Font = Theme.Ui(9.5f);
            dropDown.BackColor = Theme.Surface;
            dropDown.ForeColor = Theme.Text;
            dropDown.ImageScalingSize = iconSize;
            dropDown.Opening += (s, e) => Round(dropDown);

            // Altura do item: o texto e o espaço em volta somam cerca de 32 px (a 100%).
            int textH = TextRenderer.MeasureText("Ág", dropDown.Font).Height;
            int vertical = Math.Max(2, (int)Math.Round((ItemHeight * k - textH) / 2f) - 2); // o menu ainda soma 2 px em volta do texto
            foreach (ToolStripItem item in dropDown.Items)
            {
                if (item is ToolStripSeparator)
                {
                    item.AutoSize = false;
                    item.Height = (int)Math.Round(9 * k);
                    continue;
                }
                item.Padding = new Padding(item.Padding.Left, vertical, item.Padding.Right, vertical); // a largura o menu mede sozinho
                var menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.HasDropDownItems) Style(menuItem.DropDown, renderer, k, iconSize);
            }
        }

        /// <summary>Cantos arredondados pequenos e borda na cor do tema (Windows 11; no 10 a chamada falha e fica reto).</summary>
        private static void Round(ToolStripDropDown dropDown)
        {
            try
            {
                IntPtr handle = dropDown.Handle;
                int round = DWMWCP_ROUNDSMALL;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                int border = Theme.Border.R | (Theme.Border.G << 8) | (Theme.Border.B << 16);
                DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
            catch { }
        }

        /// <summary>Ícone de 16 px (na escala do Windows) para um item do menu, desenhado com o glifo do tema.</summary>
        public static Bitmap Icon(GlyphKind kind)
        {
            float k = Scale;
            int size = (int)Math.Round(16 * k);
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var brush = new SolidBrush(Theme.TextSecondary))
            {
                Theme.HighQuality(g);
                g.Clear(Color.Transparent);
                float inset = 1.5f * k;
                Theme.DrawGlyph(g, kind, new RectangleF(inset, inset, size - inset * 2, size - inset * 2), brush, 1.4f * k);
            }
            return bmp;
        }
    }

    /// <summary>
    /// Renderer escuro do menu: fundo Surface, seleção SurfaceHover arredondada e recuada, texto Text (desabilitado em
    /// TextTertiary), separadores Border e o item marcado com um ponto na cor do modo.
    /// </summary>
    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        /// <summary>Modo atual: cor do ponto dos itens marcados que não são de um modo (ex.: troca automática).</summary>
        public Mode CurrentMode { get; set; }

        public DarkMenuRenderer() : base(new DarkMenuColors())
        {
            RoundedEdges = false;
            CurrentMode = Mode.DiaADia;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // A margem dos ícones é a mesma superfície do menu: sem faixa de outra cor.
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!(e.Item.Selected || e.Item.Pressed)) return;
            Graphics g = e.Graphics;
            SmoothingMode before = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = g.DpiX / 96f;
            var r = new RectangleF(4 * k, 1, e.Item.Width - 8 * k, e.Item.Height - 2); // recuada 4 px das bordas do menu
            // Item desabilitado com o foco do teclado (setas): só o contorno, para o foco não sumir nele.
            if (e.Item.Enabled) Theme.FillRounded(g, r, Theme.Radius.Xs, Theme.SurfaceHover);
            else Theme.StrokeRounded(g, r, Theme.Radius.Xs, Theme.BorderStrong);
            g.SmoothingMode = before;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Desenha direto: o renderer padrão troca o desabilitado pelo cinza claro do sistema.
            Color color = e.Item.Enabled ? Theme.Text : Theme.TextTertiary;
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, color, e.TextFormat);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Graphics g = e.Graphics;
            SmoothingMode before = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = g.DpiX / 96f;
            Mode mode = e.Item.Tag is Mode ? (Mode)e.Item.Tag : CurrentMode;
            Color color = e.Item.Enabled ? Theme.Palette(mode).Fill : Theme.TextTertiary;
            float d = 7 * k;
            Rectangle r = IconRect(e.ImageRectangle, k);
            using (var b = new SolidBrush(color))
                g.FillEllipse(b, r.X + (r.Width - d) / 2f, r.Y + (r.Height - d) / 2f, d, d);
            g.SmoothingMode = before;
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Image == null) return;
            float k = e.Graphics.DpiX / 96f;
            Rectangle r = IconRect(e.ImageRectangle, k);
            if (e.Item.Enabled)
            {
                e.Graphics.DrawImage(e.Image, r);
                return;
            }
            // Desabilitado: o mesmo ícone mais apagado (o padrão do Windows o deixaria claro demais no fundo escuro).
            using (var attributes = new ImageAttributes())
            {
                var matrix = new ColorMatrix { Matrix33 = 0.45f };
                attributes.SetColorMatrix(matrix);
                e.Graphics.DrawImage(e.Image, r, 0, 0, e.Image.Width, e.Image.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        /// <summary>O Windows encosta o ícone na borda da seleção: um pouco mais para dentro.</summary>
        private static Rectangle IconRect(Rectangle r, float k)
        {
            r.Offset((int)Math.Round(6 * k), 0);
            return r;
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item != null && !e.Item.Enabled ? Theme.TextTertiary : Theme.TextSecondary;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            float k = e.Graphics.DpiX / 96f;
            int y = e.Item.Height / 2;
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawLine(pen, (int)(10 * k), y, e.Item.Width - (int)(10 * k), y);
        }
    }

    /// <summary>Cores de reserva do renderer escuro (as partes que ele não desenha à mão).</summary>
    internal class DarkMenuColors : ProfessionalColorTable
    {
        public DarkMenuColors()
        {
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.SurfaceHover; } }
        public override Color MenuItemSelected { get { return Theme.SurfaceHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.SurfaceHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.SurfaceHover; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.SurfaceHover; } }
        public override Color MenuItemPressedGradientMiddle { get { return Theme.SurfaceHover; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.SurfaceHover; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Surface; } }
        public override Color CheckBackground { get { return Theme.Surface; } }
        public override Color CheckSelectedBackground { get { return Theme.SurfaceHover; } }
        public override Color CheckPressedBackground { get { return Theme.SurfaceHover; } }
        public override Color ButtonSelectedBorder { get { return Theme.SurfaceHover; } }
    }
}
