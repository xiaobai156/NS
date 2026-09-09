$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root "start_cuda_ocr.ps1")
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
