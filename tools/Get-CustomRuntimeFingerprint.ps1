#requires -Version 7.0
$ErrorActionPreference='Stop'
$repoDirectory=Split-Path $PSScriptRoot -Parent
$sourceDirectory=Join-Path $repoDirectory 'third_party/scrcpy'
$cameraDirectory=Join-Path $repoDirectory 'camera-agent/src/main/java/com/example/vircam'
$cameraFiles=@(Get-ChildItem -LiteralPath (Join-Path $cameraDirectory 'shell') -File -Filter '*.java')
foreach($name in @('FrameProtocol.java','LatestFrame.java','FrameMarker.java')) {
    $cameraFiles+=Get-Item -LiteralPath (Join-Path $cameraDirectory "core/$name")
}
# Match the original build fingerprint so existing verified caches remain reusable.
$lines=@(Get-ChildItem -LiteralPath $sourceDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
    [IO.Path]::GetRelativePath($sourceDirectory,$_.FullName).Replace('\','/')+':'+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
})
$lines+=@($cameraFiles | Sort-Object FullName | ForEach-Object {
    [IO.Path]::GetRelativePath($repoDirectory,$_.FullName).Replace('\','/')+':'+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
})
[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))))
