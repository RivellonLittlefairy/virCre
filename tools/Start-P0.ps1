#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Serial,
    [ValidateRange(5,600)][int]$Seconds = 30,
    [string]$Adb = 'adb',
    [string]$Scrcpy = 'scrcpy',
    [switch]$NoMirror,
    [switch]$SkipInstall
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Device.ps1')
. (Join-Path $PSScriptRoot 'Evidence.ps1')
$repoRoot = Split-Path $PSScriptRoot -Parent
$apk = Join-Path $repoRoot 'android\app\build\outputs\apk\debug\app-debug.apk'
if (-not (Test-Path -LiteralPath $apk)) { throw 'Build APK first with tools/Build.ps1' }
$identity = Assert-P0Device
$session = [Guid]::NewGuid().ToString('N')
$sessionDir = Join-Path $repoRoot "out\$session"
New-Item -ItemType Directory -Path $sessionDir -Force | Out-Null
$identity | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sessionDir 'device.json') -Encoding utf8
$sessionBytes = [Guid]::NewGuid().ToByteArray()
$mac = '02:56:43:{0:x2}:{1:x2}:{2:x2}' -f $sessionBytes[0], $sessionBytes[1], $sessionBytes[2]
$seed = [int]$sessionBytes[3] * 256 + $sessionBytes[4]
$remoteApk = "/data/local/tmp/vircre-$session.apk"
$agentOut = Join-Path $sessionDir 'agent.jsonl'
$agentErr = Join-Path $sessionDir 'agent.stderr.log'
$agent = $null
$mirror = $null
$remotePid = $null
$associationCreated = $false
$pushed = $false
$status = 'failed'
$cleanupErrors = [System.Collections.Generic.List[string]]::new()
$ready = $null
$probeStopAt = $null
$probeStopped = $false
$userId = Invoke-DeviceAdb -AdbArgs @('shell','am','get-current-user')
if ($userId -ne '0') { throw 'P0 currently supports the primary Android user (0) only' }
try {
    Write-Host "P0 device: $($identity.model), API $($identity.sdk). Session: $session"
    if (-not $SkipInstall) { Write-Host (Invoke-DeviceAdb -AdbArgs @('install','-r','-t',$apk) -TimeoutSeconds 60) }
    Invoke-DeviceAdb -AdbArgs @('shell','am','force-stop','com.example.vircam') | Out-Null
    $packageInfo = Invoke-DeviceAdb -AdbArgs @('shell','dumpsys','package','com.example.vircam')
    if ($packageInfo -notmatch 'android.permission.CAMERA: granted=true') {
        Write-Host 'Please allow the normal Android camera permission prompt on the phone screen (no physical camera will be opened).'
        Invoke-DeviceAdb -AdbArgs @('shell','am','start','-W','--display','0','-n','com.example.vircam/.MainActivity',
            '--ez','preparePermission','true') | Out-Null
        $permissionDeadline = [DateTime]::UtcNow.AddSeconds(120)
        do {
            Start-Sleep -Milliseconds 750
            $packageInfo = Invoke-DeviceAdb -AdbArgs @('shell','dumpsys','package','com.example.vircam')
        } while ($packageInfo -notmatch 'android.permission.CAMERA: granted=true' -and [DateTime]::UtcNow -lt $permissionDeadline)
        if ($packageInfo -notmatch 'android.permission.CAMERA: granted=true') { throw 'Camera permission was not granted within 120 seconds' }
        Invoke-DeviceAdb -AdbArgs @('shell','am','force-stop','com.example.vircam') | Out-Null
    }
    $before = Invoke-DeviceAdb -AdbArgs @('shell','cmd','companiondevice','list',$userId)
    if ($before -match [regex]::Escape($mac)) { throw 'Association address collision; retry with a new session' }
    # Official CDM debugging entry point. Never fabricate association IDs or grant hidden permissions.
    $associationCreated = $true
    Invoke-DeviceAdb -AdbArgs @('shell','cmd','companiondevice','associate',$userId,'com.android.shell',$mac,
        'android.app.role.COMPANION_DEVICE_APP_STREAMING','true') | Out-Null
    $associationId = $null
    for ($i = 0; $i -lt 12; $i++) {
        $list = Invoke-DeviceAdb -AdbArgs @('shell','cmd','companiondevice','list',$userId)
        $pattern = '(?im)^\s*(\d+)\s*\|\s*com\.android\.shell\s*\|\s*' + [regex]::Escape($mac) + '\s*$'
        if ($list -match $pattern) { $associationId = $Matches[1]; break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $associationId) { throw 'CDM did not create a verifiable association' }
    Write-Host "CDM association: $associationId"
    [ordered]@{session=$session;serial=$Serial;associationId=$associationId;mac=$mac;remoteApk=$remoteApk;userId=$userId} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sessionDir 'session.json') -Encoding utf8
    Start-Sleep -Milliseconds 1000
    (Invoke-DeviceAdb -AdbArgs @('shell','cmd','companiondevice','list',$userId)) -split "`n" |
        Where-Object { $_ -match 'com\.android\.shell' -and $_ -match [regex]::Escape($mac) } |
        Set-Content -LiteralPath (Join-Path $sessionDir 'associations-after-create.txt') -Encoding utf8
    Invoke-DeviceAdb -AdbArgs @('push',$apk,$remoteApk) | Out-Null
    $pushed = $true
    $agentCommand = "CLASSPATH=$remoteApk app_process /system/bin com.example.vircam.shell.ShellMain $associationId $Seconds $session $seed"
    $agent = Start-Process -FilePath $Adb -ArgumentList @('-s',$Serial,'shell',$agentCommand) -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $agentOut -RedirectStandardError $agentErr
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds + 20)
    while (-not $agent.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        if ($probeStopAt -and -not $probeStopped -and [DateTime]::UtcNow -ge $probeStopAt) {
            # Stop the consumer before the planned camera teardown, rather than reporting
            # the expected teardown as an unexpected camera disconnection.
            Invoke-DeviceAdb -AdbArgs @('shell','am','force-stop','com.example.vircam') | Out-Null
            $probeStopped = $true
        }
        if ($mirror -and $mirror.HasExited) { throw 'The P0 mirror window closed; stopping this session' }
        if (Test-Path -LiteralPath $agentOut) {
            $events = @(Get-Content -LiteralPath $agentOut | ForEach-Object {
                try { $_ | ConvertFrom-Json -ErrorAction Stop } catch { }
            })
            $started = $events | Where-Object event -eq 'started' | Select-Object -First 1
            if ($started) { $remotePid = [int]$started.pid }
            if (-not $ready) {
                $ready = $events | Where-Object event -eq 'ready' | Select-Object -First 1
                if ($ready) {
                    $probeStopAt = [DateTime]::UtcNow.AddSeconds($Seconds - 2)
                    if (-not $NoMirror) {
                        $mirrorCommand = Get-Command $Scrcpy -ErrorAction SilentlyContinue
                        if ($mirrorCommand) {
                            # Interactive window: needed for the normal Android camera permission prompt.
                            $mirror = Start-Process -FilePath $mirrorCommand.Source -ArgumentList @('-s',$Serial,
                                "--display-id=$($ready.displayId)",'--no-audio','--window-title=VirCam-P0',
                                '--always-on-top','--window-x=100','--window-y=100') -PassThru `
                                -RedirectStandardOutput (Join-Path $sessionDir 'scrcpy.stdout.log') `
                                -RedirectStandardError (Join-Path $sessionDir 'scrcpy.stderr.log')
                        } else { Write-Warning 'scrcpy unavailable. The virtual permission dialog requires a display viewer.' }
                    }
                    Write-Host (Invoke-DeviceAdb -AdbArgs @('shell','am','start','-W','--display',"$($ready.displayId)",
                        '-n','com.example.vircam/.MainActivity','-f','0x18000000',
                        '--ei','virtualDeviceId',"$($ready.deviceId)",'--es','cameraId',"$($ready.cameraId)",
                        '--es','session',$session))
                }
            }
        }
        Start-Sleep -Milliseconds 250
        $agent.Refresh()
    }
    if (-not $agent.HasExited) { throw 'P0 watchdog deadline exceeded' }
    $agent.WaitForExit()
    # Full frame events stay in agent.jsonl; keep the interactive console bounded.
    Get-Content -LiteralPath $agentOut -Tail 8 | Write-Host
    if ($agent.ExitCode -ne 0) { throw "Agent failed ($($agent.ExitCode)); see $agentErr" }
    if (-not $ready) { throw 'Agent ended without a ready event' }
    $probeLog = Invoke-DeviceAdb -AdbArgs @('shell','run-as','com.example.vircam','cat',"files/p0-$session.jsonl")
    $probeLog | Set-Content -LiteralPath (Join-Path $sessionDir 'probe.log') -Encoding utf8
    $evidence = Measure-P0Evidence -ProbeLines ($probeLog -split "`n") -AgentLines (Get-Content -LiteralPath $agentOut) `
        -Session $session -DeviceId $ready.deviceId -DisplayId $ready.displayId -CameraId $ready.cameraId
    if ($evidence.Failures -gt 0 -or $evidence.Count -lt 3) { throw "No continuous matching camera evidence ($($evidence.Count) frames, $($evidence.Failures) failures)" }
    $status = 'input-path-verified'
    Write-Host "Input path verified: $($evidence.Count) matched Camera2 frames. Stability/physical-lens-cover checks remain separate."
} finally {
    if ($ready -and -not $probeStopped) {
        try { Invoke-DeviceAdb -AdbArgs @('shell','am','force-stop','com.example.vircam') -TimeoutSeconds 5 | Out-Null }
        catch { $cleanupErrors.Add($_.Exception.Message) }
    }
    if ($remotePid) {
        try {
            $commandLine = Invoke-DeviceAdb -AdbArgs @('shell','cat',"/proc/$remotePid/cmdline")
            if ($commandLine.Contains($session) -and $commandLine.Contains('com.example.vircam.shell.ShellMain')) {
                Invoke-DeviceAdb -AdbArgs @('shell','kill','-TERM',"$remotePid") | Out-Null
            }
        } catch { if ($agent -and -not $agent.HasExited) { $cleanupErrors.Add($_.Exception.Message) } }
    }
    if ($agent -and -not $agent.HasExited) { Stop-Process -Id $agent.Id -ErrorAction SilentlyContinue }
    if ($mirror -and -not $mirror.HasExited) { Stop-Process -Id $mirror.Id -ErrorAction SilentlyContinue }
    if ($associationCreated) {
        try { Invoke-DeviceAdb -AdbArgs @('shell','cmd','companiondevice','disassociate',$userId,'com.android.shell',$mac) | Out-Null }
        catch { $cleanupErrors.Add($_.Exception.Message) }
    }
    if ($pushed) {
        try { Invoke-DeviceAdb -AdbArgs @('shell','rm',$remoteApk) | Out-Null }
        catch { $cleanupErrors.Add($_.Exception.Message) }
    }
    if ($cleanupErrors.Count -gt 0) { $status = 'cleanup-failed' }
    [ordered]@{ session=$session; status=$status; cleanupErrors=@($cleanupErrors.ToArray()) } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sessionDir 'result.json') -Encoding utf8
    foreach ($cleanupError in $cleanupErrors) { Write-Warning "Cleanup: $cleanupError" }
    Write-Host "Evidence: $sessionDir"
}
if ($cleanupErrors.Count -gt 0) { throw 'P0 session left cleanup errors; inspect result.json and session.json' }
