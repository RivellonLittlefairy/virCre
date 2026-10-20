#requires -Version 7.0
param(
    [string]$ToolchainDirectory,
    [string]$Sdk="$env:LOCALAPPDATA\Android\Sdk",
    [string]$Java='C:\Program Files\Android\Android Studio\jbr',
    [ValidateSet('debug','release')][string]$BuildType='debug',
    [switch]$SkipTests
)
$ErrorActionPreference='Stop'
$repoDirectory=Split-Path $PSScriptRoot -Parent
if(-not $ToolchainDirectory) {$ToolchainDirectory=Join-Path $repoDirectory '.cache\toolchains\msys64\ucrt64'}
$ToolchainDirectory=[IO.Path]::GetFullPath($ToolchainDirectory)
$toolBin=Join-Path $ToolchainDirectory 'bin'
foreach($tool in @('cc.exe','meson.exe','ninja.exe','pkgconf.exe')) {
    if(-not(Test-Path -LiteralPath (Join-Path $toolBin $tool))) {throw "Portable UCRT64 toolchain is missing $tool; see docs/windows-ime-plan.html"}
}
# MSYS2 package aliases may fail to materialize as hard links on Windows. Copy only the compiler alias needed by windres.
if(-not(Test-Path -LiteralPath (Join-Path $toolBin 'gcc.exe'))) {Copy-Item -LiteralPath (Join-Path $toolBin 'cc.exe') -Destination (Join-Path $toolBin 'gcc.exe')}
if(-not(Test-Path -LiteralPath (Join-Path $toolBin 'ld.exe'))) {Copy-Item -LiteralPath (Join-Path $toolBin 'ld.bfd.exe') -Destination (Join-Path $toolBin 'ld.exe')}
$pluginSource=Join-Path $ToolchainDirectory 'lib\bfd-plugins\liblto_plugin.dll'
if(Test-Path -LiteralPath $pluginSource) {
    Get-ChildItem -LiteralPath (Join-Path $ToolchainDirectory 'lib\gcc\x86_64-w64-mingw32') -Directory | ForEach-Object {
        $pluginAlias=Join-Path $_.FullName 'liblto_plugin.dll'
        if(-not(Test-Path -LiteralPath $pluginAlias)) {Copy-Item -LiteralPath $pluginSource -Destination $pluginAlias}
    }
}
$sourceDirectory=Join-Path $repoDirectory 'third_party\scrcpy'
$buildDirectory=Join-Path $repoDirectory ('.cache\scrcpy-build\'+$BuildType)
$runtimeDirectory=Join-Path $repoDirectory '.cache\custom-runtime\scrcpy'
& (Join-Path $PSScriptRoot 'Prepare-ScrcpyRuntime.ps1')
$officialDirectory=Join-Path $repoDirectory '.cache\bundled-runtime\scrcpy'
$previousEnvironment=@{}
foreach($name in @('PATH','CC','PKG_CONFIG','PKG_CONFIG_LIBDIR','TEMP','TMP')) {$previousEnvironment[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $compilerTemp=Join-Path $repoDirectory '.cache\scrcpy-build\tmp'
    New-Item -ItemType Directory -Path $compilerTemp -Force | Out-Null
    $env:TEMP=$compilerTemp;$env:TMP=$compilerTemp
    $env:PATH=$toolBin+[IO.Path]::PathSeparator+$env:PATH
    $env:CC=Join-Path $toolBin 'cc.exe'
    $env:PKG_CONFIG=Join-Path $toolBin 'pkgconf.exe'
    $env:PKG_CONFIG_LIBDIR=Join-Path $ToolchainDirectory 'lib\pkgconfig'
    $meson=Join-Path $toolBin 'meson.exe'
    $setupArgs=@('setup',$buildDirectory,$sourceDirectory,('--buildtype='+$BuildType),'-Dcompile_server=false','-Dportable=true')
    if(Test-Path -LiteralPath (Join-Path $buildDirectory 'build.ninja')) {$setupArgs+='--reconfigure'}
    & $meson @setupArgs
    if($LASTEXITCODE -ne 0) {throw 'scrcpy Meson setup failed'}
    & $meson compile -C $buildDirectory scrcpy
    if($LASTEXITCODE -ne 0) {throw 'scrcpy client build failed'}
    # Test against the same verified runtime DLLs that will ship, rather than the compiler environment's FFmpeg distribution.
    Get-ChildItem -LiteralPath $officialDirectory -Filter '*.dll' | Copy-Item -Destination (Join-Path $buildDirectory 'app') -Force
    if($BuildType -eq 'debug' -and -not $SkipTests) {
        & $meson test -C $buildDirectory --print-errorlogs
        if($LASTEXITCODE -ne 0) {throw 'scrcpy client tests failed'}
    }

    $serverBuild=Join-Path $buildDirectory 'server-build'
    $generated=Join-Path $serverBuild ('gen-'+[Guid]::NewGuid().ToString('N'))
    $classes=Join-Path $generated 'classes'
    $configDirectory=Join-Path $generated 'com\genymobile\scrcpy'
    New-Item -ItemType Directory -Force -Path $classes,$configDirectory | Out-Null
    $version='5.0.1'
    "package com.genymobile.scrcpy; public final class BuildConfig { public static final boolean DEBUG=false; public static final String VERSION_NAME=`"$version`"; }" | Set-Content -LiteralPath (Join-Path $configDirectory 'BuildConfig.java') -Encoding utf8
    $buildTools=Get-ChildItem -LiteralPath (Join-Path $Sdk 'build-tools') -Directory | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
    $androidJar=Join-Path $Sdk 'platforms\android-36\android.jar'
    $frameworkAidl=Join-Path $Sdk 'platforms\android-36\framework.aidl'
    $aidl=Join-Path $buildTools.FullName 'aidl.exe'
    $lambdaJar=Join-Path $buildTools.FullName 'core-lambda-stubs.jar'
    $aidlSource=Join-Path $sourceDirectory 'server\src\main\aidl'
    & $aidl ('-o'+$generated) ('-I'+$aidlSource) (Join-Path $aidlSource 'android\content\IOnPrimaryClipChangedListener.aidl')
    if($LASTEXITCODE -ne 0) {throw 'Clipboard AIDL compilation failed'}
    & $aidl ('-o'+$generated) ('-I'+$aidlSource) '-p' $frameworkAidl (Join-Path $aidlSource 'android\view\IDisplayWindowListener.aidl')
    if($LASTEXITCODE -ne 0) {throw 'Display AIDL compilation failed'}
    $cameraSourceRoot=Join-Path $repoDirectory 'camera-agent\src\main\java\com\example\vircam'
    $cameraSources=@(Get-ChildItem -LiteralPath (Join-Path $cameraSourceRoot 'shell') -Filter '*.java')
    foreach($cameraCore in @('FrameProtocol.java','LatestFrame.java','FrameMarker.java')) {$cameraSources+=Get-Item -LiteralPath (Join-Path $cameraSourceRoot "core\$cameraCore")}
    $javaSources=@(Get-ChildItem -LiteralPath (Join-Path $sourceDirectory 'server\src\main\java'),$generated -Filter '*.java' -Recurse | ForEach-Object {'"'+$_.FullName.Replace('\','/')+'"'})
    $javaSources+=@($cameraSources | ForEach-Object {'"'+$_.FullName.Replace('\','/')+'"'})
    $javaArgs=Join-Path $serverBuild 'javac.args'
    $javaSources | Set-Content -LiteralPath $javaArgs -Encoding utf8
    & (Join-Path $Java 'bin\javac.exe') -encoding UTF-8 -source 8 -target 8 -Xlint:-options -bootclasspath $androidJar -classpath ($lambdaJar+';'+$generated) -d $classes ('@'+$javaArgs)
    if($LASTEXITCODE -ne 0) {throw 'scrcpy server Java compilation failed'}
    $dexArgs=Join-Path $serverBuild 'd8.args'
    # D8 treats each response-file line as one literal argument; unlike javac, it does not unquote paths.
    $dexArguments=@('--min-api','36','--lib',$androidJar.Replace('\','/'),'--output',(Join-Path $serverBuild 'scrcpy-server.zip').Replace('\','/'))
    $dexArguments+=Get-ChildItem -LiteralPath $classes -Filter '*.class' -Recurse | ForEach-Object {$_.FullName.Replace('\','/')}
    $dexArguments | Set-Content -LiteralPath $dexArgs -Encoding utf8
    & (Join-Path $Java 'bin\java.exe') -cp (Join-Path $buildTools.FullName 'lib\d8.jar') com.android.tools.r8.D8 ('@'+$dexArgs)
    if($LASTEXITCODE -ne 0) {throw 'scrcpy server dex compilation failed'}

    New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
    Get-ChildItem -LiteralPath $officialDirectory | Where-Object {$_.Name -ne 'runtime-manifest.json'} | Copy-Item -Destination $runtimeDirectory -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $buildDirectory 'app\scrcpy.exe') -Destination (Join-Path $runtimeDirectory 'scrcpy.exe') -Force
    Copy-Item -LiteralPath (Join-Path $serverBuild 'scrcpy-server.zip') -Destination (Join-Path $runtimeDirectory 'scrcpy-server') -Force
    $hashes=[ordered]@{}
    Get-ChildItem -LiteralPath $runtimeDirectory -File -Recurse | Where-Object {$_.Name -ne 'runtime-manifest.json'} | Sort-Object FullName | ForEach-Object {
        $relative=[IO.Path]::GetRelativePath($runtimeDirectory,$_.FullName).Replace('\','/')
        $hashes[$relative]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    $sourceHashes=@(Get-ChildItem -LiteralPath $sourceDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($sourceDirectory,$_.FullName).Replace('\','/')+':'+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
    $sourceHashes+=@($cameraSources | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($repoDirectory,$_.FullName).Replace('\','/')+':'+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
    $sourceFingerprint=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($sourceHashes -join "`n"))))
    $officialRecord=Get-Content -LiteralPath (Join-Path $officialDirectory 'runtime-manifest.json') -Raw | ConvertFrom-Json
    [ordered]@{version=$version;distribution='vircre-source-build';features=@('windows-ime-v1','window-capture-exclusion-v1','vdm-direct-encoder-v1','camera-hd-jpeg-v1','camera-display-size-v1','camera-live-move-v1');acceptance='pending-user-validation';sourceCommit=$officialRecord.sourceCommit;sourceFingerprint=$sourceFingerprint;buildType=$BuildType;androidMinApi=36;compiler=((& $env:CC --version | Select-Object -First 1) -join '');files=$hashes} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runtimeDirectory 'runtime-manifest.json') -Encoding utf8
} finally {
    foreach($name in $previousEnvironment.Keys) {[Environment]::SetEnvironmentVariable($name,$previousEnvironment[$name])}
}
if(-not $SkipTests) {& (Join-Path $PSScriptRoot 'Test-CustomScrcpy.ps1') -RuntimeDirectory $runtimeDirectory}
Write-Output ('Custom scrcpy built: '+$runtimeDirectory)
