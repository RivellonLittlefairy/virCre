param([switch]$SkipLint)
$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path $PSScriptRoot -Parent) 'android'
if(-not (Test-Path -LiteralPath $project)) {
    throw 'The Android probe APK project has been retired. Use tools/Build-Installer.ps1 for the desktop release.'
}
Push-Location $project
try {
    $tasks = @(':app:assembleDebug', ':app:testDebugUnitTest')
    if (-not $SkipLint) { $tasks += ':app:lintDebug' }
    & .\gradlew.bat @tasks --console=plain
    if ($LASTEXITCODE -ne 0) { throw "Gradle failed ($LASTEXITCODE)" }
} finally { Pop-Location }
