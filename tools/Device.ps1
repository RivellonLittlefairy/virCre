# Shared by inspection and the bounded P0 session runner. Always addresses one explicit device.
function Invoke-BoundedProcess {
    param([string]$FilePath, [string[]]$Arguments, [int]$TimeoutMilliseconds = 15000)
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
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            $process.Kill($true)
            $null = $process.WaitForExit(1000)
            throw "Process timed out after $TimeoutMilliseconds ms: $FilePath"
        }
        return [PSCustomObject]@{ExitCode=$process.ExitCode;Output=$stdout.GetAwaiter().GetResult();Error=$stderr.GetAwaiter().GetResult()}
    } finally { $process.Dispose() }
}

function Invoke-DeviceAdb {
    param([Parameter(Mandatory)][string[]]$AdbArgs, [ValidateRange(1,60)][int]$TimeoutSeconds = 15)
    $result = Invoke-BoundedProcess -FilePath $script:Adb -Arguments (@('-s',$script:Serial) + $AdbArgs) -TimeoutMilliseconds ($TimeoutSeconds * 1000)
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
