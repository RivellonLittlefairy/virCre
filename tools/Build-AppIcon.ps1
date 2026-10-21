#requires -Version 7.0
param([string]$SourceImage)
$ErrorActionPreference='Stop'
$repoDirectory=Split-Path $PSScriptRoot -Parent
$brandDirectory=Join-Path $repoDirectory 'desktop\VirCre.Desktop\Assets\Brand'
if(-not $SourceImage) {$SourceImage=Join-Path $brandDirectory 'vircre-icon-source.png'}
Add-Type -AssemblyName System.Drawing
$source=[Drawing.Image]::FromFile([IO.Path]::GetFullPath($SourceImage))
try {
    if($source.Width -ne $source.Height -or $source.Width -lt 1024) {throw 'The icon source must be square and at least 1024 pixels wide.'}
    New-Item -ItemType Directory -Path $brandDirectory -Force | Out-Null
    $sizes=@(16,20,24,32,40,48,64,96,128,256)
    $images=@()
    foreach($size in $sizes) {
        $bitmap=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics=[Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.CompositingMode=[Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($source,[Drawing.Rectangle]::new(0,0,$size,$size),0,0,$source.Width,$source.Height,[Drawing.GraphicsUnit]::Pixel)
            } finally {$graphics.Dispose()}
            $stream=[IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
                $images+=,$stream.ToArray()
                if($size -eq 256) {[IO.File]::WriteAllBytes((Join-Path $brandDirectory 'vircre-icon-256.png'),$stream.ToArray())}
            } finally {$stream.Dispose()}
        } finally {$bitmap.Dispose()}
    }
    $iconStream=[IO.MemoryStream]::new()
    $writer=[IO.BinaryWriter]::new($iconStream)
    try {
        $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
        $offset=6+16*$sizes.Count
        for($index=0;$index -lt $sizes.Count;$index++) {
            $dimension=if($sizes[$index] -eq 256) {0} else {$sizes[$index]}
            $writer.Write([byte]$dimension);$writer.Write([byte]$dimension)
            $writer.Write([byte]0);$writer.Write([byte]0)
            $writer.Write([uint16]1);$writer.Write([uint16]32)
            $writer.Write([uint32]$images[$index].Length);$writer.Write([uint32]$offset)
            $offset+=$images[$index].Length
        }
        foreach($image in $images) {$writer.Write([byte[]]$image)}
        $writer.Flush()
        [IO.File]::WriteAllBytes((Join-Path $brandDirectory 'vircre.ico'),$iconStream.ToArray())
    } finally {$writer.Dispose();$iconStream.Dispose()}
} finally {$source.Dispose()}
Write-Output ('Windows application icon exported: '+($sizes -join ', ')+' px')
