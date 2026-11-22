# Native CUDA dependency

The Windows x64 `cuda_hash.dll` is built from `tools/CudaImage/cuda_image.cu` with CUDA 12.6 and Visual C++ from an x64 developer command prompt.

```powershell
nvcc -std=c++17 -O2 --shared --cudart=static -Xcompiler=/MT '-gencode=arch=compute_52,code=[sm_52,compute_52]' -o native/win-x64/cuda_hash.dll tools/CudaImage/cuda_image.cu
```

SHA-256 `844f88ec7ca0b83661e9f48c90f8bb841592946dac26a1d042022aab7aee1bc8`

The DLL exports `cuda_fingerprints` for a single title region and `cuda_fingerprints_batch` for several regions of one image. Run the CUDA-tagged matcher tests and compare fingerprints with the previous binary when changing the kernels or build flags.
