#version 450

layout(location = 0) in vec3 worldPos;
layout(location = 1) in vec3 worldNormal;
layout(location = 2) in vec2 uv;

// RT0 = worldPos.xyz | unused
// RT1 = worldNormal.xyz | specularStrength (0..1)
// RT2 = albedo.rgb | shininess / 256 (lighting.frag rescales)
// Canonical packing: RenderLab.Scene/MaterialPacking.cs (keep in sync).
layout(location = 0) out vec4 outPosition;
layout(location = 1) out vec4 outNormal;
layout(location = 2) out vec4 outAlbedo;

layout(push_constant) uniform PushConstants {
    mat4 model;
    mat4 viewProj;
    vec3 albedo;
    float specularStrength;
    float shininess;
} pc;

const float SHININESS_RANGE = 256.0;

void main() {
    outPosition = vec4(worldPos, 1.0);
    outNormal = vec4(normalize(worldNormal), pc.specularStrength);
    outAlbedo = vec4(pc.albedo, pc.shininess / SHININESS_RANGE);
}
