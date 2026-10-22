#requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$root=[IO.Path]::GetFullPath($OutputDirectory)
if(-not(Test-Path -LiteralPath (Join-Path $root 'VirCre.Desktop.exe') -PathType Leaf)) {throw 'Publish the desktop application before preparing compliance materials'}
$lock=Get-Content -LiteralPath (Join-Path $repo 'third_party\distribution-lock.json') -Raw | ConvertFrom-Json -AsHashtable
$runtime=Join-Path $root 'runtime\scrcpy'
$runtimeRecord=Get-Content -LiteralPath (Join-Path $runtime 'runtime-manifest.json') -Raw | ConvertFrom-Json
if($runtimeRecord.version -ne $lock.scrcpyVersion -or $runtimeRecord.sourceCommit -ne $lock.scrcpyCommit) {throw 'Unreviewed scrcpy version; update the distribution lock and notices first'}
foreach($entry in $lock.unchangedBinaries.GetEnumerator()) {
    $path=Join-Path $runtime $entry.Key
    if(-not(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Value) {
        throw "Unreviewed third-party binary: $($entry.Key). Update the license/source inventory before distribution."
    }
}

# Fail on stale cached custom binaries: the source archive must describe the actual server/client build.
$scrcpySource=Join-Path $repo 'third_party\scrcpy'
if($runtimeRecord.distribution -eq 'vircre-source-build') {
    $fingerprint=& (Join-Path $PSScriptRoot 'Get-CustomRuntimeFingerprint.ps1')
    if($fingerprint -ne $runtimeRecord.sourceFingerprint) {throw 'The custom runtime is stale relative to its source snapshot. Rebuild with tools/Build-Scrcpy.ps1 first.'}
}

# Probe the exact DLLs being shipped, not an unrelated ffmpeg executable on PATH.
if(-not('VirCre.Compliance.NativeString' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace VirCre.Compliance {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr NativeString();
}
'@
}
$ffmpeg=[Collections.Generic.List[object]]::new()
$handles=[Collections.Generic.List[IntPtr]]::new()
try {
    foreach($item in @(@('avutil-61.dll','avutil'),@('swresample-7.dll','swresample'),@('avcodec-63.dll','avcodec'),@('avformat-63.dll','avformat'))) {
        $handle=[Runtime.InteropServices.NativeLibrary]::Load((Join-Path $runtime $item[0]));$handles.Add($handle)
        $record=[ordered]@{file=$item[0]}
        foreach($field in @('license','configuration')) {
            $address=[Runtime.InteropServices.NativeLibrary]::GetExport($handle,($item[1]+'_'+$field))
            $function=[Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer($address,[VirCre.Compliance.NativeString])
            $record[$field]=[Runtime.InteropServices.Marshal]::PtrToStringUTF8($function.Invoke())
        }
        if($record.license -ne 'LGPL version 2.1 or later' -or $record.configuration -match '--enable-(gpl|nonfree|version3)' -or $record.configuration -notmatch '--enable-shared') {
            throw "Unexpected FFmpeg license/configuration: $($item[0])"
        }
        $ffmpeg.Add($record)
    }
} finally {for($i=$handles.Count-1;$i -ge 0;$i--) {[Runtime.InteropServices.NativeLibrary]::Free($handles[$i])}}

$compliance=Join-Path $root 'compliance'
$sources=Join-Path $compliance 'sources'
$licenses=Join-Path $compliance 'licenses'
$cache=Join-Path $repo '.cache\compliance-sources'
foreach($directory in @($compliance,$sources,$licenses,$cache)) {New-Item -ItemType Directory -Force -Path $directory | Out-Null}
foreach($name in @('LICENSE','NOTICE','THIRD-PARTY-NOTICES.html')) {Copy-Item -LiteralPath (Join-Path $repo $name) -Destination (Join-Path $root $name) -Force}
Copy-Item -LiteralPath (Join-Path $repo 'third_party\distribution-lock.json') -Destination (Join-Path $compliance 'distribution-lock.json') -Force
$nativeLicenses=Join-Path $licenses 'native'
New-Item -ItemType Directory -Force -Path $nativeLicenses | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo 'third_party\licenses') | Copy-Item -Destination $nativeLicenses -Recurse -Force
Copy-Item -LiteralPath (Join-Path $scrcpySource 'LICENSE') -Destination (Join-Path $licenses 'scrcpy-LICENSE') -Force
foreach($source in $lock.sources) {
    if([IO.Path]::GetFileName($source.file) -ne $source.file -or $source.sha256 -notmatch '^[a-f0-9]{64}$' -or $source.url -notmatch '^https://') {throw 'Invalid source distribution lock'}
    $archive=Join-Path $cache $source.file
    if(-not(Test-Path -LiteralPath $archive)) {
        $temporary=$archive+'.'+[Guid]::NewGuid().ToString('N')+'.download'
        try {
            Invoke-WebRequest -Uri $source.url -OutFile $temporary -TimeoutSec 180
            if((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $source.sha256) {throw "Source checksum mismatch: $($source.id)"}
            [IO.File]::Move($temporary,$archive,$true)
        } finally {if(Test-Path -LiteralPath $temporary) {Remove-Item -LiteralPath $temporary}}
    }
    if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $source.sha256) {throw "Cached source checksum mismatch: $($source.id)"}
    Copy-Item -LiteralPath $archive -Destination (Join-Path $sources $source.file) -Force
}

# Preserve package terms from the versions actually selected by restore, including Microsoft proprietary terms.
$assets=Get-Content -LiteralPath (Join-Path $repo 'desktop\VirCre.Desktop\obj\project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$packageRoots=@($assets.packageFolders.Keys)
$packages=[Collections.Generic.List[object]]::new()
$microsoftTerms=[Collections.Generic.List[string]]::new()
function Copy-PackageNotices([string]$id,[string]$version,[string]$relative,[string[]]$files,[string]$NoticeDirectory,[string]$AgreementFile) {
    $packageDirectory=if($NoticeDirectory) {$NoticeDirectory} else {$packageRoots | ForEach-Object {Join-Path $_ $relative} | Where-Object {Test-Path -LiteralPath $_ -PathType Container} | Select-Object -First 1}
    if(-not $packageDirectory) {throw "NuGet package directory missing: $id/$version"}
    $copied=[Collections.Generic.List[string]]::new()
    foreach($file in $files) {
        $destination=Join-Path $licenses ('nuget/'+$id+'/'+$version+'/'+$file)
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath (Join-Path $packageDirectory $file) -Destination $destination -Force
        $copied.Add([IO.Path]::GetRelativePath($root,$destination).Replace('\','/'))
        if($id.StartsWith('Microsoft.') -and ([IO.Path]::GetFileName($file) -match '(?i)^(license|sdk_license)\.txt$' -or $file -eq $AgreementFile)) {
            $text=[IO.File]::ReadAllText($destination)
            if($text -match 'MICROSOFT SOFTWARE LICENSE TERMS') {$microsoftTerms.Add("$id $version`n$text")}
        }
    }
    $packages.Add([ordered]@{id=$id;version=$version;notices=@($copied)})
}
foreach($entry in $assets.libraries.GetEnumerator() | Sort-Object Key) {
    if($entry.Value.type -ne 'package') {continue}
    $identity=$entry.Key.Split('/')
    $noticeFiles=@($entry.Value.files | Where-Object {[IO.Path]::GetFileName($_) -match '(?i)^(license(?:\.[a-z0-9-]+)?|notice(?:\.[a-z0-9-]+)?|third[-_]?party[-_]?notices(?:\.[a-z0-9-]+)?|sdk_license\.txt)$'})
    if($noticeFiles.Count -gt 0) {Copy-PackageNotices $identity[0] $identity[1] $entry.Value.path $noticeFiles}
    elseif($identity[0] -ne 'Microsoft.Windows.SDK.BuildTools') {throw "Package terms require review: $($entry.Key)"}
}
$dependencies=Get-Content -LiteralPath (Join-Path $root 'VirCre.Desktop.deps.json') -Raw | ConvertFrom-Json -AsHashtable
foreach($entry in $dependencies.libraries.GetEnumerator() | Where-Object {$_.Value.type -eq 'runtimepack'}) {
    $identity=($entry.Key -replace '^runtimepack\.','').Split('/')
    $id=$identity[0];$version=$identity[1];$relative=$id.ToLowerInvariant()+'/'+$version
    if($packages | Where-Object {$_.id -eq $id -and $_.version -eq $version}) {continue}
    if($id -eq 'Microsoft.NETCore.App.Runtime.win-x64') {
        Copy-PackageNotices $id $version $relative @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')
        continue
    }
    # Some implicit SDK packs only provide a licenseUrl, not license files inside the nupkg.
    $override=$lock.packageLicenseOverrides | Where-Object {$_.id -eq $id -and $_.version -eq $version} | Select-Object -First 1
    if(-not $override) {throw "Runtime pack license requires review: $id/$version"}
    $packageDirectory=$packageRoots | ForEach-Object {Join-Path $_ $relative} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
    $nuspecPath=Join-Path $packageDirectory ($id.ToLowerInvariant()+'.nuspec')
    [xml]$nuspec=Get-Content -LiteralPath $nuspecPath -Raw
    if($nuspec.SelectSingleNode("//*[local-name()='licenseUrl']").InnerText -ne $override.licenseUrl) {throw "Runtime pack license URL changed: $id/$version"}
    foreach($file in $override.files.GetEnumerator()) {
        if([IO.Path]::GetFileName($file.Key) -ne $file.Key -or (Get-FileHash -LiteralPath (Join-Path $repo ('third_party/licenses/'+$file.Key)) -Algorithm SHA256).Hash -ne $file.Value) {
            throw "Runtime pack license snapshot mismatch: $id/$version"
        }
    }
    Copy-PackageNotices $id $version $relative @($override.files.Keys) (Join-Path $repo 'third_party\licenses') $override.termsText
}
if($microsoftTerms.Count -eq 0) {throw 'Microsoft binary distribution terms were not collected'}
$terms=@'
virCre: independent licenses for bundled components

Original virCre source is licensed under Apache-2.0 (see LICENSE and NOTICE).
Microsoft proprietary binary components are not relicensed under Apache-2.0.
By installing or using those components, you accept their applicable terms
reproduced below. Distributors must preserve and pass on those terms.
These Microsoft-specific terms apply only to their respective components.
They do not restrict the Apache-licensed code, scrcpy, or rights under LGPL,
including library replacement and reverse engineering to debug modifications.
No upstream publisher endorses virCre. Full notices: THIRD-PARTY-NOTICES.html.

Applicable original Microsoft component terms:

'@ + ($microsoftTerms -join "`n`n------------------------------------------------------------`n`n")
$escaped=$terms.Replace('\','\\').Replace('{','\{').Replace('}','\}')
$escaped=[regex]::Replace($escaped,'[^\x00-\x7f]',{param($m) $value=[int][char]$m.Value;if($value -gt 32767){$value-=65536};'\u'+$value+'?'})
$rtf='{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs20 '+$escaped.Replace("`r",'').Replace("`n","\par`n")+'}'
[IO.File]::WriteAllText((Join-Path $compliance 'THIRD-PARTY-TERMS.rtf'),$rtf,[Text.Encoding]::ASCII)

# Whitelist version-controlled project/source roots. Never archive .git, credentials, local logs or build output.
$paths=@((& git -C $repo -c core.quotepath=false ls-files --cached --others --exclude-standard -z) -join "`n") -split "`0"
if($LASTEXITCODE -ne 0) {throw 'Cannot enumerate the matching source snapshot'}
$paths=@($paths | Where-Object {$_ -match '^(desktop/|android-agent/|camera-agent/|tools/|packaging/|third_party/|VERSION$|Directory\.Build\.props$|LICENSE$|NOTICE$|THIRD-PARTY-NOTICES\.html$|README\.md$|\.gitattributes$|\.gitignore$|docs/(scrcpy-changes|license-compliance)\.html$)' -and
    $_ -notmatch '(^|/)(bin|obj|build|\.cache|\.git|out)(/|$)' -and (Test-Path -LiteralPath (Join-Path $repo $_) -PathType Leaf)} | Sort-Object -Unique)
$sourceZip=Join-Path $sources 'virCre-source.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream=[IO.File]::Open($sourceZip,[IO.FileMode]::Create)
$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
$sourceFiles=[ordered]@{}
try {
    foreach($relative in $paths) {
        $path=Join-Path $repo $relative
        if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) {throw "Source snapshot must not follow symlinks: $relative"}
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$path,$relative,[IO.Compression.CompressionLevel]::Optimal)
        $sourceFiles[$relative]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
} finally {$zip.Dispose();$stream.Dispose()}
$sourceFiles | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $compliance 'source-files.json') -Encoding utf8NoBOM
$files=[ordered]@{}
Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object {$_.FullName -ne (Join-Path $compliance 'manifest.json')} | Sort-Object FullName | ForEach-Object {
    $files[[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/')]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
[ordered]@{schema=1;projectLicense='Apache-2.0 (original source only)';sourceCommit=((& git -C $repo rev-parse HEAD) -join '');
    sourceSnapshot='sources/virCre-source.zip';scrcpySourceFingerprint=$runtimeRecord.sourceFingerprint;
    sources=@($lock.sources);ffmpeg=@($ffmpeg);packages=@($packages);files=$files} |
    ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $compliance 'manifest.json') -Encoding utf8NoBOM
Write-Output ('Distribution materials prepared: '+$compliance)
