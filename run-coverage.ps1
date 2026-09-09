param(
    [double]$MinimumLineCoverage = 80.0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$invariantCulture = [Globalization.CultureInfo]::InvariantCulture

if ([double]::IsNaN($MinimumLineCoverage) -or
    [double]::IsInfinity($MinimumLineCoverage) -or
    $MinimumLineCoverage -lt 0.0 -or
    $MinimumLineCoverage -gt 100.0) {
    Write-Error 'MinimumLineCoverage must be between 0 and 100.'
    exit 1
}

$projectRoot = $PSScriptRoot
$testProject = Join-Path $projectRoot 'OcrLineTool.Tests\OcrLineTool.Tests.csproj'
$runSettings = Join-Path $projectRoot 'coverage.runsettings'
$coverageDirectory = Join-Path $projectRoot 'artifacts\coverage'

if (-not (Test-Path -LiteralPath $testProject -PathType Leaf)) {
    Write-Error "Test project not found: $testProject"
    exit 1
}

if (-not (Test-Path -LiteralPath $runSettings -PathType Leaf)) {
    Write-Error "Runsettings file not found: $runSettings"
    exit 1
}

if (Test-Path -LiteralPath $coverageDirectory) {
    Remove-Item -LiteralPath $coverageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $coverageDirectory -Force | Out-Null

$testArguments = @(
    'test'
    $testProject
    '-c'
    'Debug'
    '--no-restore'
    '--nologo'
    '--collect:XPlat Code Coverage'
    '--settings'
    $runSettings
    '--results-directory'
    $coverageDirectory
)

try {
    & dotnet @testArguments
    $testExitCode = $LASTEXITCODE
}
catch {
    Write-Output 'Tests: FAIL'
    Write-Output 'Coverage gate aborted.'
    Write-Error $_
    exit 1
}

if ($testExitCode -ne 0) {
    Write-Output 'Tests: FAIL'
    Write-Output 'Coverage gate aborted.'
    exit 1
}

$coverageReports = @(Get-ChildItem -LiteralPath $coverageDirectory -Recurse -File -Filter 'coverage.cobertura.xml')
if ($coverageReports.Count -eq 0) {
    Write-Error "Cobertura report not found under $coverageDirectory"
    exit 1
}

if ($coverageReports.Count -ne 1) {
    Write-Error "Expected exactly one Cobertura report under $coverageDirectory, found $($coverageReports.Count)."
    exit 1
}

try {
    [xml]$coverageDocument = Get-Content -LiteralPath $coverageReports[0].FullName -Raw
    $coverageNode = $coverageDocument.SelectSingleNode('/coverage')
    if ($null -eq $coverageNode) {
        throw 'Cobertura root node /coverage is missing.'
    }

    $lineRateText = $coverageNode.GetAttribute('line-rate')
    if ([string]::IsNullOrWhiteSpace($lineRateText)) {
        throw 'Cobertura root attribute line-rate is missing.'
    }

    $lineRate = [double]::Parse($lineRateText, $invariantCulture)
    if ($lineRate -lt 0.0 -or $lineRate -gt 1.0) {
        throw "Cobertura line-rate must be between 0 and 1, got $lineRateText."
    }
}
catch {
    Write-Error "Unable to read Cobertura report: $($_.Exception.Message)"
    exit 1
}

$lineCoverage = $lineRate * 100.0
$lineCoverageText = $lineCoverage.ToString('0.00', $invariantCulture)
$minimumCoverageText = $MinimumLineCoverage.ToString('0.00', $invariantCulture)

Write-Output 'Tests: PASS'
Write-Output "Line coverage: $lineCoverageText%"
Write-Output "Required: $minimumCoverageText%"

if ($lineCoverage -lt $MinimumLineCoverage) {
    Write-Output 'Coverage gate: FAIL'
    exit 1
}

Write-Output 'Coverage gate: PASS'
exit 0
