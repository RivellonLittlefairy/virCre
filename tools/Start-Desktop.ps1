#requires -Version 7.0
param([switch]$NoBuild)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$project=Join-Path $repoRoot 'desktop\VirCam.Desktop\VirCam.Desktop.csproj'
if(-not $NoBuild) {
    & dotnet build $project --nologo
    if($LASTEXITCODE -ne 0) { throw 'Windows build failed' }
}
$app=Join-Path $repoRoot 'desktop\VirCam.Desktop\bin\Debug\net8.0-windows\VirCam.Desktop.exe'
if(-not (Test-Path -LiteralPath $app)) { throw 'Windows executable missing' }
$start=[Diagnostics.ProcessStartInfo]::new($app)
$start.UseShellExecute=$false
$start.ArgumentList.Add($repoRoot)
$start.ArgumentList.Add((Get-Process -Id $PID).Path)
[Diagnostics.Process]::Start($start) | Out-Null
