using System.Drawing.Drawing2D;

namespace Porkn.Windows;

internal static class UiTheme
{
    public static readonly Color WindowBackground = Color.FromArgb(246, 246, 248);
    public static readonly Color SidebarBackground = Color.FromArgb(239, 240, 244);
    public static readonly Color CardBackground = Color.FromArgb(255, 255, 255);
    public static readonly Color SubtleCardBackground = Color.FromArgb(250, 250, 252);
    public static readonly Color Border = Color.FromArgb(222, 224, 230);
    public static readonly Color PrimaryText = Color.FromArgb(29, 29, 31);
    public static readonly Color SecondaryText = Color.FromArgb(105, 105, 112);
    public static readonly Color TertiaryText = Color.FromArgb(145, 145, 154);
    public static readonly Color Blue = Color.FromArgb(0, 112, 245);
    public static readonly Color BlueHover = Color.FromArgb(18, 126, 255);
    public static readonly Color BluePressed = Color.FromArgb(0, 92, 210);
    public static readonly Color Green = Color.FromArgb(36, 156, 82);
    public static readonly Color GreenHover = Color.FromArgb(42, 176, 94);
    public static readonly Color GreenPressed = Color.FromArgb(30, 126, 67);
    public static readonly Color Red = Color.FromArgb(204, 59, 59);
    public static readonly Color Warning = Color.FromArgb(214, 139, 21);

    public static Font Font(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style, GraphicsUnit.Point);
    public static Font Mono(float size, FontStyle style = FontStyle.Regular) => new("Cascadia Mono", size, style, GraphicsUnit.Point);
}

internal static class DrawingExtensions
{
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedPanel : Panel
{
    public int CornerRadius { get; set; } = 18;
    public Color FillColor { get; set; } = UiTheme.CardBackground;
    public Color BorderColor { get; set; } = UiTheme.Border;
    public bool DrawBorder { get; set; } = true;

    public RoundedPanel()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (ClientSize.Width <= 1 || ClientSize.Height <= 1) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;

        using var fill = new SolidBrush(FillColor);
        using var path = DrawingExtensions.RoundedRect(bounds, CornerRadius);
        e.Graphics.FillPath(fill, path);

        if (DrawBorder)
        {
            using var pen = new Pen(BorderColor, 1f);
            e.Graphics.DrawPath(pen, path);
        }
    }
}

internal sealed class RoundedButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public int CornerRadius { get; set; } = 13;
    public Color NormalColor { get; set; } = UiTheme.Blue;
    public Color HoverColor { get; set; } = UiTheme.BlueHover;
    public Color PressedColor { get; set; } = UiTheme.BluePressed;
    public Color DisabledColor { get; set; } = Color.FromArgb(226, 227, 232);
    public Color BorderColor { get; set; } = Color.Transparent;
    public Color TextColor { get; set; } = Color.White;
    public Color DisabledTextColor { get; set; } = Color.FromArgb(145, 145, 154);
    public bool DrawButtonBorder { get; set; }

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        TabStop = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (ClientSize.Width <= 1 || ClientSize.Height <= 1) return;

        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;

        var color = !Enabled ? DisabledColor : _pressed ? PressedColor : _hovered ? HoverColor : NormalColor;
        using var brush = new SolidBrush(color);
        using var path = DrawingExtensions.RoundedRect(bounds, CornerRadius);
        pevent.Graphics.FillPath(brush, path);

        if (DrawButtonBorder)
        {
            using var pen = new Pen(BorderColor, 1f);
            pevent.Graphics.DrawPath(pen, path);
        }

        var textColor = Enabled ? TextColor : DisabledTextColor;
        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            ClientRectangle,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
