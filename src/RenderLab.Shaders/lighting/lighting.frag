#version 450

layout(set = 0, binding = 0) uniform sampler2D gPosition;
layout(set = 0, binding = 1) uniform sampler2D gNormal;
layout(set = 0, binding = 2) uniform sampler2D gAlbedo;

// Per-light data. Layout mirrors RenderLab.Scene.GpuLight (std430).
// Type tag drives variant: 0 = point (uses positionType.xyz), 1 = directional
// (uses directionPad.xyz, no falloff). Per-variant fields are zero-filled by
// the packer for unused cases so this shader can read both unconditionally.
struct Light {
    vec4 positionType;    // xyz = world position (point); w = type
    vec4 directionPad;    // xyz = unit direction FROM light (directional); w unused
    vec4 colorIntensity;  // rgb = color, a = intensity
};

layout(set = 1, binding = 0, std430) readonly buffer Lights {
    Light lights[];
};

layout(push_constant) uniform LightParams {
    vec4 cameraPos;
    // Four ints packed into a vec4 lane-by-lane so the whole block fits
    // under maxPushConstantSize = 128 (Vulkan minimum guarantee):
    //   x = shadingMode    (0 Lambert / 1 Phong / 2 Blinn-Phong)
    //   y = lightingOnly   (1 = drop albedo factor; ambient stays on)
    //   z = lightCount
    //   w = backgroundMode (0 solid clear, 1 ambient gradient)
    vec4 flags;
    vec4 ambientSky;      // hemispheric ambient: top hemisphere
    vec4 ambientGround;   // hemispheric ambient: bottom hemisphere
    // View basis for reconstructing a world-space ray in the background path.
    // xyz of camRight / camUp are pre-scaled by tan(fovX/2) and tan(fovY/2).
    vec4 camRight;
    vec4 camUp;
    vec4 camForward;
} pc;

#define PC_SHADING_MODE    int(pc.flags.x)
#define PC_LIGHTING_ONLY   int(pc.flags.y)
#define PC_LIGHT_COUNT     int(pc.flags.z)
#define PC_BACKGROUND_MODE int(pc.flags.w)

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

const float SHININESS_RANGE = 256.0;
const int MODE_LAMBERT = 0;
const int MODE_PHONG = 1;
const int MODE_BLINN_PHONG = 2;
const int LIGHT_POINT = 0;
const int LIGHT_DIRECTIONAL = 1;

vec3 shadeLight(Light L, vec3 fragPos, vec3 N, vec3 V,
                vec3 albedo, float specularStrength, float shininess,
                bool stripAlbedo)
{
    int  type       = int(L.positionType.w);
    vec3 lightColor = L.colorIntensity.rgb;
    float intensity = L.colorIntensity.a;

    vec3  lightDir;
    float attenuation;

    if (type == LIGHT_DIRECTIONAL) {
        // Direction in the buffer points FROM the light into the scene; the
        // shading convention wants a vector pointing from the surface TOWARD
        // the light, so negate.
        lightDir = -normalize(L.directionPad.xyz);
        attenuation = 1.0;
    } else {
        vec3 lightPos = L.positionType.xyz;
        lightDir = normalize(lightPos - fragPos);
        float dist = length(lightPos - fragPos);
        attenuation = 1.0 / (1.0 + 0.09 * dist + 0.032 * dist * dist);
    }

    float diff = max(dot(N, lightDir), 0.0);
    vec3 diffuseTerm = stripAlbedo
        ? diff * lightColor * intensity
        : diff * albedo * lightColor * intensity;

    vec3 specular = vec3(0.0);
    float shin = max(shininess, 1.0);
    float lightFacing = step(0.0, dot(N, lightDir));

    if (PC_SHADING_MODE == MODE_PHONG) {
        vec3 reflectDir = reflect(-lightDir, N);
        float norm = (shin + 2.0) / 2.0;
        float spec = norm * pow(max(dot(reflectDir, V), 0.0), shin);
        specular = lightFacing * spec * lightColor * intensity * specularStrength;
    } else if (PC_SHADING_MODE == MODE_BLINN_PHONG) {
        vec3 halfDir = normalize(lightDir + V);
        float shinBP = shin * 4.0;
        float norm = (shinBP + 8.0) / 8.0;
        float spec = norm * pow(max(dot(N, halfDir), 0.0), shinBP);
        specular = lightFacing * spec * lightColor * intensity * specularStrength;
    }

    return (diffuseTerm + specular) * attenuation;
}

void main() {
    vec4 normalSample = texture(gNormal, uv);
    vec4 albedoSample = texture(gAlbedo, uv);

    // GBuffer is cleared to 0 and geometry writes normalized normals (length ≈ 1).
    // No geometry here → background path. Solid mode discards so the render pass
    // clear color survives; ambient-gradient mode writes a hemispheric blend
    // along the view ray's world-Y axis (sky/ground horizon).
    if (length(normalSample.rgb) < 0.5) {
        if (PC_BACKGROUND_MODE == 0) {
            discard;
        }
        vec2 ndc = uv * 2.0 - 1.0;
        // Vulkan framebuffer Y points down: ndc.y = -1 is the top of the screen,
        // which should look "up" in world space, so we subtract ndc.y * camUp.
        vec3 rayDir = normalize(pc.camForward.xyz
                              + ndc.x * pc.camRight.xyz
                              - ndc.y * pc.camUp.xyz);
        float skyMixBg = rayDir.y * 0.5 + 0.5;
        vec3 bgColor = mix(pc.ambientGround.rgb, pc.ambientSky.rgb, skyMixBg);
        outColor = vec4(bgColor, 1.0);
        return;
    }

    vec3 fragPos = texture(gPosition, uv).rgb;
    vec3 N = normalize(normalSample.rgb);
    vec3 V = normalize(pc.cameraPos.xyz - fragPos);
    vec3 albedo = albedoSample.rgb;

    // Material params packed into the GBuffer alpha channels by gbuffer.frag.
    // Canonical packing: RenderLab.Scene/MaterialPacking.cs (keep in sync).
    float specularStrength = normalSample.a;
    float shininess = albedoSample.a * SHININESS_RANGE;

    bool stripAlbedo = (PC_LIGHTING_ONLY == 1);

    vec3 accum = vec3(0.0);
    for (int i = 0; i < PC_LIGHT_COUNT; i++) {
        accum += shadeLight(lights[i], fragPos, N, V, albedo,
                            specularStrength, shininess, stripAlbedo);
    }

    // Hemispheric ambient: blend ground→sky along the surface normal's up axis.
    // N.y in [-1,1] → skyMix in [0,1]. Stays on in lightingOnly mode (per the
    // existing rule); only the albedo factor drops so the toggle keeps its
    // "no surface color anywhere" meaning.
    float skyMix = N.y * 0.5 + 0.5;
    vec3 ambientColor = mix(pc.ambientGround.rgb, pc.ambientSky.rgb, skyMix);
    vec3 ambient = stripAlbedo ? ambientColor : ambientColor * albedo;

    outColor = vec4(ambient + accum, 1.0);
}
