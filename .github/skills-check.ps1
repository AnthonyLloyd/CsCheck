# Checks that the finding-test-gaps scripts work on this OS, on CsCheck's PCG and its round-trip tests: coverage from the TUnit test app,
# uncovered.cs and candidates.cs on it, and mutants.cs on the candidates in the members those tests pin, with the survivors rerun against
# the SeedString tests, which kill some of them. Run it from the repo root.
# The filters leave out PCGTests' timing tests, which four copies run at once would make flaky.
param([string]$Framework = 'net10.0')
$ErrorActionPreference = 'Stop'
$scripts = 'plugins/cscheck/skills/finding-test-gaps/scripts'
$filter = '/*/*/PCGTests/*Round*'
$rerunFilter = '/*/*/PCGTests/SeedString*'
$members = 'PCG.ToString,Index,SeedString.Parse'
$out = Join-Path ($env:RUNNER_TEMP ?? [IO.Path]::GetTempPath()) 'skills-check'
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

function Invoke-Native([string]$Name, [scriptblock]$Command) {
    Write-Host "--- $Name"
    $text = (& $Command 2>&1 | ForEach-Object { Write-Host $_; "$_" }) -join "`n"
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "$Name exited with $code" }
    $text
}
function Assert-That([bool]$Ok, [string]$What) { if (-not $Ok) { throw "check failed: $What" } }

Invoke-Native 'coverage of the round-trip tests' { dotnet run -c Release --project Tests -f $Framework -- --coverage --coverage-output-format cobertura --results-directory "$out/coverage-1" --treenode-filter $filter } | Out-Null

$uncovered = Invoke-Native 'uncovered.cs' { dotnet run --file "$scripts/uncovered.cs" -- "$out/coverage-1" PCG.cs --sweep "$out/sweep.md" }
Assert-That ($uncovered -match 'PCG\.cs: \d+ of \d+ lines run') 'uncovered.cs found PCG.cs in the coverage'
Assert-That (Test-Path -LiteralPath "$out/sweep.md") 'uncovered.cs --sweep wrote the table'

$listing = Invoke-Native 'candidates.cs' { dotnet run --file "$scripts/candidates.cs" -- CsCheck/PCG.cs --members $members --coverage "$out/coverage-1" --out "$out/pcg.candidates.json" }
Assert-That ($listing -notmatch "isn't in the coverage") 'candidates.cs matched PCG.cs in the coverage'
$ids = @([regex]::Matches($listing, '(?m)^  (\S+) +\d+  ') | ForEach-Object { $_.Groups[1].Value })
Assert-That ($ids.Count -ge 4) "candidates.cs lists at least 4 candidates in $members, not $($ids.Count)"

$spec = [ordered]@{ testProject = 'Tests'; framework = $Framework; filter = $filter; rerunFilter = $rerunFilter; candidates = 'pcg.candidates.json'; mutants = @($ids | ForEach-Object { @{ id = $_ } }) }
$spec | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$out/pcg.json"
Invoke-Native 'mutants.cs' { dotnet run --file "$scripts/mutants.cs" -- "$out/pcg.json" } | Out-Null
$results = @(Get-Content -LiteralPath "$out/pcg-run/results.json" -Raw | ConvertFrom-Json)
Assert-That ($results.Count -eq $ids.Count) "a result for each of the $($ids.Count) mutants, not $($results.Count)"
$errors = @($results | Where-Object status -eq 'ERROR')
Assert-That ($errors.Count -eq 0) "no mutant ends in ERROR, but $($errors.id -join ', ') did"
Assert-That (@($results | Where-Object status -in 'KILLED', 'CRASHED').Count -gt 0) 'at least one mutant is killed'
Assert-That (@($results | Where-Object detail -like '*outside the filter').Count -gt 0) 'the rerun catches at least one survivor, outside the filter'
Assert-That (@($results | Where-Object status -eq 'SURVIVED').Count -gt 0) 'at least one mutant survives'
Write-Host "--- passed: $(($results | Group-Object status | ForEach-Object { "$($_.Count) $($_.Name)" }) -join ', ')"
