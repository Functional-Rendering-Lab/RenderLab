#version 450

layout(location = 0) in vec3 worldPos;
layout(location = 1) in vec3 worldNormal;
layout(location = 2) in vec2 uv;

layout(location = 0) out vec4 outPosition;  // RT0: world position
layout(location = 1) out vec4 outNormal;    // RT1: world normal
layout(location = 2) out vec4 outAlbedo;    // RT2: albedo

void main() {
    outPosition = vec4(worldPos, 1.0);
    outNormal = vec4(normalize(worldNormal), 0.0);
    // Checkerboard pattern as procedural albedo
    float checker = mod(floor(uv.x * 4.0) + floor(uv.y * 4.0), 2.0);
    vec3 albedo = mix(vec3(0.8, 0.8, 0.8), vec3(0.3, 0.5, 0.8), checker);
    outAlbedo = vec4(albedo, 1.0);
}
