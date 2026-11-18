# 构建 BootTime.exe：生成图标 -> 编译 -> 创建桌面快捷方式
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'BootTime.cs'
$ico  = Join-Path $root 'app.ico'
$exe  = Join-Path $root 'BootTime.exe'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [float]([Math]::Max(1, $s * 0.02))
    $bg = New-RoundedPath $pad $pad ($s - 2 * $pad) ($s - 2 * $pad) ($s * 0.23)
    $bgBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(30, 30, 36))
    $g.FillPath($bgBrush, $bg)

    $ringW = [Math]::Max(1.0, $s * 0.052)
    $ringPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 209, 160)), $ringW
    $m = $s * 0.23
    $g.DrawEllipse($ringPen, $m, $m, ($s - 2 * $m), ($s - 2 * $m))

    $cx = $s / 2.0; $cy = $s / 2.0
    $handW = [Math]::Max(1.0, $s * 0.048)
    $handPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(238, 238, 244)), $handW
    $handPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $handPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($handPen, [float]$cx, [float]$cy, [float]$cx, [float]($cy - $s * 0.16))
    $g.DrawLine($handPen, [float]$cx, [float]$cy, [float]($cx + $s * 0.12), [float]($cy + $s * 0.02))

    $dotBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 209, 160))
    $dr = [Math]::Max(1.5, $s * 0.032)
    $g.FillEllipse($dotBrush, ($cx - $dr), ($cy - $dr), ($dr * 2), ($dr * 2))

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()

    $g.Dispose(); $bmp.Dispose(); $ms.Dispose(); $bg.Dispose()
    $bgBrush.Dispose(); $ringPen.Dispose(); $handPen.Dispose(); $dotBrush.Dispose()
    return , $bytes
}

$sizes = @(16, 32, 48, 256)
$pngs = @()
foreach ($s in $sizes) { $pngs += , (New-IconPng $s) }

$stream = [System.IO.File]::Open($ico, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($stream)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Flush(); $bw.Dispose(); $stream.Dispose()
Write-Host "icon  -> $ico"

& $csc /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 `
    /out:$exe /win32icon:$ico `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll `
    $src
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
Write-Host "exe   -> $exe"

$desktop = [Environment]::GetFolderPath('Desktop')
$lnkPath = Join-Path $desktop '开机时间.lnk'
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut($lnkPath)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $root
$lnk.IconLocation = "$exe,0"
$lnk.Description = '查看本次开机时间与已运行时长'
$lnk.Save()
Write-Host "shortcut -> $lnkPath"
