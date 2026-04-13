#version 450

layout(set = 0, binding = 0) uniform sampler2D gPosition;
layout(set = 0, binding = 1) uniform sampler2D gNormal;
layout(set = 0, binding = 2) uniform sampler2D gAlbedo;

layout(push_constant) uniform LightParams {
    vec4 cameraPos;
    vec4 lightPos;
    vec4 lightColor;   // rgb = color, a = intensity
} light;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

const float SHININESS_RANGE = 256.0;

void main() {
    vec4 normalSample = texture(gNormal, uv);
    vec4 albedoSample = texture(gAlbedo, uv);

    vec3 fragPos = texture(gPosition, uv).rgb;
    vec3 normal  = normalize(normalSample.rgb);
    vec3 albedo  = albedoSample.rgb;

    // Material params packed into the GBuffer alpha channels by gbuffer.frag.
    float specularStrength = normalSample.a;
    float shininess = albedoSample.a * SHININESS_RANGE;

    // Ambient
    vec3 ambient = 0.05 * albedo;

    // Diffuse (Lambertian)
    vec3 lightDir = normalize(light.lightPos.xyz - fragPos);
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * albedo * light.lightColor.rgb * light.lightColor.a;

    // Specular (Blinn-Phong)
    vec3 viewDir = normalize(light.cameraPos.xyz - fragPos);
    vec3 halfDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(normal, halfDir), 0.0), max(shininess, 1.0));
    vec3 specular = spec * light.lightColor.rgb * light.lightColor.a * specularStrength;

    // Attenuation
    float dist = length(light.lightPos.xyz - fragPos);
    float attenuation = 1.0 / (1.0 + 0.09 * dist + 0.032 * dist * dist);

    vec3 result = ambient + (diffuse + specular) * attenuation;
    outColor = vec4(result, 1.0);
}
