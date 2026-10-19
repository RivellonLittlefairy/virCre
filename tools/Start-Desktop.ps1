#requires -Version 7.0
param([switch]$NoBuild)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$projectName='VirCre.Desktop'
$frameworkPath='net8.0-windows10.0.19041.0\win-x64'
$project=Join-Path $repoRoot "desktop\$projectName\$projectName.csproj"
if(-not $NoBuild) {
    & dotnet build $project --nologo -p:ScrcpyRuntimeVariant=Custom
    if($LASTEXITCODE -ne 0) { throw 'Windows build failed' }
}
$app=Join-Path $repoRoot "desktop\$projectName\bin\Debug\$frameworkPath\$projectName.exe"
if(-not (Test-Path -LiteralPath $app)) { throw 'Windows executable missing' }
$start=[Diagnostics.ProcessStartInfo]::new($app)
$start.UseShellExecute=$false
[Diagnostics.Process]::Start($start) | Out-Null
