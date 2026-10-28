// rife implemented with ncnn library

#ifndef RIFE_H
#define RIFE_H

#include <string>
#include <vector>
#include <memory>
#include <map>
#include <mutex>
#include <semaphore>

// ncnn
#include "net.h"

class RIFE
{
public:
    RIFE(int gpuid, bool tta_mode = false, bool uhd_mode = false, int num_threads = 1, bool rife_v2 = false, bool rife_v4 = false, int padding = 32);
    ~RIFE();

#if _WIN32
    int load(const std::wstring& modeldir);
#else
    int load(const std::string& modeldir);
#endif

    int process(const float* src0R, const float* src0G, const float* src0B,
                const float* src1R, const float* src1G, const float* src1B,
                float* dstR, float* dstG, float* dstB,
                const int w, const int h, const ptrdiff_t stride, const float timestep) const;

    int process_v4(const float* src0R, const float* src0G, const float* src0B,
                   const float* src1R, const float* src1G, const float* src1B,
                   float* dstR, float* dstG, float* dstB,
                   const int w, const int h, const ptrdiff_t stride, const float timestep) const;

    struct BatchOutput {
        float *r, *g, *b;
        ptrdiff_t stride;
        float timestep;
    };
    int process_v4_batch(const float* src0R,const float* src0G,const float* src0B,
                         const float* src1R,const float* src1G,const float* src1B,
                         int w,int h,ptrdiff_t stride,const std::vector<BatchOutput>& outputs) const;

    int process_v4_cached(const float* src0R,const float* src0G,const float* src0B,
                         const float* src1R,const float* src1G,const float* src1B,
                         float* dstR,float* dstG,float* dstB,int w,int h,ptrdiff_t stride,float timestep,int pairKey,std::counting_semaphore<>* slots) const;
private:
    bool memoryOptimized=false;
    mutable ncnn::PoolAllocator hostPool;
    struct PairInputs;
    mutable std::mutex inputMutex;
    mutable std::map<int,std::shared_ptr<PairInputs>> inputCache;

    ncnn::VulkanDevice* vkdev;
    ncnn::Net flownet;
    ncnn::Net contextnet;
    ncnn::Net fusionnet;
    ncnn::Pipeline* rife_preproc;
    ncnn::Pipeline* rife_postproc;
    ncnn::Pipeline* rife_flow_tta_avg;
    ncnn::Pipeline* rife_flow_tta_temporal_avg;
    ncnn::Pipeline* rife_out_tta_temporal_avg;
    ncnn::Pipeline* rife_v4_timestep;
    ncnn::Layer* rife_uhd_downscale_image;
    ncnn::Layer* rife_uhd_upscale_flow;
    ncnn::Layer* rife_uhd_double_flow;
    ncnn::Layer* rife_v2_slice_flow;
    bool tta_mode;
    bool tta_temporal_mode;
    bool uhd_mode;
    int num_threads;
    bool rife_v2;
    bool rife_v4;
    int padding;
};

#endif // RIFE_H
