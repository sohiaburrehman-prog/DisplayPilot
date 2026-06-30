Add-Type -AssemblyName System.Drawing

function New-MockupPlaceholder {
    param(
        [string]$Path,
        [string]$Title,
        [string]$Subtitle,
        [int]$BgR,
        [int]$BgG,
        [int]$BgB,
        [string[]]$Lines
    )

    $w = 1280
    $h = 720
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.TextRenderingHint = 'ClearTypeGridFit'

    $bg = [System.Drawing.Color]::FromArgb(255, $BgR, $BgG, $BgB)
    $graphics.Clear($bg)

    # Window chrome
    $chrome = [System.Drawing.Color]::FromArgb(255, 28, 30, 42)
    $graphics.FillRectangle((New-Object System.Drawing.SolidBrush $chrome), 80, 60, 1120, 600)
    $graphics.DrawRectangle((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(80, 120, 200, 255), 1)), 80, 60, 1120, 600)

    $titleFont = New-Object System.Drawing.Font 'Segoe UI Semibold', 26
    $subFont = New-Object System.Drawing.Font 'Segoe UI', 13
    $lineFont = New-Object System.Drawing.Font 'Segoe UI', 12
    $badgeFont = New-Object System.Drawing.Font 'Segoe UI', 10

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 240, 248))
    $muted = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 170, 190))
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 140, 255))

    $graphics.DrawString($Title, $titleFont, $white, 110, 90)
    $graphics.DrawString($Subtitle, $subFont, $muted, 110, 130)

    # Placeholder badge
    $badge = 'PLACEHOLDER — replace with real Win+Shift+S capture'
    $badgeSize = $graphics.MeasureString($badge, $badgeFont)
    $graphics.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(180, 200, 80, 40))), 110, 165, $badgeSize.Width + 16, 24)
    $graphics.DrawString($badge, $badgeFont, $white, 118, 169)

    $y = 210
    foreach ($line in $Lines) {
        $graphics.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 255, 255, 255))), 110, $y, 1040, 44)
        $graphics.DrawString($line, $lineFont, $accent, 124, $y + 12)
        $y += 56
    }

    $dir = Split-Path $Path -Parent
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bmp.Dispose()
}

$base = Join-Path $PSScriptRoot '..\..\docs\screenshots'
New-MockupPlaceholder (Join-Path $base 'panel-displays.png') 'DisplayPilot - Displays' 'Monitor cards - arrangement map - resolution dropdowns' 22 24 36 @(
    'Monitor 1 - Dell U2720 - Primary - 3840 x 2160 - 60 Hz'
    'Monitor 2 - LG 27GL850 - 2560 x 1440 - 144 Hz'
    'Swap displays'
)
New-MockupPlaceholder (Join-Path $base 'panel-advanced.png') 'DisplayPilot - Advanced' 'Hotkeys - auto-swap summary - activity log' 24 26 40 @(
    'Open panel: Ctrl + Shift + M'
    '3 profiles active — Manage ›'
    'View activity log'
)
New-MockupPlaceholder (Join-Path $base 'settings-profiles.png') 'Profile manager' 'Auto-swap profiles - search - duplicate - Active now badge' 26 22 38 @(
    'steam -> eldenring  [Active now]  -> Gaming screen'
    'Search: elden'
    'Duplicate - Last triggered 6/30/2026 3:42 PM'
)
New-MockupPlaceholder (Join-Path $base 'wizard.png') 'Setup wizard' 'Pick primary monitor - enable auto-start' 20 32 44 @(
    'Step 2 of 3 - Choose your gaming monitor'
    'LG 27GL850 (recommended)'
    'Finish setup'
)
Write-Host 'Dark-theme mockup placeholders created.'
