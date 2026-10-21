#requires -Version 7.0
param([string]$RuntimeDirectory)
$ErrorActionPreference='Stop'
$repoDirectory=Split-Path $PSScriptRoot -Parent
if(-not $RuntimeDirectory) {$RuntimeDirectory=Join-Path $repoDirectory '.cache\custom-runtime\scrcpy'}
$RuntimeDirectory=[IO.Path]::GetFullPath($RuntimeDirectory)
$manifest=Get-Content -LiteralPath (Join-Path $RuntimeDirectory 'runtime-manifest.json') -Raw | ConvertFrom-Json
if($manifest.distribution -ne 'vircre-source-build' -or $manifest.features -notcontains 'windows-ime-v1') {
    throw 'Build the custom Windows IME runtime first with tools/Build-Scrcpy.ps1'
}
if($manifest.androidMinApi -ne 29 -or $manifest.virtualCameraMinApi -ne 36) {
    throw 'Rebuild with tools/Build-Scrcpy.ps1: ordinary windows require the Android 10 compatible server; virtual camera requires Android 16.'
}
foreach($required in @('scrcpy.exe','scrcpy-server','adb.exe','AdbWinApi.dll','AdbWinUsbApi.dll','SDL3.dll','avcodec-63.dll','avformat-63.dll','avutil-61.dll','swresample-7.dll','libusb-1.0.dll')) {
    if(-not $manifest.files.$required) {throw "Custom runtime manifest is missing $required"}
}
foreach($file in $manifest.files.PSObject.Properties) {
    $path=[IO.Path]::GetFullPath((Join-Path $RuntimeDirectory $file.Name))
    if(-not $path.StartsWith($RuntimeDirectory+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Invalid custom runtime file path'}
    if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Value) {
        throw ('Custom runtime is incomplete or changed: '+$file.Name)
    }
}
Write-Output ('Using paired custom client/server: '+$RuntimeDirectory)
