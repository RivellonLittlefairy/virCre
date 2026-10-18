#requires -Version 7.0
param([switch]$NoBuild, [switch]$CameraLab)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$projectName=if($CameraLab){'VirCam.Desktop'}else{'VirCre.Desktop'}
$frameworkPath=if($CameraLab){'net8.0-windows'}else{'net8.0-windows10.0.19041.0\win-x64'}
$project=Join-Path $repoRoot "desktop\$projectName\$projectName.csproj"
if(-not $NoBuild) {
    & dotnet build $project --nologo
    if($LASTEXITCODE -ne 0) { throw 'Windows build failed' }
}
$app=Join-Path $repoRoot "desktop\$projectName\bin\Debug\$frameworkPath\$projectName.exe"
if(-not (Test-Path -LiteralPath $app)) { throw 'Windows executable missing' }
$start=[Diagnostics.ProcessStartInfo]::new($app)
$start.UseShellExecute=$false
if($CameraLab) {
    $start.ArgumentList.Add($repoRoot)
    $start.ArgumentList.Add((Get-Process -Id $PID).Path)
}
[Diagnostics.Process]::Start($start) | Out-Null
