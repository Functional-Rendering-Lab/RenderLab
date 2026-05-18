#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUV;

layout(push_constant) uniform PushConstants {
    mat4 model;
} pc;

// Per-frame camera. Lives in a UBO (set=1, binding=0) instead of push
// constants so the per-draw block stays under the 128-byte
// maxPushConstantSize guaranteed by every Vulkan implementation.
layout(set = 1, binding = 0) uniform Camera {
    mat4 viewProj;
} cam;

layout(location = 0) out vec3 worldPos;
layout(location = 1) out vec3 worldNormal;
layout(location = 2) out vec2 uv;

void main() {
    vec4 wp = pc.model * vec4(inPosition, 1.0);
    worldPos = wp.xyz;
    worldNormal = normalize(mat3(pc.model) * inNormal);
    uv = inUV;
    gl_Position = cam.viewProj * wp;
}
