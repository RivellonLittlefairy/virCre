#requires -Version 7.0
param([string]$RuntimeDirectory,[switch]$RequireWindowsIme)
$ErrorActionPreference='Stop'
$repoDirectory=Split-Path $PSScriptRoot -Parent
if(-not $RuntimeDirectory) {$RuntimeDirectory=Join-Path $repoDirectory '.cache\custom-runtime\scrcpy'}
$manifestPath=Join-Path $RuntimeDirectory 'runtime-manifest.json'
if(-not(Test-Path -LiteralPath $manifestPath)) {throw 'Custom scrcpy runtime has not been built'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if($manifest.distribution -ne 'vircre-source-build') {throw 'Custom runtime provenance is missing'}
foreach($file in $manifest.files.PSObject.Properties) {
    $filePath=Join-Path $RuntimeDirectory $file.Name
    if(-not(Test-Path -LiteralPath $filePath) -or (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash -ne $file.Value) {throw ('Custom runtime hash mismatch: '+$file.Name)}
}
foreach($required in @('scrcpy.exe','scrcpy-server','SDL3.dll','adb.exe')) {
    if(-not $manifest.files.$required) {throw ('Custom runtime is missing '+$required)}
}
$probeInfo=[Diagnostics.ProcessStartInfo]::new((Join-Path $RuntimeDirectory 'scrcpy.exe'))
$probeInfo.UseShellExecute=$false;$probeInfo.CreateNoWindow=$true
$probeInfo.RedirectStandardOutput=$true;$probeInfo.RedirectStandardError=$true
$probeInfo.Environment['PATH']=Join-Path $env:SystemRoot 'System32'
$probeInfo.ArgumentList.Add('--version')
$probe=[Diagnostics.Process]::Start($probeInfo)
try {
    $output=$probe.StandardOutput.ReadToEndAsync();$errorOutput=$probe.StandardError.ReadToEndAsync()
    if(-not $probe.WaitForExit(15000)) {$probe.Kill();throw 'Custom scrcpy version probe timed out'}
    $version=$output.GetAwaiter().GetResult()+$errorOutput.GetAwaiter().GetResult()
    if($probe.ExitCode -ne 0 -or $version -notmatch 'scrcpy 5\.0\.1') {throw ('Custom scrcpy failed without toolchain PATH: exit '+$probe.ExitCode)}
} finally {$probe.Dispose()}
Write-Output 'PASS: custom client runs with no compiler or user-tool PATH; paired server and file hashes verified'
if($RequireWindowsIme) {
    $help=& (Join-Path $RuntimeDirectory 'scrcpy.exe') --help
    if($LASTEXITCODE -ne 0 -or ($help -join "`n") -notmatch '\-\-windows-ime') {throw 'Custom runtime does not expose --windows-ime'}
    Write-Output 'PASS: Windows IME mode is available explicitly'
}
