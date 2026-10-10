$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root ('language-observation-' + (Get-Date -Format yyyyMMdd-HHmmss) + '.json')
if (Test-Path $out) { throw 'Refusing to overwrite an observation' }
$list = Get-WinUserLanguageList
$items = @(foreach ($entry in $list) {
 [ordered]@{languageTag=$entry.LanguageTag;inputMethodTips=@($entry.InputMethodTips)}
})
[ordered]@{utc=[DateTime]::UtcNow.ToString('o');scope='Read-only current-user language enumeration';collectionType=$list.GetType().FullName;count=$list.Count;languages=$items} | ConvertTo-Json -Depth 6 | Set-Content $out -Encoding UTF8
[pscustomobject]@{path=$out;bytes=(Get-Item $out).Length;sha256=(Get-FileHash $out).Hash} | ConvertTo-Json
