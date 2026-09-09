# Native CUDA dependency

The Windows x64 `cuda_hash.dll` is retained byte-for-byte from the audited repository, not silently rebuilt or downloaded. Both projects now reference this explicit path instead of a temporary validation directory.

SHA-256: `481a198f6739938a485323f430dc33f728dd4a3cd410cabe19e9fbc84e72e874`

This change verifies packaging identity only. It does not claim CUDA execution, driver compatibility, model accuracy, or native ABI validation on a GPU. Keep the binary and native source changes reviewed together; a future native rebuild must use the CUDA/Visual C++ toolchain and run the CUDA-tagged tests.
