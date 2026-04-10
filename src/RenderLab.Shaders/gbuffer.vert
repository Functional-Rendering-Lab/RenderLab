#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUV;

layout(push_constant) uniform PushConstants {
    mat4 model;
    mat4 viewProj;
} pc;

layout(location = 0) out vec3 worldPos;
layout(location = 1) out vec3 worldNormal;
layout(location = 2) out vec2 uv;

void main() {
    vec4 wp = pc.model * vec4(inPosition, 1.0);
    worldPos = wp.xyz;
    worldNormal = normalize(mat3(pc.model) * inNormal);
    uv = inUV;
    gl_Position = pc.viewProj * wp;
}
