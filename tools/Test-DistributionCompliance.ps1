#requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($OutputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
foreach($required in @('LICENSE','NOTICE','THIRD-PARTY-NOTICES.html','compliance\manifest.json',
    'compliance\THIRD-PARTY-TERMS.rtf','compliance\sources\virCre-source.zip',
    'compliance\sources\ffmpeg-9.0.2.tar.xz','compliance\sources\libusb-1.0.30.tar.gz',
    'compliance\sources\sdl-3.4.18.tar.gz','compliance\sources\dav1d-1.5.4.tar.xz',
    'compliance\sources\zlib-1.3.1.tar.gz')) {
    $path=Join-Path $root $required
    if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Distribution compliance material is missing or empty: $required"
    }
}
$manifest=Get-Content -LiteralPath (Join-Path $root 'compliance\manifest.json') -Raw | ConvertFrom-Json -AsHashtable
if($manifest.schema -ne 1 -or $manifest.files.Count -lt 20) {throw 'Invalid distribution compliance inventory'}
foreach($entry in $manifest.files.GetEnumerator()) {
    $path=[IO.Path]::GetFullPath((Join-Path $root $entry.Key))
    if(-not $path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Invalid compliance inventory path'}
    if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Value) {
        throw "Distribution file missing or changed after inspection: $($entry.Key)"
    }
}
foreach($source in $manifest.sources) {
    $relative='compliance/sources/'+$source.file
    if(-not $manifest.files.ContainsKey($relative) -or $manifest.files[$relative] -ne $source.sha256) {
        throw "Corresponding source archive does not match its lock: $($source.id)"
    }
}
foreach($name in @('Microsoft.WindowsAppSDK','Microsoft.NETCore.App.Runtime.win-x64','Microsoft.Windows.SDK.NET.Ref')) {
    if(-not($manifest.packages | Where-Object {$_.id -eq $name -and $_.notices.Count -gt 0})) {throw "Required Microsoft/.NET license missing: $name"}
}
$dependencies=Get-Content -LiteralPath (Join-Path $root 'VirCre.Desktop.deps.json') -Raw | ConvertFrom-Json -AsHashtable
foreach($entry in $dependencies.libraries.GetEnumerator() | Where-Object {$_.Value.type -eq 'runtimepack'}) {
    $identity=($entry.Key -replace '^runtimepack\.','').Split('/')
    if(-not($manifest.packages | Where-Object {$_.id -eq $identity[0] -and $_.version -eq $identity[1]})) {throw "Runtime pack missing from compliance inventory: $($entry.Key)"}
}
foreach($package in $manifest.packages) {
    foreach($notice in $package.notices) {
        if(-not $manifest.files.ContainsKey($notice)) {throw "Package license is not backed by an inventoried file: $($package.id)"}
    }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceArchive=[IO.Compression.ZipFile]::OpenRead((Join-Path $root 'compliance\sources\virCre-source.zip'))
try {
    foreach($required in @('LICENSE','NOTICE','third_party/scrcpy/LICENSE','tools/Build-Scrcpy.ps1',
        'third_party/scrcpy/app/deps/ffmpeg.sh','camera-agent/src/main/java/com/example/vircam/shell/ApplicationCameraSession.java')) {
        if(-not $sourceArchive.GetEntry($required)) {throw "Source snapshot is incomplete: $required"}
    }
    if($sourceArchive.Entries | Where-Object {$_.FullName -match '(^|/)(\.git|\.cache|out|obj|bin)(/|$)'}) {throw 'Source snapshot contains private or generated workspace directories'}
} finally {$sourceArchive.Dispose()}
Write-Output ('PASS: distribution licenses, Microsoft terms, corresponding source archives and '+$manifest.files.Count+' file hashes verified')
