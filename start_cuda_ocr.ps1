$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$python = $env:OCR_NVIDIA_PYTHON
if ([string]::IsNullOrWhiteSpace($python)) {
    $python = Join-Path $root ".venv\Scripts\python.exe"
}
else {
    $python = $python.Trim().Trim('"')
}
$checker = Get-ChildItem -LiteralPath $root -File -Filter "*NVIDIA-CUDA*.py" |
    Select-Object -First 1 -ExpandProperty FullName
$exe = Get-ChildItem -LiteralPath $root -Recurse -File -Filter "*NVIDIA-CUDA.exe" |
    Where-Object { $_.Directory.Name -like "*-v1.5.77" } |
    Select-Object -First 1 -ExpandProperty FullName

if (-not (Test-Path -LiteralPath $python)) {
    throw "CUDA Python environment not found: $python"
}
if ([string]::IsNullOrWhiteSpace($checker) -or -not (Test-Path -LiteralPath $checker)) {
    throw "CUDA check script not found."
}
if ([string]::IsNullOrWhiteSpace($exe) -or -not (Test-Path -LiteralPath $exe)) {
    throw "NVIDIA CUDA executable not found."
}

& $python $checker
if ($LASTEXITCODE -ne 0) {
    throw "CUDA environment check failed; OCR was not started."
}

Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
