#requires -Version 7.0
$ErrorActionPreference='Stop'
$repoDirectory=Split-Path $PSScriptRoot -Parent
$outputRoot=[IO.Path]::GetFullPath((Join-Path $repoDirectory 'out'))
$testRoot=[IO.Path]::GetFullPath((Join-Path $outputRoot ('runtime-tests-'+[Guid]::NewGuid().ToString('N'))))
if(-not $testRoot.StartsWith($outputRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Invalid test cleanup path'}
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
try {
    $badArchive=Join-Path $testRoot 'bad.zip';[IO.File]::WriteAllText($badArchive,'not the official archive')
    $rejectedDestination=Join-Path $testRoot 'rejected'
    $rejected=$false
    try {& (Join-Path $PSScriptRoot 'Prepare-ScrcpyRuntime.ps1') -ArchivePath $badArchive -DestinationDirectory $rejectedDestination}
    catch {$rejected=$_.Exception.Message -like '*SHA-256 mismatch*'}
    if(-not $rejected -or (Test-Path -LiteralPath $rejectedDestination)) {throw 'A bad archive must fail before writing runtime files'}
    Write-Output 'PASS: invalid archive rejected before runtime extraction'
    $destination=Join-Path $testRoot 'bundle'
    & (Join-Path $PSScriptRoot 'Prepare-ScrcpyRuntime.ps1') -DestinationDirectory $destination
    $record=Get-Content -LiteralPath (Join-Path $destination 'runtime-manifest.json') -Raw | ConvertFrom-Json
    foreach($entry in $record.files.PSObject.Properties) {
        if((Get-FileHash -LiteralPath (Join-Path $destination $entry.Name) -Algorithm SHA256).Hash -ne $entry.Value) {throw ('Invalid packaged hash: '+$entry.Name)}
    }
    Write-Output 'PASS: pinned client, server, dependencies and license notices match the runtime manifest'
    $sdl=Join-Path $destination 'SDL3.dll';[IO.File]::AppendAllText($sdl,'changed')
    & (Join-Path $PSScriptRoot 'Prepare-ScrcpyRuntime.ps1') -DestinationDirectory $destination
    if((Get-FileHash -LiteralPath $sdl -Algorithm SHA256).Hash -ne $record.files.'SDL3.dll') {throw 'Changed runtime file was not restored from verified archive'}
    Write-Output 'PASS: repeated preparation restores a changed runtime file from the verified archive'
} finally {if(Test-Path -LiteralPath $testRoot) {Remove-Item -LiteralPath $testRoot -Recurse -Force}}
