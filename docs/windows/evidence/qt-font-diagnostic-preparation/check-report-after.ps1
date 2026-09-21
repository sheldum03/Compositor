$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$reportPath = '/Users/admin/.codex/worktrees/192b/Compositor/docs/windows/evidence/qt-windows-server/raw/text/text-report.json'
$observed = @()
foreach ($mode in @('normal','missing-font-field')) {
    $code = 0; $errorText = $null; $complete = $false; $fonts = @()
    try {
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($mode -eq 'missing-font-field') { foreach ($r in $report.results) { $r.PSObject.Properties.Remove('resolvedFonts') } }
        $complete = $report.windowsExecuted -eq $true -and @($report.results).Count -eq 12
        $fonts = @($report.results | ForEach-Object { $_.resolvedFonts } | Sort-Object -Unique)
    } catch { $complete = $false; $errorText = $_.ToString() }
    $result = @{case=$mode; exitCode=$code; reportComplete=$complete; resolvedFonts=$fonts; error=$errorText}
    $completed = @($result | Where-Object { $_.exitCode -ne 0 -or -not $_.reportComplete }).Count -eq 0
    $observed += @{case=$mode; caughtError=($null -ne $errorText); completedDespiteCaughtError=$completed; fonts=$fonts}
}
$observed | ConvertTo-Json -Depth 5
