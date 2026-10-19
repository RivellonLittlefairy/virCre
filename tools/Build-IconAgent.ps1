#requires -Version 7.0
param([string]$Sdk = "$env:LOCALAPPDATA\Android\Sdk", [string]$Java = 'C:\Program Files\Android\Android Studio\jbr')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repo 'android-agent\src\main\java\com\vircre\icons\IconMain.java'
$output = Join-Path $repo 'android-agent\build'
$jar = Join-Path $output 'icon-agent.jar'
if ((Test-Path -LiteralPath $jar) -and (Get-Item -LiteralPath $jar).LastWriteTimeUtc -ge (Get-Item -LiteralPath $source).LastWriteTimeUtc) { return }
if (-not (Test-Path -LiteralPath "$Java\bin\javac.exe")) {
    if ($env:JAVA_HOME) { $Java = $env:JAVA_HOME } else { throw 'JDK missing. Set JAVA_HOME or pass -Java.' }
}
$androidJar = Join-Path $Sdk 'platforms\android-36\android.jar'
$d8 = Get-ChildItem -LiteralPath (Join-Path $Sdk 'build-tools') -Directory | Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'lib\d8.jar' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $d8 -or -not (Test-Path -LiteralPath $androidJar)) { throw 'Android SDK API 36 and Build Tools are required.' }
$classes = Join-Path $output 'classes'
New-Item -ItemType Directory -Force $classes | Out-Null
& "$Java\bin\javac.exe" --release 11 -encoding UTF-8 -classpath $androidJar -d $classes $source
if ($LASTEXITCODE -ne 0) { throw 'Icon agent Java compilation failed' }
& "$Java\bin\java.exe" -cp $d8 com.android.tools.r8.D8 --min-api 36 --lib $androidJar --output $jar (Join-Path $classes 'com\vircre\icons\IconMain.class')
if ($LASTEXITCODE -ne 0) { throw 'Icon agent dex build failed' }
