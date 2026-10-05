function Wait-AccountLogoutCheckpoints {
    param([string]$Output, [string]$Schema, [string]$Mode, [Diagnostics.Process]$Server, [Diagnostics.Process]$Driver)
    if ($Schema -notmatch '^acc_ui_[a-f0-9]{32}$' -or $Mode -notin @('online','offline')) { throw 'Logout fixture scope rejected' }
    $identity = $null
    foreach ($stage in @('active','cancelled','ready','result')) {
        $path = Join-Path $Output ('logout-' + $stage)
        $deadline = [Diagnostics.Stopwatch]::StartNew()
        while (!(Test-Path -LiteralPath ($path + '.request.json'))) {
            if ($Driver.HasExited) { throw "Logout driver exited before $stage" }
            if ($deadline.Elapsed.TotalSeconds -gt 45) { throw "Logout checkpoint timeout: $stage" }
            [Threading.Thread]::Sleep(100)
        }
        $request = [IO.File]::ReadAllText($path + '.request.json') | ConvertFrom-Json
        $userId = ([Guid]::Parse($request.UserId)).ToString('D')
        $familyId = ([Guid]::Parse($request.FamilyId)).ToString('D')
        $key = "$userId/$familyId"
        if ($null -eq $identity) { $identity = $key }
        if ($identity -ne $key) { throw 'Logout family changed between checkpoints' }
        $query = @"
SELECT json_build_object(
 'userId',f.user_id,'familyId',f.family_id,'revoked',f.revoked_at IS NOT NULL,
 'reason',f.revocation_reason,'observedUtc',clock_timestamp(),
 'live',f.revoked_at IS NULL AND f.expires_at>now() AND u.status='active',
 'families',(SELECT count(*) FROM $Schema.session_families),
 'access',(SELECT count(*) FROM $Schema.access_tokens WHERE family_id=f.family_id AND expires_at>now()),
 'refresh',(SELECT count(*) FROM $Schema.refresh_tokens WHERE family_id=f.family_id AND consumed_at IS NULL AND expires_at>now()),
 'loginAudit',(SELECT count(*) FROM $Schema.account_security_events WHERE family_id=f.family_id AND action='login' AND outcome='success'),
 'logoutAudit',(SELECT count(*) FROM $Schema.account_security_events WHERE family_id=f.family_id AND action='logout' AND outcome='success'),
 'otherMutations',(SELECT count(*) FROM $Schema.account_security_events WHERE action NOT IN ('register','login','logout')))
FROM $Schema.session_families f JOIN $Schema.users u ON u.user_id=f.user_id
WHERE f.family_id='$familyId' AND f.user_id='$userId'
"@
        $poll = [Diagnostics.Stopwatch]::StartNew()
        $attempts = 0
        do {
            $attempts++
            $previous = $env:PGOPTIONS
            try {
                $env:PGOPTIONS = '-c statement_timeout=3000'
                $raw = & psql.exe -X -w -t -A -v ON_ERROR_STOP=1 -c $query
                if ($LASTEXITCODE -ne 0 -or !$raw) { throw 'Scoped logout SQL query failed' }
            } finally { $env:PGOPTIONS = $previous }
            $state = $raw | ConvertFrom-Json
            if ($stage -ne 'result' -or $Mode -eq 'offline' -or $state.revoked) { break }
            if ($poll.Elapsed.TotalSeconds -gt 20) { throw 'Exact family revocation not observed within 20 seconds' }
            [Threading.Thread]::Sleep(100)
        } while ($true)
        [IO.File]::WriteAllText($path + '.sql.json', (@{ stage=$stage; mode=$Mode; attempts=$attempts; elapsedMs=$poll.ElapsedMilliseconds; state=$state } | ConvertTo-Json -Depth 5))
        if ($state.families -ne 1 -or $state.loginAudit -ne 1 -or $state.otherMutations -ne 0) { throw 'Not an ordinary fresh sole session' }
        if ($stage -eq 'result' -and $Mode -eq 'online') {
            if (!$state.revoked -or $state.reason -ne 'logout' -or $state.logoutAudit -ne 1) { throw 'Exact family logout audit/revocation mismatch' }
        } else {
            if (!$state.live -or $state.revoked -or $state.logoutAudit -ne 0 -or $state.access -ne 1 -or $state.refresh -ne 1) { throw 'Expected untouched live session family' }
        }
        if ($stage -eq 'ready' -and $Mode -eq 'offline') {
            if ($Server.HasExited) { throw 'Own server already exited before offline control' }
            $Server.Kill()
            if (!$Server.WaitForExit(5000)) { throw 'Owned server did not stop' }
            $tcp = [Net.Sockets.TcpClient]::new()
            try {
                $connectWatch = [Diagnostics.Stopwatch]::StartNew()
                $task = $tcp.ConnectAsync('127.0.0.1',5193)
                try { [void]$task.Wait(10000) } catch [AggregateException] { }
                $refused = $task.IsFaulted -and $task.Exception.InnerException -is [Net.Sockets.SocketException] -and $task.Exception.InnerException.SocketErrorCode -eq [Net.Sockets.SocketError]::ConnectionRefused
                if (!$refused -or $tcp.Connected) { throw 'Offline loopback endpoint not conclusively refused' }
            } finally { $tcp.Dispose() }
            [IO.File]::WriteAllText((Join-Path $Output 'offline-control.json'), (@{ serverPid=$Server.Id; exited=$Server.HasExited; connectionRefused=$refused; elapsedMs=$connectWatch.ElapsedMilliseconds; utc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json))
        }
        [IO.File]::WriteAllText($path + '.ok', [DateTime]::UtcNow.ToString('o'))
    }
}
