function Measure-P0Evidence {
    param([string[]]$ProbeLines, [string[]]$AgentLines, [string]$Session,
        [int]$DeviceId, [int]$DisplayId, [string]$CameraId)
    $submitted = [Collections.Generic.HashSet[int]]::new()
    foreach ($line in $AgentLines) {
        try {
            $value = $line | ConvertFrom-Json -ErrorAction Stop
            if ($value.event -eq 'frame-submitted' -and $null -ne $value.marker) {
                [int]$marker = 0
                if ([int]::TryParse([string]$value.marker, [ref]$marker) -and $marker -ge 0 -and $marker -le 65535) {
                    $null = $submitted.Add($marker)
                }
            }
        } catch { }
    }
    $count = 0; $failures = 0; $lastMarker = -1; [long]$lastTime = -1; $lastAccepted = 0
    foreach ($line in $ProbeLines) {
        try { $value = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
        if ($value.session -ne $Session) { continue }
        if ($value.event -in @('probe-failed','probe-start-failed')) { $failures++; continue }
        if ($value.event -ne 'frame-read' -or $null -eq $value.marker -or $null -eq $value.timestamp -or $null -eq $value.accepted -or
                $value.deviceId -ne $DeviceId -or $value.displayId -ne $DisplayId -or $value.cameraId -ne $CameraId) { continue }
        [int]$marker = 0; [long]$timestamp = 0; [int]$accepted = 0
        if (-not [int]::TryParse([string]$value.marker,[ref]$marker) -or
                -not [long]::TryParse([string]$value.timestamp,[ref]$timestamp) -or
                -not [int]::TryParse([string]$value.accepted,[ref]$accepted)) { continue }
        if (-not $submitted.Contains($marker) -or $timestamp -le $lastTime -or $accepted -le $lastAccepted) { continue }
        $delta = ($marker - $lastMarker) -band 65535
        if ($lastMarker -ge 0 -and ($delta -eq 0 -or $delta -ge 32768)) { continue }
        $count++; $lastMarker = $marker; $lastTime = $timestamp; $lastAccepted = $accepted
    }
    return [PSCustomObject]@{Count=$count;Failures=$failures}
}
