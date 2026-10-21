#requires -Version 7.0
param(
    [ValidatePattern('^\d+\.\d+(?:\.\d+)?$')][string]$Version,
    [string]$Compiler,
    [string]$OutputDirectory,
    [switch]$SkipRuntimeBuild
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$Version=& (Join-Path $PSScriptRoot 'Get-ProjectVersion.ps1') -Version $Version
$parsedVersion=[Version]$Version
$patchVersion=[Math]::Max(0,$parsedVersion.Build)
$expectedFileVersion="$($parsedVersion.Major).$($parsedVersion.Minor).$patchVersion.0"
if(-not $OutputDirectory) {$OutputDirectory=Join-Path $repo 'out\installers'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(-not $Compiler) {
    $candidates=@((Join-Path $repo '.cache\toolchains\inno-setup-6.7.3\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'))
    $Compiler=$candidates | Where-Object {Test-Path -LiteralPath $_ -PathType Leaf} | Select-Object -First 1
}
if(-not $Compiler -or -not (Test-Path -LiteralPath $Compiler -PathType Leaf)) {
    throw 'Inno Setup 6 compiler not found. Install it or pass -Compiler; see docs/windows-packaging.html.'
}
$Compiler=(Resolve-Path -LiteralPath $Compiler).Path
# Workspace-extracted tools can inherit Low MIC. A Low compiler cannot replace
# the Medium-labelled installer from the previous build. Scope this to our cache.
if($Compiler.StartsWith((Join-Path $repo '.cache')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
    & icacls $Compiler /setintegritylevel M | Out-Null
    if($LASTEXITCODE -ne 0) {throw 'Cannot normalize workspace compiler executable integrity'}
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
if(-not $SkipRuntimeBuild) {
    & (Join-Path $PSScriptRoot 'Build-Scrcpy.ps1') -BuildType release -SkipTests
}
# Each run gets a clean staging directory. Never package the checkout, device logs or build caches.
$publish=Join-Path $repo ('out\package-staging-'+[Guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'Publish-Desktop.ps1') -Runtime Custom -SkipTests -Version $Version -OutputDirectory $publish -GuidePage virtual-camera-window.html
foreach($required in @('VirCre.Desktop.exe','VirCre.Desktop.dll','VirCre.Desktop.pri','VirCre.Desktop.runtimeconfig.json',
    'Microsoft.UI.Xaml.dll','Assets\icon-agent.jar','Assets\Brand\vircre.ico','Assets\Brand\vircre-icon-256.png','runtime\scrcpy\scrcpy.exe','runtime\scrcpy\scrcpy-server',
    'runtime\scrcpy\adb.exe','runtime\scrcpy\licenses','使用说明.html')) {
    if(-not (Test-Path -LiteralPath (Join-Path $publish $required))) {throw "Missing publish resource: $required"}
}
& (Join-Path $PSScriptRoot 'Prepare-CustomScrcpyRuntime.ps1') -RuntimeDirectory (Join-Path $publish 'runtime\scrcpy')
$manifest=Get-Content -LiteralPath (Join-Path $publish 'runtime\scrcpy\runtime-manifest.json') -Raw | ConvertFrom-Json
foreach($feature in @('windows-ime-v1','window-capture-exclusion-v1','vdm-direct-encoder-v1','camera-hd-jpeg-v1','camera-display-size-v1','camera-live-move-v1')) {
    if($manifest.features -notcontains $feature) {throw "Missing runtime feature: $feature"}
}
if($manifest.buildType -ne 'release') {throw 'The installer requires a release build of the custom runtime.'}
$fileVersion=(Get-Item -LiteralPath (Join-Path $publish 'VirCre.Desktop.exe')).VersionInfo.FileVersion
if($fileVersion -ne $expectedFileVersion) {throw "Published version does not match installer: $fileVersion"}
if(Get-ChildItem -LiteralPath $publish -Filter '*.apk' -File -Recurse) {throw 'A test APK must not be included in the desktop package.'}
if(Get-ChildItem -LiteralPath (Join-Path $publish 'docs') -Filter 'code-review-*.md' -File -Recurse) {throw 'Local code review notes must not be included in the desktop package.'}
& (Join-Path $PSScriptRoot 'Test-DistributionCompliance.ps1') -OutputDirectory $publish
& $Compiler "/DPublishDir=$publish" "/DSetupDir=$OutputDirectory" "/DAppVersion=$Version" "/DAppFileVersion=$expectedFileVersion" (Join-Path $repo 'packaging\virCre.iss')
if($LASTEXITCODE -ne 0) {throw 'Inno Setup compilation failed'}
$setup=Join-Path $OutputDirectory "virCre-$Version-win-x64-setup.exe"
if(-not (Test-Path -LiteralPath $setup -PathType Leaf)) {throw 'Installer output is missing'}
& icacls $setup /setintegritylevel M | Out-Null
if($LASTEXITCODE -ne 0) {throw 'Cannot normalize generated installer executable integrity'}
$zip=Join-Path $OutputDirectory "virCre-$Version-win-x64-portable.zip"
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $publish -Force | Select-Object -ExpandProperty FullName) -DestinationPath $zip -Force
$hashes=@($setup,$zip) | ForEach-Object {(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_)}
$hashFile=Join-Path $OutputDirectory "virCre-$Version-SHA256SUMS.txt"
$hashes | Set-Content -LiteralPath $hashFile -Encoding utf8NoBOM
[ordered]@{version=$Version;fileVersion=$expectedFileVersion;publishDirectory=$publish;installer=$setup;portable=$zip;checksums=$hashFile;
    runtimeSourceFingerprint=$manifest.sourceFingerprint;validation='compiled-and-file-inspected; user installation and device checks pending'} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory "virCre-$Version-build.json") -Encoding utf8
Write-Output "Installer: $setup"
Write-Output "Portable: $zip"
Write-Output "Checksums: $hashFile"
