#requires -Version 7.0
param([string]$OutputDirectory,[ValidateSet('Official','Custom')][string]$Runtime='Official',[switch]$SkipTests,
    [ValidateSet('desktop-shortcuts.html','windows-ime-preview.html','virtual-camera-window.html')][string]$GuidePage,
    [ValidatePattern('^\d+\.\d+(?:\.\d+)?$')][string]$Version)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(-not $OutputDirectory) {
    $outputName=if($Runtime -eq 'Custom') {'out\desktop-ime-preview'} else {'out\desktop-release'}
    $OutputDirectory=Join-Path $repo $outputName
}
$publishArgs=@('publish',(Join-Path $repo 'desktop\VirCre.Desktop\VirCre.Desktop.csproj'),'-c','Release','-r','win-x64','--self-contained','true','-o',$OutputDirectory,'--nologo',("-p:ScrcpyRuntimeVariant="+$Runtime))
if($Version) {
    $parsedVersion=[Version]$Version
    $patchVersion=[Math]::Max(0,$parsedVersion.Build)
    $fileVersion="$($parsedVersion.Major).$($parsedVersion.Minor).$patchVersion.0"
    $publishArgs+=@("-p:Version=$Version","-p:AssemblyVersion=$fileVersion","-p:FileVersion=$fileVersion")
}
& dotnet @publishArgs
if($LASTEXITCODE -ne 0) {throw 'Desktop publish failed'}
if(-not $SkipTests) {& (Join-Path $PSScriptRoot 'Test-DesktopPublish.ps1') -OutputDirectory $OutputDirectory}
$publishedDocs=Join-Path $OutputDirectory 'docs'
New-Item -ItemType Directory -Force -Path $publishedDocs | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo 'docs') | Where-Object {$_.PSIsContainer -or $_.Name -notlike 'code-review-*.md'} | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $publishedDocs -Recurse -Force}
$selectedGuide=if($GuidePage) {$GuidePage} elseif($Runtime -eq 'Custom') {'windows-ime-preview.html'} else {'desktop-shortcuts.html'}
$guide='<meta charset="utf-8"><meta http-equiv="refresh" content="0;url=docs/'+$selectedGuide+'"><a href="docs/'+$selectedGuide+'">打开使用说明</a>'
Set-Content -LiteralPath (Join-Path $OutputDirectory '使用说明.html') -Value $guide -Encoding utf8
& (Join-Path $PSScriptRoot 'Prepare-DistributionCompliance.ps1') -OutputDirectory $OutputDirectory
& (Join-Path $PSScriptRoot 'Test-DistributionCompliance.ps1') -OutputDirectory $OutputDirectory
Write-Output "Ready: $OutputDirectory\VirCre.Desktop.exe"
Write-Output 'ADB and scrcpy are bundled in runtime\scrcpy. Keep the complete folder in a stable location before creating shortcuts.'
