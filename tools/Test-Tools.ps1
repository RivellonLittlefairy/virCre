#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Device.ps1')
. (Join-Path $PSScriptRoot 'Evidence.ps1')
function Assert-Equal($actual, $expected, [string]$message) {
    if ($actual -ne $expected) { throw "$message (actual=$actual, expected=$expected)" }
}
$session = '00112233445566778899aabbccddeeff'
$agent = @('{"event":"frame-submitted","marker":0}', '{"event":"frame-submitted","marker":1}', '{"event":"frame-submitted","marker":2}')
$nonFrames = @('{"event":"activity-context","session":"00112233445566778899aabbccddeeff"}',
    '{"event":"probe-failed","session":"00112233445566778899aabbccddeeff"}')
$result = Measure-P0Evidence -ProbeLines $nonFrames -AgentLines $agent -Session $session -DeviceId 6 -DisplayId 19 -CameraId 'v6_test'
Assert-Equal $result.Count 0 'Metadata must never count as marker zero'
Assert-Equal $result.Failures 1 'Terminal probe failures must be surfaced'
$real = @(0..2 | ForEach-Object { @{event='frame-read';session=$session;deviceId=6;displayId=19;cameraId='v6_test';marker=$_;accepted=($_+1);timestamp=($_+100)} | ConvertTo-Json -Compress })
$result = Measure-P0Evidence -ProbeLines ($real + $real + '{"event":"frame-read","session":"foreign","marker":0}') -AgentLines $agent -Session $session -DeviceId 6 -DisplayId 19 -CameraId 'v6_test'
Assert-Equal $result.Count 3 'Only increasing, matching session frames count, duplicates do not'
$result = Measure-P0Evidence -ProbeLines $real -AgentLines $agent -Session $session -DeviceId 7 -DisplayId 19 -CameraId 'v6_test'
Assert-Equal $result.Count 0 'Wrong device must not pass'
$processResult = Invoke-BoundedProcess -FilePath (Get-Process -Id $PID).Path -Arguments @('-NoProfile','-Command','[Console]::Write("ok"); exit 7') -TimeoutMilliseconds 10000
Assert-Equal $processResult.ExitCode 7 'Child exit code is preserved'
Assert-Equal $processResult.Output 'ok' 'Child output is preserved'
$timer = [Diagnostics.Stopwatch]::StartNew()
$timedOut = $false
try { Invoke-BoundedProcess -FilePath (Get-Process -Id $PID).Path -Arguments @('-NoProfile','-Command','Start-Sleep -Seconds 20') -TimeoutMilliseconds 500 | Out-Null }
catch { $timedOut = $_.Exception.Message -match 'timed out' }
Assert-Equal $timedOut $true 'A stalled process must time out'
if ($timer.Elapsed.TotalSeconds -gt 5) { throw 'Timeout did not bound the process lifetime' }
Write-Output 'PASS: metadata false positives, monotonic/session evidence, failure reporting, wrong device, child output/exit, bounded timeout'
