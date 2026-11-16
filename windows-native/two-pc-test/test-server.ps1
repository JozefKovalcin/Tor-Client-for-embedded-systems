param([ValidateRange(1,65535)][int]$Port=8081)
$ErrorActionPreference='Stop'
function Reply($stream,[int]$code,[string]$reason,[byte[]]$body,[string]$type) {
    $header=[Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $code $reason`r`nContent-Length: $($body.Length)`r`nContent-Type: $type`r`nConnection: close`r`n`r`n")
    $stream.Write($header,0,$header.Length)
    if ($body.Length) { $stream.Write($body,0,$body.Length) }
    $stream.Flush()
}
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$Port)
try {
    $listener.Start()
    Write-Host "Test server: 127.0.0.1:$Port. Nechaj toto okno otvorene. Koniec: Ctrl+C."
    while ($true) {
        while (!$listener.Pending()) { Start-Sleep -Milliseconds 100 }
        $client=$listener.AcceptTcpClient()
        try {
            $client.ReceiveTimeout=300000; $client.SendTimeout=300000
            $stream=$client.GetStream()
            $header=[Collections.Generic.List[byte]]::new()
            do {
                $next=$stream.ReadByte()
                if ($next -lt 0) { throw 'Spojenie sa zavrelo pred HTTP poziadavkou.' }
                $header.Add([byte]$next)
                $count=$header.Count
                if ($count -gt 16384) { throw 'HTTP hlavicka je prilis velka.' }
            } until ($count -ge 4 -and $header[$count-4] -eq 13 -and $header[$count-3] -eq 10 -and $header[$count-2] -eq 13 -and $header[$count-1] -eq 10)
            $text=[Text.Encoding]::ASCII.GetString($header.ToArray())
            $line=$text.Split([string[]]@("`r`n"),[StringSplitOptions]::None)[0]
            if ($line -match '^GET /health HTTP/1\.[01]$') {
                Reply $stream 200 'OK' ([Text.Encoding]::UTF8.GetBytes("TOR TEST SERVER OK`n")) 'text/plain'
            } elseif ($line -match '^POST /echo HTTP/1\.[01]$') {
                if ($text -match '(?im)^Transfer-Encoding:') { throw 'Chunked prenos tento test nepouziva.' }
                $match=[regex]::Match($text,'(?im)^Content-Length:\s*(\d+)\s*$')
                if (!$match.Success) { throw 'Chyba Content-Length.' }
                $size=[long]$match.Groups[1].Value
                if ($size -gt 4194304) { throw 'Test podporuje najviac 4 MiB.' }
                if ($text -match '(?im)^Expect:\s*100-continue\s*$') {
                    $continue=[Text.Encoding]::ASCII.GetBytes("HTTP/1.1 100 Continue`r`n`r`n")
                    $stream.Write($continue,0,$continue.Length); $stream.Flush()
                }
                $body=New-Object byte[] ([int]$size)
                $received=0
                while ($received -lt $body.Length) {
                    $n=$stream.Read($body,$received,[Math]::Min(65536,$body.Length-$received))
                    if ($n -eq 0) { throw 'Prenos sa prerusil pred prijatim vsetkych dat.' }
                    $received+=$n
                }
                Reply $stream 200 'OK' $body 'application/octet-stream'
                Write-Host "OK: prijatych a vratenych $received bajtov."
            } else {
                Reply $stream 404 'Not Found' ([Text.Encoding]::ASCII.GetBytes('Not found')) 'text/plain'
            }
        } catch {
            Write-Host ('Poziadavka sa nepodarila: '+$_.Exception.Message)
        } finally { $client.Dispose() }
    }
} finally { $listener.Stop() }
