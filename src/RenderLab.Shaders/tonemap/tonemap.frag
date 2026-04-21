#version 450

layout(set = 0, binding = 0) uniform sampler2D hdrInput;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec3 hdr = texture(hdrInput, uv).rgb;

    // Reinhard tonemap
    vec3 mapped = hdr / (hdr + vec3(1.0));

    // Gamma correction (linear -> sRGB)
    mapped = pow(mapped, vec3(1.0 / 2.2));

    outColor = vec4(mapped, 1.0);
}
