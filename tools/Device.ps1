# Shared by inspection and the bounded P0 session runner. Always addresses one explicit device.
function Invoke-BoundedProcess {
    param([string]$FilePath, [string[]]$Arguments, [int]$TimeoutMilliseconds = 15000, [string]$CancelFile)
    if($CancelFile -and (Test-Path -LiteralPath $CancelFile)) { throw [OperationCanceledException]::new('Startup cancelled by user') }
    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo.FileName = $FilePath
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.CreateNoWindow = $true
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        foreach ($argument in $Arguments) { $process.StartInfo.ArgumentList.Add($argument) }
        $null = $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timer=[Diagnostics.Stopwatch]::StartNew()
        while(-not $process.WaitForExit(100)) {
            if($CancelFile -and (Test-Path -LiteralPath $CancelFile)) {
                $process.Kill($true);$null=$process.WaitForExit(1000)
                throw [OperationCanceledException]::new('Startup cancelled by user')
            }
            if($timer.ElapsedMilliseconds -ge $TimeoutMilliseconds) {
                $process.Kill($true)
                $null = $process.WaitForExit(1000)
                throw "Process timed out after $TimeoutMilliseconds ms: $FilePath"
            }
        }
        return [PSCustomObject]@{ExitCode=$process.ExitCode;Output=$stdout.GetAwaiter().GetResult();Error=$stderr.GetAwaiter().GetResult()}
    } finally { $process.Dispose() }
}

function Invoke-DeviceAdb {
    param([Parameter(Mandatory)][string[]]$AdbArgs, [ValidateRange(1,60)][int]$TimeoutSeconds = 15)
    $cancelPath=if(-not $script:CleaningUp){$script:StopFile}else{$null}
    $result = Invoke-BoundedProcess -FilePath $script:Adb -Arguments (@('-s',$script:Serial) + $AdbArgs) -TimeoutMilliseconds ($TimeoutSeconds * 1000) -CancelFile $cancelPath
    if ($result.ExitCode -ne 0) { throw "adb $($AdbArgs -join ' ') failed: $($result.Output)$($result.Error)" }
    return ($result.Output + $result.Error).Trim()
}

function Assert-P0Device {
    if ($script:Serial -notmatch '^[a-zA-Z0-9._:-]+$') { throw 'Unsupported ADB serial syntax' }
    $state = Invoke-DeviceAdb -AdbArgs @('get-state')
    if ($state -ne 'device') { throw "Selected device is not authorized: $state" }
    $sdk = Invoke-DeviceAdb -AdbArgs @('shell','getprop','ro.build.version.sdk')
    if ([int]$sdk -lt 36) { throw "Android 16 / API 36 required, found $sdk" }
    $identity = [ordered]@{
        serial = $script:Serial
        sdk = $sdk
        model = Invoke-DeviceAdb -AdbArgs @('shell','getprop','ro.product.model')
        fingerprint = Invoke-DeviceAdb -AdbArgs @('shell','getprop','ro.build.fingerprint')
    }
    return $identity
}

# Recover tcp:0 even if the creating adb client was cancelled before returning its port.
function Remove-SessionForward {
    param([string]$Serial,[string]$Session)
    $lines=Invoke-DeviceAdb -AdbArgs @('forward','--list') -TimeoutSeconds 5
    foreach($line in ($lines -split "`n")) {
        $parts=$line.Trim() -split '\s+'
        if($parts.Count -eq 3 -and $parts[0] -ceq $Serial -and $parts[2] -ceq "localabstract:vircre-$Session") {
            if($parts[1] -notmatch '^tcp:[0-9]+$') {throw 'Unexpected session forward endpoint'}
            Invoke-DeviceAdb -AdbArgs @('forward','--remove',$parts[1]) -TimeoutSeconds 5|Out-Null
        }
    }
}
