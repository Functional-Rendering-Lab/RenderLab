#version 450

layout(set = 0, binding = 0) uniform sampler2D inputTexture;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec3 color = texture(inputTexture, uv).rgb;
    float gray = dot(color, vec3(0.2126, 0.7152, 0.0722));
    outColor = vec4(vec3(gray), 1.0);
}
