#requires -Version 7.0
param([string]$OutputDirectory,[switch]$SmokeTest)
$ErrorActionPreference='Stop'
$repoDirectory=Split-Path $PSScriptRoot -Parent
if(-not $OutputDirectory) {$OutputDirectory=Join-Path $repoDirectory 'out\desktop-release'}
# A publish that lacks the application's PRI builds successfully but crashes while loading MainWindow.
foreach($relativePath in @('VirCre.Desktop.exe','VirCre.Desktop.dll','VirCre.Desktop.pri','Microsoft.UI.Xaml.dll','runtime\scrcpy\runtime-manifest.json')) {
    $publishedFile=Join-Path $OutputDirectory $relativePath
    if(-not (Test-Path -LiteralPath $publishedFile -PathType Leaf) -or (Get-Item -LiteralPath $publishedFile).Length -eq 0) {
        throw "Desktop publish is incomplete: missing or empty $relativePath"
    }
}
Write-Output 'PASS: published desktop includes its XAML resource index, application and bundled runtime'
if($SmokeTest) {
    # Otherwise single-instance activation could redirect to an old build and falsely pass this test.
    if(Get-Process -Name 'VirCre.Desktop' -ErrorAction SilentlyContinue) {throw 'Close the existing virCre window before running the startup smoke test'}
    $publishRoot=[IO.Path]::GetFullPath($OutputDirectory)
    $probe=Start-Process -FilePath (Join-Path $publishRoot 'VirCre.Desktop.exe') -WorkingDirectory $publishRoot -PassThru -WindowStyle Hidden
    try {
        $deadline=[DateTime]::UtcNow.AddSeconds(15)
        do {
            $probe.Refresh()
            if($probe.HasExited) {throw ('Published desktop exited before showing its window: '+$probe.ExitCode)}
            if($probe.MainWindowHandle -ne 0 -and $probe.Responding) {break}
            Start-Sleep -Milliseconds 100
        } while([DateTime]::UtcNow -lt $deadline)
        if($probe.MainWindowHandle -eq 0) {throw 'Published desktop did not create its main window within 15 seconds'}
        if($probe.WaitForExit(2000)) {throw ('Published desktop exited just after startup: '+$probe.ExitCode)}
        $probe.Refresh()
        if(-not $probe.Responding) {throw 'Published desktop window stopped responding'}
        Write-Output ('PASS: published WinUI main window starts and responds: '+$probe.MainWindowTitle)
    } finally {
        if(-not $probe.HasExited) {
            if(-not $probe.CloseMainWindow() -or -not $probe.WaitForExit(15000)) {
                throw ('Startup smoke test could not close its window normally; process remains owned by PID '+$probe.Id)
            }
        }
        $probe.Dispose()
    }
}
