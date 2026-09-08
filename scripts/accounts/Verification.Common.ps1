function Check([bool]$ok, [string]$message) {
    if (-not $ok) { Receipt "FAILED assertion: $message"; throw $message }
}
function Receipt([string]$message) { $script:receipts.Add($message) }
function Native([string]$exe, [string[]]$argv) {
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $text = & $exe @argv 2>&1; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $old }
    Check ($code -eq 0) 'Native operation failed (output suppressed)'
    return ($text -join "`n").Trim()
}
function Sql([string]$query) {
    return Native $script:psql @('-X','-w','-q','-t','-A','-h','127.0.0.1','-p','56432','-d','zapara_test','-v','ON_ERROR_STOP=1','-c',$query)
}
function Save-Private([string]$name, [string]$text) {
    $path = Join-Path $script:private $name
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
    return $path
}
function Start-Api {
    $port = 5190
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
    try { $listener.Start() }
    catch { throw 'Account proof port 5190 unavailable' }
    finally { $listener.Stop() }
    $script:baseUrl = "http://127.0.0.1:$port"
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $script:runtime
    $info.Arguments = '"' + (Join-Path $script:out 'server\Zapara.Server.dll') + '" --urls ' + $script:baseUrl
    $info.WorkingDirectory = $script:out; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $script:api = [Diagnostics.Process]::new(); $script:api.StartInfo = $info
    Check ($script:api.Start()) 'Owned API start failed'
    Receipt "started pid=$($script:api.Id) loopback-port=$port"
    $script:stdout = $script:api.StandardOutput.ReadToEndAsync()
    $script:stderr = $script:api.StandardError.ReadToEndAsync()
    for ($i = 0; $i -lt 50; $i++) {
        Check (-not $script:api.HasExited) 'Owned API exited'
        $status = & $script:curl -s --noproxy '*' --max-time 1 -o NUL -w '%{http_code}' "$script:baseUrl/health/live"
        if ($LASTEXITCODE -eq 0 -and $status -eq '200') { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'Owned API readiness timeout'
}
function Stop-Api {
    if ($null -eq $script:api) { return }
    $process = $script:api
    try {
        if (-not $process.HasExited) { $process.Kill() }
        Check ($process.WaitForExit(10000)) 'Owned API stop failed'
        Receipt "stopped pid=$($process.Id)"
        $logs = $script:stdout.GetAwaiter().GetResult() + $script:stderr.GetAwaiter().GetResult()
        foreach ($secret in $script:secrets) {
            Check (-not $logs.Contains($secret)) 'Credential canary found in application logs'
        }
        Receipt 'application-log credential canaries absent=true'
    } finally { $process.Dispose(); $script:api = $null }
}
function Http([string]$method, [string]$path, [int]$expected, $body = $null, [string]$token = '', [string]$code = '') {
    Check ($null -ne $script:api -and -not $script:api.HasExited) 'Owned API not running'
    $requestHeaders = @('Content-Type: application/json')
    if ($token) { $requestHeaders += "Authorization: Bearer $token" }
    $headerFile = Save-Private 'request.headers' ($requestHeaders -join "`r`n")
    $responseFile = Join-Path $script:private 'response.json'
    $responseHeaders = Join-Path $script:private 'response.headers'
    $argv = @('-s','--noproxy','*','--max-time','10','-X',$method,'--header',"@$headerFile",'-D',$responseHeaders,
        '-o',$responseFile,'-w','%{http_code}',"$script:baseUrl$path")
    if ($null -ne $body) {
        $bodyFile = Save-Private 'request.json' ($body | ConvertTo-Json -Depth 8 -Compress)
        $argv += @('--data-binary',"@$bodyFile")
    }
    $status = Native $script:curl $argv
    Check ($status -eq [string]$expected) "HTTP status mismatch: $method $path expected=$expected actual=$status"
    $text = [IO.File]::ReadAllText($responseFile)
    if ($path.StartsWith('/api/v1/')) {
        Check ([IO.File]::ReadAllText($responseHeaders) -match '(?im)^Cache-Control: no-store\s*$') 'Missing no-store'
    }
    if ($code) {
        $errorBody = $text | ConvertFrom-Json
        Check ($errorBody.code -eq $code -and $errorBody.status -eq $expected) 'Wrong safe error'
        Check ((@($errorBody.PSObject.Properties.Name | Sort-Object) -join ',') -eq 'code,status,title') 'Wrong error fields'
        foreach ($secret in $script:secrets) { Check (-not $text.Contains($secret)) 'Error credential canary found' }
    }
    Receipt "$method $path status=$status no-store=$($path.StartsWith('/api/v1/')) safe-error-fields=$([bool]$code)"
    if ($expected -eq 204) { Check ($text.Length -eq 0) 'Nonempty 204'; return }
    return ($text | ConvertFrom-Json)
}
function Login([string]$password) {
    $session = Http 'POST' '/api/v1/auth/login' 200 @{
        username = 'http.synthetic'; password = $password
        device = @{ deviceId = [Guid]::NewGuid().ToString(); deviceName = 'Synthetic CLI'; platform = 'windows' }
    }
    $script:secrets.Add($session.accessToken); $script:secrets.Add($session.refreshToken)
    return $session
}
