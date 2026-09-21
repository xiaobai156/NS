$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$venv = Join-Path $root ".venv-cpu"
$venvPython = Join-Path $venv "Scripts\python.exe"
$requirements = Join-Path $root "requirements-cpu.txt"

if (-not (Test-Path -LiteralPath $venvPython)) {
    $python = Get-Command python -ErrorAction Stop
    & $python.Source -m venv $venv
}

& $venvPython -m pip install --upgrade pip
& $venvPython -m pip install --index-url "https://pypi.org/simple" -r $requirements
if ($LASTEXITCODE -ne 0) {
    throw "CPU package installation failed."
}

$cuda = & $venvPython -c "import paddle, paddleocr; print('1' if paddle.device.is_compiled_with_cuda() else '0')"
if ($LASTEXITCODE -ne 0 -or $cuda.Trim() -ne "0") {
    throw "CPU environment verification failed: a CPU-only PaddlePaddle build is required."
}
Write-Output "CPU OCR environment is ready: $venvPython"
