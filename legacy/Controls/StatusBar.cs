namespace PrimaryDisplaySwap.Controls;

internal enum StatusKind
{
    Neutral,
    Success,
    Error
}

internal sealed class StatusBar : Control
{
    private StatusKind _kind = StatusKind.Neutral;
    private string _message = string.Empty;
    private float _successPulse;
    private System.Windows.Forms.Timer? _pulseTimer;

    public StatusBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        Height = 32;
        Font = AppTheme.CaptionFont;
    }

    public void SetStatus(string message, bool? success = null)
    {
        _message = message;
        _kind = success switch
        {
            true => StatusKind.Success,
            false => StatusKind.Error,
            _ => StatusKind.Neutral
        };

        if (_kind == StatusKind.Success)
        {
            StartSuccessPulse();
        }
        else
        {
            StopSuccessPulse();
            _successPulse = 0f;
        }

        Invalidate();
    }

    public void Clear() => SetStatus(string.Empty);

    private void StartSuccessPulse()
    {
        _successPulse = 1f;
        _pulseTimer ??= new System.Windows.Forms.Timer { Interval = 40 };
        _pulseTimer.Tick -= OnPulseTick;
        _pulseTimer.Tick += OnPulseTick;
        if (!_pulseTimer.Enabled)
        {
            _pulseTimer.Start();
        }
    }

    private void StopSuccessPulse()
    {
        if (_pulseTimer is not null)
        {
            _pulseTimer.Stop();
        }
    }

    private void OnPulseTick(object? sender, EventArgs e)
    {
        _successPulse -= 0.06f;
        if (_successPulse <= 0f)
        {
            _successPulse = 0f;
            StopSuccessPulse();
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        AppTheme.ConfigureGraphics(g);

        if (string.IsNullOrEmpty(_message))
        {
            return;
        }

        var bounds = ClientRectangle;
        if (_kind == StatusKind.Success && _successPulse > 0f)
        {
            var alpha = (int)(28 * _successPulse);
            AppTheme.FillRoundedRect(g, bounds, 6, Color.FromArgb(alpha, AppTheme.Success));
        }

        var dotColor = _kind switch
        {
            StatusKind.Success => AppTheme.Success,
            StatusKind.Error => AppTheme.Error,
            _ => AppTheme.TextMuted
        };

        var textColor = _kind switch
        {
            StatusKind.Success => AppTheme.Success,
            StatusKind.Error => AppTheme.Error,
            _ => AppTheme.TextMuted
        };

        if (_kind == StatusKind.Success)
        {
            DrawSuccessCheck(g, new Rectangle(4, (Height - 16) / 2, 16, 16), dotColor);
        }
        else
        {
            var dotBounds = new Rectangle(6, (Height - 8) / 2, 8, 8);
            using var brush = new SolidBrush(dotColor);
            g.FillEllipse(brush, dotBounds);
        }

        var textLeft = _kind == StatusKind.Success ? 24 : 20;
        TextRenderer.DrawText(
            g,
            _message,
            Font,
            new Rectangle(textLeft, 0, Width - textLeft - 4, Height),
            textColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private static void DrawSuccessCheck(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 2f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        g.DrawLine(pen, bounds.Left + 3, bounds.Top + 8, bounds.Left + 6, bounds.Bottom - 4);
        g.DrawLine(pen, bounds.Left + 6, bounds.Bottom - 4, bounds.Right - 2, bounds.Top + 4);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pulseTimer?.Dispose();
        }

        base.Dispose(disposing);
    }
}
