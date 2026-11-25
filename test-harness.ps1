$ErrorActionPreference = "Stop"
$exe = "C:\Users\Sundowner\Documents\Strata\Workspace\aldis-lamp\plugin\windows\aldis-bridge.exe"

Start-Process $exe -WindowStyle Hidden
Start-Sleep -Seconds 2

$tcp = New-Object System.Net.Sockets.TcpClient("127.0.0.1", 12346)
$tcp.NoDelay = $true
$s = $tcp.GetStream()
$s.ReadTimeout = 8000

$ng = [byte[]](1..16 | ForEach-Object { [byte](Get-Random -Maximum 256) })
$wkey = [Convert]::ToBase64String($ng)
$req = "GET / HTTP/1.1`r`nHost: 127.0.0.1:12346`r`nUpgrade: websocket`r`nConnection: Upgrade`r`nSec-WebSocket-Key: $wkey`r`nSec-WebSocket-Version: 13`r`n`r`n"
$rb = [Text.Encoding]::ASCII.GetBytes($req)
$s.Write($rb, 0, $rb.Length); $s.Flush()

# read handshake up to blank line
$hdr = New-Object System.Text.StringBuilder
while ($true) {
    $b = $s.ReadByte()
    if ($b -lt 0) { throw "handshake closed" }
    [void]$hdr.Append([char]$b)
    $t = $hdr.ToString()
    if ($t.EndsWith("`r`n`r`n")) { break }
}
"HTTP: " + ($hdr.ToString() -split "`r`n")[0]

function Send-Json([System.IO.Stream]$st, [string]$json) {
    $payload = [Text.Encoding]::UTF8.GetBytes($json)
    $frame = New-Object System.Collections.Generic.List[byte]
    $frame.Add([byte]0x81)
    $mask = [byte[]](1..4 | ForEach-Object { [byte](Get-Random -Minimum 1 -Maximum 255) })
    if ($payload.Length -lt 126) { $frame.Add([byte](0x80 -bor $payload.Length)) }
    else {
        $frame.Add([byte](0x80 -bor 126))
        $frame.Add([byte]($payload.Length -shr 8))
        $frame.Add([byte]($payload.Length -band 0xFF))
    }
    foreach ($m in $mask) { $frame.Add($m) }
    for ($i = 0; $i -lt $payload.Length; $i++) { $frame.Add([byte]($payload[$i] -bxor $mask[$i % 4])) }
    $bytes = $frame.ToArray()
    $st.Write($bytes, 0, $bytes.Length); $st.Flush()
}

function Read-Json([System.IO.Stream]$st) {
    $b0 = $st.ReadByte(); if ($b0 -lt 0) { return $null }
    $b1 = $st.ReadByte()
    $len = $b1 -band 0x7F
    if ($len -eq 126) { $len = ($st.ReadByte() -shl 8) -bor $st.ReadByte() }
    elseif ($len -eq 127) { $len = 0; for ($n=0; $n -lt 8; $n++) { $len = ($len -shl 8) -bor $st.ReadByte() } }
    $buf = New-Object byte[] $len
    $off = 0
    while ($off -lt $len) { $off += $st.Read($buf, $off, $len - $off) }
    return [Text.Encoding]::UTF8.GetString($buf)
}

Send-Json $s '{"type":"hello"}'
"hello -> " + (Read-Json $s)
Send-Json $s '{"type":"pi","note":"harness breadcrumb"}'
Send-Json $s '{"type":"element","event":"dot","wpm":20,"key":"k"}'
"dot   -> " + (Read-Json $s)
Send-Json $s '{"type":"send","text":"SOS","wpm":20,"key":"k"}'
"send  -> " + (Read-Json $s)
Start-Sleep -Milliseconds 400
Send-Json $s '{"type":"stop"}'
"stop  -> " + (Read-Json $s)
Send-Json $s '{"type":"detect"}'
$deadline = (Get-Date).AddSeconds(20)
while ($true) {
    $m = Read-Json $s
    if ($m -and $m.Contains('"detect"')) { "detect-> " + $m; break }
    if ((Get-Date) -gt $deadline) { "detect timed out"; break }
}

$s.Close(); $tcp.Close()
Start-Sleep -Seconds 1
Get-Process aldis-bridge -ErrorAction SilentlyContinue | Stop-Process -Force
"--- bridge.log ---"
Get-Content "$env:LOCALAPPDATA\AldisLamp\bridge.log" -Tail 40
