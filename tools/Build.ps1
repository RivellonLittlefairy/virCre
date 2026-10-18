param([switch]$SkipLint)
$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path $PSScriptRoot -Parent) 'android'
Push-Location $project
try {
    $tasks = @(':app:assembleDebug', ':app:testDebugUnitTest')
    if (-not $SkipLint) { $tasks += ':app:lintDebug' }
    & .\gradlew.bat @tasks --console=plain
    if ($LASTEXITCODE -ne 0) { throw "Gradle failed ($LASTEXITCODE)" }
} finally { Pop-Location }
