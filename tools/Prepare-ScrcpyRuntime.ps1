#requires -Version 7.0
param([string]$DestinationDirectory,[string]$ArchivePath)
$ErrorActionPreference='Stop'
$repoDirectory=Split-Path $PSScriptRoot -Parent
$manifestPath=Join-Path $repoDirectory 'third_party\scrcpy-runtime.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if(-not $DestinationDirectory) {$DestinationDirectory=Join-Path $repoDirectory '.cache\bundled-runtime\scrcpy'}
$DestinationDirectory=[IO.Path]::GetFullPath($DestinationDirectory)
$existingManifest=Join-Path $DestinationDirectory 'runtime-manifest.json'
if(Test-Path -LiteralPath $existingManifest) {
    $existing=Get-Content -LiteralPath $existingManifest -Raw | ConvertFrom-Json
    if($existing.distribution -ne 'official-unmodified') {throw 'Refusing to overwrite a custom scrcpy runtime with the official distribution'}
}
if(-not $ArchivePath) {
    $downloadDirectory=Join-Path $repoDirectory '.cache\scrcpy-runtime'
    New-Item -ItemType Directory -Force -Path $downloadDirectory | Out-Null
    $ArchivePath=Join-Path $downloadDirectory ('scrcpy-win64-v'+$manifest.version+'.zip')
    if(-not (Test-Path -LiteralPath $ArchivePath)) {
        $downloadPath=$ArchivePath+'.'+[Guid]::NewGuid().ToString('N')+'.download'
        try {
            Invoke-WebRequest -Uri $manifest.archiveUrl -OutFile $downloadPath -TimeoutSec 120
            if((Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash -ne $manifest.archiveSha256) {throw 'scrcpy archive SHA-256 mismatch'}
            [IO.File]::Move($downloadPath,$ArchivePath,$true)
        } finally {if(Test-Path -LiteralPath $downloadPath) {Remove-Item -LiteralPath $downloadPath}}
    }
}
if((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash -ne $manifest.archiveSha256) {throw 'scrcpy archive SHA-256 mismatch; no runtime files were written'}
New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($ArchivePath)
$fileHashes=[ordered]@{}
try {
    foreach($fileName in $manifest.files) {
        if([IO.Path]::GetFileName($fileName) -ne $fileName) {throw 'Runtime manifest contains an invalid file name'}
        $entry=$archive.GetEntry($manifest.archivePrefix+$fileName)
        if($null -eq $entry) {throw ('Runtime archive is missing '+$fileName)}
        $entryStream=$entry.Open()
        try {$expectedHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($entryStream))} finally {$entryStream.Dispose()}
        $fileHashes[$fileName]=$expectedHash
        $outputPath=Join-Path $DestinationDirectory $fileName
        if((Test-Path -LiteralPath $outputPath) -and (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash -eq $expectedHash) {continue}
        $temporaryPath=$outputPath+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
        try {
            $entryStream=$entry.Open();$outputStream=[IO.File]::Create($temporaryPath)
            try {$entryStream.CopyTo($outputStream)} finally {$outputStream.Dispose();$entryStream.Dispose()}
            [IO.File]::Move($temporaryPath,$outputPath,$true)
        } finally {if(Test-Path -LiteralPath $temporaryPath) {Remove-Item -LiteralPath $temporaryPath}}
    }
} finally {$archive.Dispose()}
$licenseDirectory=Join-Path $DestinationDirectory 'licenses'
New-Item -ItemType Directory -Force -Path $licenseDirectory | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repoDirectory 'third_party\licenses') -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenseDirectory $_.Name) -Force
    $fileHashes['licenses/'+$_.Name]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
$runtimeRecord=[ordered]@{version=$manifest.version;sourceCommit=$manifest.sourceCommit;distribution='official-unmodified';archiveSha256=$manifest.archiveSha256;files=$fileHashes}
$recordPath=Join-Path $DestinationDirectory 'runtime-manifest.json'
$recordJson=$runtimeRecord | ConvertTo-Json -Depth 5
if(-not (Test-Path -LiteralPath $recordPath) -or [IO.File]::ReadAllText($recordPath) -ne $recordJson) {[IO.File]::WriteAllText($recordPath,$recordJson,[Text.UTF8Encoding]::new($false))}
Write-Output ('Bundled scrcpy '+$manifest.version+' verified: '+$DestinationDirectory)
