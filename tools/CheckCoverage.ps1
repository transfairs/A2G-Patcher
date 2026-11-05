# Fails unless the coverlet report under the given folder covers every line and branch of A2G-Patch.
# Run after: dotnet test ... --collect:"XPlat Code Coverage" --results-directory TestResults
param([string]$Results = "TestResults")

$report = Get-ChildItem $Results -Recurse -Filter coverage.cobertura.xml | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $report) { throw "No coverage.cobertura.xml under $Results." }
$coverage = ([xml](Get-Content -Raw $report.FullName)).coverage

$summary = "Lines: $($coverage.'lines-covered')/$($coverage.'lines-valid'), branches: $($coverage.'branches-covered')/$($coverage.'branches-valid')"
Write-Output $summary
if ($env:GITHUB_STEP_SUMMARY) { "### Test coverage`n`n$summary" | Add-Content $env:GITHUB_STEP_SUMMARY -Encoding utf8 }

$gaps = foreach ($class in $coverage.SelectNodes("//class")) {
    foreach ($line in $class.SelectNodes(".//line")) {
        $branchGap = $line.branch -eq "True" -and $line.'condition-coverage' -notlike "100%*"
        if ($line.hits -eq "0" -or $branchGap) {
            "$($class.filename):$($line.number) $(if ($line.hits -eq '0') { 'not run' } else { 'branches ' + $line.'condition-coverage' })"
        }
    }
}
if ($coverage.'lines-covered' -ne $coverage.'lines-valid' -or $coverage.'branches-covered' -ne $coverage.'branches-valid') {
    $gaps | Sort-Object -Unique | ForEach-Object { Write-Output "::error::Not covered: $_" }
    exit 1
}
