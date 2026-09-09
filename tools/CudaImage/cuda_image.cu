#include <cuda_runtime.h>
#include <stdint.h>
#include <math.h>

// Separable antialiased resize. Host memory contains decoded, packed BGR bytes.
__global__ void horizontal(const unsigned char* src, int w, int h, float* tmp) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= h * 65 * 3) return;
    int c = i % 3, x = (i / 3) % 65, y = i / (65 * 3);
    double scale = (double)w / 65, radius = fmax(1., scale);
    double center = (x + .5) * scale - .5, sum = 0, weight = 0;
    for (int sx = (int)ceil(center - radius); sx <= (int)floor(center + radius); ++sx) {
        double a = fmax(0., 1. - fabs(sx - center) / radius);
        int ix = max(0, min(w - 1, sx));
        sum += a * src[(y * w + ix) * 3 + c]; weight += a;
    }
    tmp[i] = (float)(sum / weight);
}

__global__ void vertical(const float* tmp, int w, int h, double topRatio,
    double bottomRatio, const double* shifts, int count, unsigned char* reduced) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= count * 65 * 16 * 3) return;
    int c = i % 3, x = (i / 3) % 65, y = (i / (3 * 65)) % 16;
    int s = i / (3 * 65 * 16);
    int top = max(0, min(h - 1, __double2int_rn(w * (topRatio + shifts[s]))));
    int bottom = max(top + 1, min(h, __double2int_rn(w * (bottomRatio + shifts[s]))));
    double scale = (bottom - top) / 16., radius = fmax(1., scale);
    double center = top + (y + .5) * scale - .5, sum = 0, weight = 0;
    for (int sy = (int)ceil(center - radius); sy <= (int)floor(center + radius); ++sy) {
        double a = fmax(0., 1. - fabs(sy - center) / radius);
        int iy = max(0, min(h - 1, sy));
        sum += a * tmp[(iy * 65 + x) * 3 + c]; weight += a;
    }
    reduced[i] = (unsigned char)max(0, min(255, (int)floor(sum / weight + .5)));
}

__global__ void hash_rows(const unsigned char* pixels, int count, uint64_t* hashes) {
    int row = blockIdx.x * blockDim.x + threadIdx.x;
    if (row >= count * 16) return;
    const unsigned char* p = pixels + row * 65 * 3;
    uint64_t bits = 0;
    for (int x = 0; x < 64; ++x) {
        int a = (114*p[3*x]+587*p[3*x+1]+299*p[3*x+2])/1000;
        int b = (114*p[3*x+3]+587*p[3*x+4]+299*p[3*x+5])/1000;
        if (a <= b) bits |= 1ULL << x;
    }
    hashes[row] = bits;
}

extern "C" __declspec(dllexport) int cuda_fingerprints(const unsigned char* src, int w, int h,
    double top, double bottom, const double* shifts, int count, uint64_t* result) {
    if (!src || !shifts || !result || w < 1 || h < 1 || count < 1 || count > 241) return -1;
    unsigned char *image=nullptr, *pixels=nullptr;
    float* tmp=nullptr; double* ds=nullptr; uint64_t* hashes=nullptr;
    cudaError_t error=cudaSuccess;
    #define RUN(call) if ((error=(call)) != cudaSuccess) goto cleanup
    RUN(cudaMalloc(&image,(size_t)w*h*3));
    RUN(cudaMalloc(&tmp,(size_t)h*65*3*sizeof(float)));
    RUN(cudaMalloc(&ds,(size_t)count*sizeof(double)));
    RUN(cudaMalloc(&pixels,(size_t)count*65*16*3));
    RUN(cudaMalloc(&hashes,(size_t)count*16*sizeof(uint64_t)));
    RUN(cudaMemcpy(image,src,(size_t)w*h*3,cudaMemcpyHostToDevice));
    RUN(cudaMemcpy(ds,shifts,(size_t)count*sizeof(double),cudaMemcpyHostToDevice));
    horizontal<<<(h*65*3+255)/256,256>>>(image,w,h,tmp);
    RUN(cudaGetLastError());
    vertical<<<(count*65*16*3+255)/256,256>>>(tmp,w,h,top,bottom,ds,count,pixels);
    RUN(cudaGetLastError());
    hash_rows<<<(count*16+255)/256,256>>>(pixels,count,hashes);
    RUN(cudaGetLastError());
    RUN(cudaMemcpy(result,hashes,(size_t)count*16*sizeof(uint64_t),cudaMemcpyDeviceToHost));
cleanup:
    cudaFree(image); cudaFree(tmp); cudaFree(ds); cudaFree(pixels); cudaFree(hashes);
    return (int)error;
}
