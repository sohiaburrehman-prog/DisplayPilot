namespace PrimaryDisplaySwap.Controls;

internal sealed class ModernCard : Panel
{
    public ModernCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        Padding = new Padding(12, 10, 12, 10);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        AppTheme.ConfigureGraphics(g);

        var bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;

        AppTheme.FillRoundedRect(g, bounds, AppTheme.CardCornerRadius, AppTheme.Surface);
        AppTheme.DrawRoundedRect(g, bounds, AppTheme.CardCornerRadius, AppTheme.BorderSubtle);
    }
}
