namespace PrimaryDisplaySwap;

/// <summary>
/// Brief on-screen reminder shown when the control panel is hidden.
/// </summary>
internal sealed class RestoreHintForm : Form
{
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private int _ticksRemaining = 30;

    public RestoreHintForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(28, 32, 40);
        Size = new Size(360, 56);
        Opacity = 0.95;

        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Hidden — press Ctrl+Shift+M to restore",
            ForeColor = Color.FromArgb(230, 235, 245),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point)
        };

        Controls.Add(label);

        _fadeTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _fadeTimer.Tick += (_, _) =>
        {
            _ticksRemaining--;
            if (_ticksRemaining <= 10)
            {
                Opacity = Math.Max(0, _ticksRemaining / 10d * 0.95);
            }

            if (_ticksRemaining <= 0)
            {
                _fadeTimer.Stop();
                Close();
            }
        };
    }

    public static void ShowBriefHint()
    {
        var form = new RestoreHintForm();
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
        form.Location = new Point(
            workArea.Right - form.Width - 16,
            workArea.Bottom - form.Height - 16);
        form.Show();
        form._fadeTimer.Start();
    }
}
