#requires -Version 7.0
param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(-not $OutputDirectory) {$OutputDirectory=Join-Path $repo 'out\desktop-release'}
& dotnet publish (Join-Path $repo 'desktop\VirCre.Desktop\VirCre.Desktop.csproj') -c Release -r win-x64 --self-contained true -o $OutputDirectory --nologo
if($LASTEXITCODE -ne 0) {throw 'Desktop publish failed'}
$publishedDocs=Join-Path $OutputDirectory 'docs'
New-Item -ItemType Directory -Force -Path $publishedDocs | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo 'docs') | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $publishedDocs -Recurse -Force}
$guide='<meta charset="utf-8"><meta http-equiv="refresh" content="0;url=docs/desktop-shortcuts.html"><a href="docs/desktop-shortcuts.html">打开使用说明</a>'
Set-Content -LiteralPath (Join-Path $OutputDirectory '使用说明.html') -Value $guide -Encoding utf8
Write-Output "Ready: $OutputDirectory\VirCre.Desktop.exe"
Write-Output 'ADB and scrcpy 5.0.1+ must be available on PATH. Keep this folder in a stable location before creating shortcuts.'
