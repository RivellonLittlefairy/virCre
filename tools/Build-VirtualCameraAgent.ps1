#requires -Version 7.0
param([string]$Sdk="$env:LOCALAPPDATA\Android\Sdk",[string]$Java='C:\Program Files\Android\Android Studio\jbr')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$sourceRoot=Join-Path $repo 'camera-agent\src\main\java\com\example\vircam'
$output=Join-Path $repo 'android-agent\build\virtual-camera'
$sources=@(Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'shell') -Filter '*.java')
foreach($name in @('FrameProtocol.java','LatestFrame.java','FrameMarker.java','TextureRevision.java','DesktopFrameRenderer.java')) {$sources+=Get-Item -LiteralPath (Join-Path $sourceRoot "core\$name")}
$jar=Join-Path $output 'virtual-camera-agent.jar'
$latestSource=(@($sources)+@(Get-Item -LiteralPath $PSCommandPath) | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
if((Test-Path -LiteralPath $jar) -and (Get-Item -LiteralPath $jar).LastWriteTimeUtc -ge $latestSource) {return}
$classes=Join-Path $output ('classes-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $classes | Out-Null
$androidJar=Join-Path $Sdk 'platforms\android-36\android.jar'
$d8=Get-ChildItem -LiteralPath (Join-Path $Sdk 'build-tools') -Directory | Sort-Object {[version]$_.Name} -Descending | ForEach-Object {Join-Path $_.FullName 'lib\d8.jar'} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
& (Join-Path $Java 'bin\javac.exe') --release 11 -encoding UTF-8 -classpath $androidJar -d $classes @($sources.FullName)
if($LASTEXITCODE -ne 0) {throw 'Virtual camera shell agent Java compilation failed'}
$response=Join-Path $output 'd8.args'
$arguments=@('--min-api','36','--lib',$androidJar.Replace('\','/'),'--output',$jar.Replace('\','/'))
$arguments+=Get-ChildItem -LiteralPath $classes -Filter '*.class' -Recurse | ForEach-Object {$_.FullName.Replace('\','/')}
$arguments | Set-Content -LiteralPath $response -Encoding utf8
& (Join-Path $Java 'bin\java.exe') -cp $d8 com.android.tools.r8.D8 ('@'+$response)
if($LASTEXITCODE -ne 0) {throw 'Virtual camera shell agent DEX compilation failed'}
