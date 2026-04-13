#version 450

layout(location = 0) in vec3 worldPos;
layout(location = 1) in vec3 worldNormal;
layout(location = 2) in vec2 uv;

// RT0 = worldPos.xyz | unused
// RT1 = worldNormal.xyz | specularStrength (0..1)
// RT2 = albedo.rgb | shininess / 256 (lighting.frag rescales)
layout(location = 0) out vec4 outPosition;
layout(location = 1) out vec4 outNormal;
layout(location = 2) out vec4 outAlbedo;

const float MATERIAL_SPEC_STRENGTH = 0.5;
const float MATERIAL_SHININESS = 32.0;
const float SHININESS_RANGE = 256.0;

void main() {
    outPosition = vec4(worldPos, 1.0);
    outNormal = vec4(normalize(worldNormal), MATERIAL_SPEC_STRENGTH);

    float checker = mod(floor(uv.x * 4.0) + floor(uv.y * 4.0), 2.0);
    vec3 albedo = mix(vec3(0.8, 0.8, 0.8), vec3(0.3, 0.5, 0.8), checker);
    outAlbedo = vec4(albedo, MATERIAL_SHININESS / SHININESS_RANGE);
}
