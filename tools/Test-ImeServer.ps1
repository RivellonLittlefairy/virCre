#requires -Version 7.0
param([string]$Java='C:\Program Files\Android\Android Studio\jbr',[string]$Sdk="$env:LOCALAPPDATA\Android\Sdk")
$ErrorActionPreference='Stop'
$repoDirectory=Split-Path $PSScriptRoot -Parent
$serverBuild=Join-Path $repoDirectory '.cache\scrcpy-build\debug\server-build'
$generation=Get-ChildItem -LiteralPath $serverBuild -Directory -Filter 'gen-*' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if(-not $generation) {throw 'Build the debug scrcpy server before running IME server checks'}
$classes=Join-Path $generation.FullName 'classes'
$testDirectory=Join-Path $serverBuild 'tests'
New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
$classpath=$classes+';'+(Join-Path $Sdk 'platforms\android-36\android.jar')
$sources=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter 'Ime*Checks.java' | Select-Object -ExpandProperty FullName)
$productionSource=Join-Path $repoDirectory 'third_party\scrcpy\server\src\main\java\com\genymobile\scrcpy\control'
$compilationSources=$sources+@('ControlMessage.java','ControlMessageReader.java','DeviceMessage.java','DeviceMessageWriter.java','ImeCommitter.java' | ForEach-Object {Join-Path $productionSource $_})
& (Join-Path $Java 'bin\javac.exe') -encoding UTF-8 -classpath $classpath -d $testDirectory @compilationSources
if($LASTEXITCODE -ne 0) {throw 'IME server check compilation failed'}
foreach($source in $sources) {
    $testClass='com.genymobile.scrcpy.control.'+[IO.Path]::GetFileNameWithoutExtension($source)
    & (Join-Path $Java 'bin\java.exe') -cp ($testDirectory+';'+$classpath) $testClass $testDirectory
    if($LASTEXITCODE -ne 0) {throw ('IME server checks failed: '+[IO.Path]::GetFileNameWithoutExtension($source))}
}
