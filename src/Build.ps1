$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compilerPath)) { $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $compilerPath)) { throw '未找到系统 .NET Framework C# 编译器。' }
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.Color]::FromArgb(23,40,36))
$mint = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(184,236,203))
$pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(184,236,203)),4
$pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
$graphics.FillRectangle($mint,15,35,7,14)
$graphics.FillRectangle($mint,29,27,7,22)
$graphics.FillRectangle($mint,43,17,7,32)
$graphics.DrawLine($pen,16,23,29,13)
$graphics.DrawLine($pen,29,13,37,15)
$stream = New-Object IO.MemoryStream
$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
$png = $stream.ToArray()
$iconStream = [IO.File]::Create((Join-Path $projectRoot 'app.ico'))
$writer = New-Object IO.BinaryWriter $iconStream
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
$writer.Write([byte]64); $writer.Write([byte]64); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22); $writer.Write($png)
$writer.Dispose(); $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $mint.Dispose(); $pen.Dispose()
$exe = Join-Path $projectRoot '上班鼓励器.exe'
& $compilerPath /nologo /target:winexe /optimize+ /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$exe" "/win32icon:$(Join-Path $projectRoot 'app.ico')" "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" (Join-Path $PSScriptRoot 'WorkEncourager.cs')
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Write-Output "已生成：$exe"
