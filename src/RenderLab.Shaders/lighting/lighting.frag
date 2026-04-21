#version 450

layout(set = 0, binding = 0) uniform sampler2D gPosition;
layout(set = 0, binding = 1) uniform sampler2D gNormal;
layout(set = 0, binding = 2) uniform sampler2D gAlbedo;

layout(push_constant) uniform LightParams {
    vec4 cameraPos;
    vec4 lightPos;
    vec4 lightColor;   // rgb = color, a = intensity
    int shadingMode;   // 0 = Lambertian, 1 = Phong, 2 = Blinn-Phong
    int lightingOnly;  // 1 = emit unfiltered light (no albedo, no ambient)
} light;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

const float SHININESS_RANGE = 256.0;
const int MODE_LAMBERT = 0;
const int MODE_PHONG = 1;
const int MODE_BLINN_PHONG = 2;

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

    // Diffuse (Lambertian) — same for all three modes.
    vec3 lightDir = normalize(light.lightPos.xyz - fragPos);
    vec3 viewDir = normalize(light.cameraPos.xyz - fragPos);
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * albedo * light.lightColor.rgb * light.lightColor.a;

    // Specular — the one term that differs between shading models.
    // For a matched visual comparison (Wikipedia-style), Blinn-Phong is
    // evaluated with exponent 4*shininess: (N·H)^4n ≈ (R·V)^n gives a
    // similar highlight, per Blinn's 1977 paper and the Wikipedia article.
    vec3 specular = vec3(0.0);
    float shin = max(shininess, 1.0);

    // Gate specular by N·L so highlights vanish on the unlit side.
    // Without this, Blinn-Phong's half-vector and Phong's reflection can
    // still align with the view on back-facing fragments.
    float lightFacing = step(0.0, dot(normal, lightDir));

    if (light.shadingMode == MODE_PHONG) {
        vec3 reflectDir = reflect(-lightDir, normal);
        float norm = (shin + 2.0) / 2.0;
        float spec = norm * pow(max(dot(reflectDir, viewDir), 0.0), shin);
        specular = lightFacing * spec * light.lightColor.rgb * light.lightColor.a * specularStrength;
    } else if (light.shadingMode == MODE_BLINN_PHONG) {
        vec3 halfDir = normalize(lightDir + viewDir);
        float shinBP = shin * 4.0;
        float norm = (shinBP + 8.0) / 8.0;
        float spec = norm * pow(max(dot(normal, halfDir), 0.0), shinBP);
        specular = lightFacing * spec * light.lightColor.rgb * light.lightColor.a * specularStrength;
    }
    // MODE_LAMBERT falls through with specular = 0.

    // Attenuation
    float dist = length(light.lightPos.xyz - fragPos);
    float attenuation = 1.0 / (1.0 + 0.09 * dist + 0.032 * dist * dist);

    if (light.lightingOnly == 1) {
        // Strip albedo from diffuse, skip ambient — pure light response.
        vec3 diffuseNoAlbedo = diff * light.lightColor.rgb * light.lightColor.a;
        outColor = vec4((diffuseNoAlbedo + specular) * attenuation, 1.0);
        return;
    }

    vec3 result = ambient + (diffuse + specular) * attenuation;
    outColor = vec4(result, 1.0);
}
