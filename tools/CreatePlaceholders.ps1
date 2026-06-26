Add-Type -AssemblyName System.Drawing

function New-Placeholder {
    param(
        [string]$Path,
        [string]$Label,
        [int]$R,
        [int]$G,
        [int]$B
    )

    $w = 960
    $h = 540
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    $graphics.SmoothingMode = 'AntiAlias'
    $bg = [System.Drawing.Color]::FromArgb(255, $R, $G, $B)
    $graphics.Clear($bg)
    $font = New-Object System.Drawing.Font 'Segoe UI', 28, ([System.Drawing.FontStyle]::Bold)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 255, 255, 255))
    $size = $graphics.MeasureString($Label, $font)
    $graphics.DrawString($Label, $font, $brush, ($w - $size.Width) / 2, ($h - $size.Height) / 2)
    $sub = New-Object System.Drawing.Font 'Segoe UI', 14
    $subBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(180, 255, 255, 255))
    $subText = 'DisplayPilot placeholder - replace with Win+Shift+S capture'
    $subSize = $graphics.MeasureString($subText, $sub)
    $graphics.DrawString($subText, $sub, $subBrush, ($w - $subSize.Width) / 2, ($h + $size.Height) / 2)
    $dir = Split-Path $Path -Parent
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bmp.Dispose()
}

$base = Join-Path $PSScriptRoot '..\..\docs\screenshots'
New-Placeholder (Join-Path $base 'panel-displays.png') 'panel-displays' 35 38 58
New-Placeholder (Join-Path $base 'panel-advanced.png') 'panel-advanced' 42 45 72
New-Placeholder (Join-Path $base 'settings-profiles.png') 'settings-profiles' 48 40 62
New-Placeholder (Join-Path $base 'wizard.png') 'wizard' 38 52 68
Write-Host 'Placeholder PNGs created.'
