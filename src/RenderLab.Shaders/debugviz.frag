#version 450

layout(set = 0, binding = 0) uniform sampler2D inputTex;

layout(push_constant) uniform PC {
    int mode; // 0 = rgb passthrough, 1 = depth
    float nearPlane;
    float farPlane;
};

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

float linearizeDepth(float d) {
    return nearPlane * farPlane / (farPlane - d * (farPlane - nearPlane));
}

void main() {
    vec4 val = texture(inputTex, uv);

    if (mode == 1) {
        float linear = linearizeDepth(val.r);
        // Log scale: maps [near, far] to [0, 1] with detail near the camera
        float norm = log(linear / nearPlane) / log(farPlane / nearPlane);
        // Near = white, far = black
        outColor = vec4(vec3(1.0 - norm), 1.0);
    } else {
        outColor = vec4(val.rgb, 1.0);
    }
}
