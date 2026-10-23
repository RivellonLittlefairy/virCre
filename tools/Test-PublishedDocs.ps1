#requires -Version 7.0
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $repo ('out\published-docs-checks-'+[Guid]::NewGuid().ToString('N'))
$source=Join-Path $fixture 'source';$output=Join-Path $fixture 'publish'
New-Item -ItemType Directory -Force -Path (Join-Path $source 'docs\internal'),(Join-Path $output 'docs\internal') | Out-Null
function Check([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Write-Fixture([string]$Path,[string]$Value){Set-Content -LiteralPath $Path -Value $Value -Encoding utf8}
Write-Fixture (Join-Path $source 'docs\index.html') '<!doctype html><meta charset="utf-8"><a href="virtual-camera-window.html">Guide</a>'
Write-Fixture (Join-Path $source 'docs\virtual-camera-window.html') '<!doctype html><meta charset="utf-8">相机说明'
Write-Fixture (Join-Path $source 'docs\release-9.9.html') '<!doctype html><meta charset="utf-8">未来版本'
Write-Fixture (Join-Path $source 'docs\internal\secret.md') 'tracked internal file must not ship'
& git -C $source init -q
if($LASTEXITCODE -ne 0){throw 'Fixture git init failed'}
& git -C $source add docs
if($LASTEXITCODE -ne 0){throw 'Fixture git add failed'}
Write-Fixture (Join-Path $source 'docs\untracked-draft.html') '<!doctype html>private draft'
Write-Fixture (Join-Path $output 'VirCre.Desktop.exe') 'fixture only; never execute'
Write-Fixture (Join-Path $output 'keep.txt') 'outside docs must survive'
Write-Fixture (Join-Path $output 'docs\windows-ime-preview.html') 'old internal guide'
Write-Fixture (Join-Path $output 'docs\internal\old.md') 'stale internal document'
$before=@{};Get-ChildItem -LiteralPath (Join-Path $source 'docs') -File -Recurse | ForEach-Object {$before[$_.FullName]=(Get-FileHash -LiteralPath $_.FullName).Hash}
$helper=Join-Path $PSScriptRoot 'Sync-PublishedDocs.ps1'
& $helper -RepositoryRoot $source -OutputDirectory $output
$published=@(Get-ChildItem -LiteralPath (Join-Path $output 'docs') -File -Recurse | ForEach-Object Name | Sort-Object)
Check (($published -join '|') -eq 'index.html|release-9.9.html|virtual-camera-window.html') 'Public document set leaked drafts/internal files or lost current/future pages'
Check ((Get-Content -LiteralPath (Join-Path $output 'keep.txt') -Raw).Trim() -eq 'outside docs must survive') 'Synchronization changed unrelated publish content'
& $helper -RepositoryRoot $source -OutputDirectory $output
Check (@(Get-ChildItem -LiteralPath (Join-Path $output 'docs') -File).Count -eq 3) 'Second synchronization is not idempotent'
function Reject-Unsafe([string]$Destination){
    $rejected=$false
    try {& $helper -RepositoryRoot $source -OutputDirectory $Destination}
    catch {$rejected=$true}
    Check $rejected ('Unsafe destination accepted: '+$Destination)
}
Reject-Unsafe $source
New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'docs') | Out-Null
Write-Fixture (Join-Path $fixture 'VirCre.Desktop.exe') 'ancestor marker must not authorize deleting ancestor docs'
$ancestorSentinel=Join-Path $fixture 'docs\ancestor-keep.html'
Write-Fixture $ancestorSentinel 'ancestor user document must survive'
$ancestorHash=(Get-FileHash -LiteralPath $ancestorSentinel).Hash
Reject-Unsafe $fixture
Check ((Test-Path -LiteralPath $ancestorSentinel) -and (Get-FileHash -LiteralPath $ancestorSentinel).Hash -eq $ancestorHash) 'Repository ancestor docs were changed'
$sourceDocs=Join-Path $source 'docs'
New-Item -ItemType Directory -Force -Path (Join-Path $sourceDocs 'docs') | Out-Null
Write-Fixture (Join-Path $sourceDocs 'VirCre.Desktop.exe') 'namespace alias must not authorize deleting source documents'
$namespaceSentinel=Join-Path $sourceDocs 'docs\namespace-keep.html'
Write-Fixture $namespaceSentinel 'source docs through extended namespace must survive'
$namespaceHash=(Get-FileHash -LiteralPath $namespaceSentinel).Hash
Reject-Unsafe ('\\?\'+$sourceDocs)
Check ((Test-Path -LiteralPath $namespaceSentinel) -and (Get-FileHash -LiteralPath $namespaceSentinel).Hash -eq $namespaceHash) 'Extended namespace changed source docs'
foreach($gitMarker in @('directory','file')){
    $gitOutput=Join-Path $fixture ('git-output-'+$gitMarker)
    New-Item -ItemType Directory -Force -Path (Join-Path $gitOutput 'docs') | Out-Null
    Write-Fixture (Join-Path $gitOutput 'VirCre.Desktop.exe') 'not a publication output'
    Write-Fixture (Join-Path $gitOutput 'docs\keep.html') 'Git root docs must survive'
    if($gitMarker -eq 'directory'){New-Item -ItemType Directory -Path (Join-Path $gitOutput '.git') | Out-Null}
    else{Write-Fixture (Join-Path $gitOutput '.git') 'gitdir: fixture-only'}
    Reject-Unsafe $gitOutput
    Check (Test-Path -LiteralPath (Join-Path $gitOutput 'docs\keep.html')) 'Git-root docs were deleted'
}
$shortOutput=Join-Path $fixture 'SHORT~1';New-Item -ItemType Directory -Force -Path (Join-Path $shortOutput 'docs') | Out-Null
Write-Fixture (Join-Path $shortOutput 'VirCre.Desktop.exe') 'short aliases unsupported'
Write-Fixture (Join-Path $shortOutput 'docs\keep.html') 'keep short-alias-like output'
Reject-Unsafe $shortOutput
Check (Test-Path -LiteralPath (Join-Path $shortOutput 'docs\keep.html')) 'Unsupported short path docs were deleted'
$unknown=Join-Path $fixture 'not-a-publish';New-Item -ItemType Directory -Force -Path (Join-Path $unknown 'docs') | Out-Null
Write-Fixture (Join-Path $unknown 'docs\keep.html') 'keep unsafe output intact'
Reject-Unsafe $unknown
Check (Test-Path -LiteralPath (Join-Path $unknown 'docs\keep.html')) 'Rejected directory was changed'
$linked=Join-Path $fixture 'linked-output';New-Item -ItemType Directory -Force -Path $linked | Out-Null
Write-Fixture (Join-Path $linked 'VirCre.Desktop.exe') 'fixture only'
New-Item -ItemType Junction -Path (Join-Path $linked 'docs') -Target (Join-Path $source 'docs') | Out-Null
Reject-Unsafe $linked
foreach($path in $before.Keys){Check ((Get-FileHash -LiteralPath $path).Hash -eq $before[$path]) ('Source changed: '+$path)}
Write-Output 'PASS: docs synchronization/idempotence/source preservation; ancestor/source/Git-root/extended namespace/short alias/junction rejection'
Write-Output ('Fixture retained for inspection: '+$fixture)
