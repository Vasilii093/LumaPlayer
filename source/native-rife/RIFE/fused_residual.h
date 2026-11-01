#pragma once
#include "layer.h"
#include "pipeline.h"
#include "command.h"
#include "gpu.h"
#include <cstring>

// Experimental RIFE beta multiply + residual add + leaky ReLU.
// Keep both intermediate storage roundings, matching the three original layers.
class LumaResidual : public ncnn::Layer {
    ncnn::Pipeline* pipeline = nullptr;
public:
    LumaResidual() { support_vulkan=true; support_packing=true; }
    int create_pipeline(const ncnn::Option& opt) override {
        if (!vkdev || !opt.use_fp16_storage || opt.use_fp16_arithmetic || opt.use_image_storage) return -1;
        const char* shader=R"glsl(#version 450
layout(binding=0) readonly buffer A { sfpvec4 a[]; };
layout(binding=1) readonly buffer B { sfpvec4 b[]; };
layout(binding=2) readonly buffer R { sfpvec4 r[]; };
layout(binding=3) writeonly buffer O { sfpvec4 o[]; };
layout(push_constant) uniform parameter { int w; int h; int c; int acstep; int bcstep; int rcstep; int ocstep; int pack; } p;
void main() {
    int x=int(gl_GlobalInvocationID.x), y=int(gl_GlobalInvocationID.y), q=int(gl_GlobalInvocationID.z);
    if(x>=p.w || y>=p.h || q>=p.c) return;
    int xy=y*p.w+x;
    precise vec4 product=vec4(a[q*p.acstep+xy])*vec4(b[q*p.bcstep]);
    precise vec4 roundedProduct=vec4(sfpvec4(product));
    precise vec4 sum=roundedProduct+vec4(r[q*p.rcstep+xy]);
    precise vec4 roundedSum=vec4(sfpvec4(sum));
    precise vec4 activated=mix(roundedSum,roundedSum*0.2,lessThan(roundedSum,vec4(0.0)));
    o[q*p.ocstep+xy]=sfpvec4(activated);
}
)glsl";
        std::vector<uint32_t> spirv;
        if(ncnn::compile_spirv_module(shader, std::strlen(shader), opt, spirv)!=0) return -1;
        pipeline=new ncnn::Pipeline(vkdev);
        pipeline->set_optimal_local_size_xyz(8,8,1);
        return pipeline->create(spirv.data(),spirv.size()*4,std::vector<ncnn::vk_specialization_type>());
    }
    int destroy_pipeline(const ncnn::Option&) override { delete pipeline; pipeline=nullptr; return 0; }
    int forward(const std::vector<ncnn::VkMat>& in,std::vector<ncnn::VkMat>& out,ncnn::VkCompute& cmd,const ncnn::Option& opt) const override {
        if(in.size()!=3 || out.size()!=1 || !pipeline) return -1;
        const auto &a=in[0], &b=in[1], &r=in[2];
        if(a.elempack!=4 || a.dims!=3 || b.dims!=3 || r.dims!=3 || b.w!=1 || b.h!=1 || a.w!=r.w || a.h!=r.h || a.c!=r.c || a.c!=b.c || a.elempack!=b.elempack || a.elempack!=r.elempack || a.elemsize!=b.elemsize || a.elemsize!=r.elemsize || a.elemsize!=2u*a.elempack) return -1;
        out[0].create(a.w,a.h,a.c,a.elemsize,a.elempack,opt.blob_vkallocator);
        if(out[0].empty())return -100;
        std::vector<ncnn::VkMat> bindings{a,b,r,out[0]};
        std::vector<ncnn::vk_constant_type> p(8);
        p[0].i=a.w;p[1].i=a.h;p[2].i=a.c;p[3].i=a.cstep;p[4].i=b.cstep;p[5].i=r.cstep;p[6].i=out[0].cstep;p[7].i=a.elempack;
        cmd.record_pipeline(pipeline,bindings,p,out[0]);
        return 0;
    }
};
DEFINE_LAYER_CREATOR(LumaResidual)
