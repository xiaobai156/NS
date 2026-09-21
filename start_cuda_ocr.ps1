$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
# Keep this script ASCII for Windows PowerShell 5.1 without a UTF-8 BOM.
$release = Join-Path $root (([char]0x53D1).ToString() + [char]0x5E03)
$exe = Get-ChildItem -LiteralPath $release -File -Filter "*NVIDIA-CUDA.exe" |
    Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace($exe)) {
    $exe = Get-ChildItem -LiteralPath $release -Directory |
        Where-Object { $_.Name -match '-v\d+\.\d+\.\d+(\.\d+)?$' } |
        Sort-Object { [version]($_.Name -replace '^.*-v', '') } -Descending |
        ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -File -Filter "*NVIDIA-CUDA.exe" } |
        Select-Object -First 1 -ExpandProperty FullName
}
if ([string]::IsNullOrWhiteSpace($exe) -or -not (Test-Path -LiteralPath $exe)) {
    throw "NVIDIA CUDA executable not found."
}

# Device checks belong to recognition, so Settings remains reachable without a GPU.
Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
