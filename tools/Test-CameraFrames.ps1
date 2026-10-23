#requires -Version 7.0
param([string]$Sdk="$env:LOCALAPPDATA\Android\Sdk",[string]$Java='C:\Program Files\Android\Android Studio\jbr',[switch]$BuildEmbeddedServer)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$output=Join-Path $repo 'out\camera-frame-efficiency'
$classes=Join-Path $output 'java-tests'
New-Item -ItemType Directory -Force -Path $classes | Out-Null
$javac=Join-Path $Java 'bin\javac.exe';$javaExe=Join-Path $Java 'bin\java.exe'
$core=Join-Path $repo 'camera-agent\src\main\java\com\example\vircam\core'
$shell=Join-Path $repo 'camera-agent\src\main\java\com\example\vircam\shell'
function Invoke-Check([string]$Name,[scriptblock]$Command) {
    $log=Join-Path $output ($Name+'.log')
    & $Command *> $log
    $code=$LASTEXITCODE
    Get-Content -LiteralPath $log -Tail 12
    if($code -ne 0) {throw "$Name failed (exit $code); see $log"}
}
Push-Location $repo
try {
    Invoke-Check 'java-core-compile' {
        $sources=@(Get-ChildItem -LiteralPath $core -Filter '*.java' | ForEach-Object FullName)
        & $javac --release 8 -Xlint:-options -encoding UTF-8 '-J-Dfile.encoding=UTF-8' '-J-Duser.language=en' -d $classes @sources (Join-Path $repo 'tools\tests\CameraFrameChecks.java')
    }
    $csharpWire=Join-Path $output 'csharp-repeat.bin';$javaWire=Join-Path $output 'java-repeat.bin'
    Invoke-Check 'csharp-camera-checks' {dotnet run --project desktop/VirCam.Tests -c Release -- --write-repeat $csharpWire}
    Invoke-Check 'java-read-csharp-wire' {& $javaExe -cp $classes CameraFrameChecks --read-repeat $csharpWire}
    Invoke-Check 'java-write-wire' {& $javaExe -cp $classes CameraFrameChecks --write-repeat $javaWire}
    Invoke-Check 'csharp-read-java-wire' {dotnet run --project desktop/VirCam.Tests -c Release --no-build -- --read-repeat $javaWire}

    if($BuildEmbeddedServer) {
        # Match Build-Scrcpy's Java 8/API-36 and D8 min-API-29 production settings,
        # without rebuilding the unmodified native client or mutating cached runtimes.
        $server=Join-Path $repo 'third_party\scrcpy\server\src\main'
        $generated=Join-Path $output 'embedded-server\generated';$embeddedClasses=Join-Path $output 'embedded-server\classes'
        $configDirectory=Join-Path $generated 'com\genymobile\scrcpy'
        New-Item -ItemType Directory -Force -Path $configDirectory,$embeddedClasses | Out-Null
        'package com.genymobile.scrcpy; public final class BuildConfig { public static final boolean DEBUG=false; public static final String VERSION_NAME="5.0.1"; }' | Set-Content -LiteralPath (Join-Path $configDirectory 'BuildConfig.java') -Encoding utf8
        $androidJar=Join-Path $Sdk 'platforms\android-36\android.jar'
        $buildTools=Get-ChildItem -LiteralPath (Join-Path $Sdk 'build-tools') -Directory | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
        $aidl=Join-Path $buildTools.FullName 'aidl.exe';$aidlSource=Join-Path $server 'aidl'
        Invoke-Check 'embedded-aidl-clipboard' {& $aidl ('-o'+$generated) ('-I'+$aidlSource) (Join-Path $aidlSource 'android\content\IOnPrimaryClipChangedListener.aidl')}
        Invoke-Check 'embedded-aidl-display' {& $aidl ('-o'+$generated) ('-I'+$aidlSource) '-p' (Join-Path $Sdk 'platforms\android-36\framework.aidl') (Join-Path $aidlSource 'android\view\IDisplayWindowListener.aidl')}
        $sources=@(Get-ChildItem -LiteralPath (Join-Path $server 'java'),$generated,$core,$shell -Filter '*.java' -Recurse | ForEach-Object {'"'+$_.FullName.Replace('\','/')+'"'})
        $response=Join-Path $output 'embedded-server\javac.args'
        $sources | Set-Content -LiteralPath $response -Encoding utf8
        $lambda=Join-Path $buildTools.FullName 'core-lambda-stubs.jar'
        Invoke-Check 'embedded-java8-compile' {& $javac -encoding UTF-8 '-J-Dfile.encoding=UTF-8' '-J-Duser.language=en' -source 8 -target 8 -Xlint:-options -bootclasspath $androidJar -classpath ($lambda+';'+$generated) -d $embeddedClasses ('@'+$response)}
        $dexResponse=Join-Path $output 'embedded-server\d8.args'
        $dex=@('--min-api','29','--lib',$androidJar.Replace('\','/'),'--output',(Join-Path $output 'embedded-server\scrcpy-server.zip').Replace('\','/'))
        $dex+=Get-ChildItem -LiteralPath $embeddedClasses -Filter '*.class' -Recurse | ForEach-Object {$_.FullName.Replace('\','/')}
        $dex | Set-Content -LiteralPath $dexResponse -Encoding utf8
        Invoke-Check 'embedded-dex' {& $javaExe -cp (Join-Path $buildTools.FullName 'lib\d8.jar') com.android.tools.r8.D8 ('@'+$dexResponse)}
    }
} finally {Pop-Location}
Write-Output 'PASS: camera efficiency checks and bidirectional C#/Java wire fixtures'
