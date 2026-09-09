$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$venv = Join-Path $root ".venv"
$venvPython = Join-Path $venv "Scripts\python.exe"
$requirements = Join-Path $root "requirements-cuda.txt"
$checkScript = Get-ChildItem -LiteralPath $root -File -Filter "*NVIDIA-CUDA*.py" |
    Select-Object -First 1 -ExpandProperty FullName

if ([string]::IsNullOrWhiteSpace($checkScript) -or -not (Test-Path -LiteralPath $checkScript)) {
    throw "CUDA check script not found."
}

if (-not (Test-Path -LiteralPath $venvPython)) {
    $python = Get-Command python -ErrorAction Stop
    & $python.Source -m venv $venv
}

& $venvPython -m pip install --upgrade pip
& $venvPython -m pip install `
    --index-url "https://pypi.org/simple" `
    --extra-index-url "https://www.paddlepaddle.org.cn/packages/stable/cu118/" `
    -r $requirements
if ($LASTEXITCODE -ne 0) {
    throw "CUDA package installation failed."
}
& $venvPython $checkScript
if ($LASTEXITCODE -ne 0) {
    throw "CUDA environment check failed."
}
