Shader "Kawaii Studio/KSHairRealistic"
{
	Properties
	{
		[HDR] _Color("Tint", Color) = (1,1,1,1)
		_MainTex("Albedo", 2D) = "white" {}
		_Cutoff("Cutout", Range(0,1)) = 0.5
		_ClampCutoff("Transparency Threshold", Range(1, 0)) = 0.5
		[ToggleUI]_AlphaSharp("Sharp Transparency", Float) = 0.0

		[Header(Emission Bloom)]
		[ToggleUI]_UseEmission("Enable Emission", Float) = 0.0
		[ToggleUI]_UseEmissionMask("Use Emission Mask", Float) = 1.0
		[Enum(EmissionMask,0,MainTexAlpha,1,MainTexRed,2)] _EmissionMaskSource("Mask Source", Int) = 0
		[ToggleUI]_EmissionMaskInvert("Invert Mask", Float) = 0.0
		_EmissionMaskStrength("Mask Strength", Range(0, 1)) = 1
		_EmissionMaskPower("Mask Power", Range(0.01, 8)) = 1
		_EmissionMask("Emission Mask (R)", 2D) = "black" {}
		[HDR]_EmissionColor("Emission Color", Color) = (0,0,0,1)
		_EmissionStrength("Emission Strength", Range(0, 20)) = 1
		_EmissionBloomBoost("Bloom Boost (HDR)", Range(0, 20)) = 1
		_EmissionClamp("Emission Clamp (0 = Off)", Range(0, 50)) = 0

		[Header(Realistic Lighting)]
		[HDR]_RimColor("Rim Color", Color) = (1,1,1,1)
		_RimStrength("Rim Strength", Range(0, 2)) = 0.35
		_RimPower("Rim Power", Range(0.5, 12)) = 4
		[HDR]_BacklightColor("Backlight Color", Color) = (1,1,1,1)
		_BacklightStrength("Backlight Strength", Range(0, 2)) = 0.35
		_BacklightPower("Backlight Power", Range(0.5, 8)) = 2

		[Header(Strands Hair Lines)]
		[ToggleUI]_UseStrands("Enable Strands", Float) = 1.0
		[HDR]_StrandColor("Strand Color", Color) = (1,1,1,1)
		_StrandStrength("Strand Strength", Range(0, 5)) = 1.5
		_StrandTiling("Strand Tiling (Density)", Range(50, 5000)) = 1200
		_StrandWidth("Strand Thickness", Range(0.0001, 1.0)) = 0.5
		_StrandSoftness("Strand Smoothness", Range(0.0001, 1.0)) = 0.1
		_StrandNoise("Strand Variation", Range(0, 1)) = 0.15

		[Header(Blood Wet Layer Procedural)]
		[ToggleUI]_UseBlood("Enable Blood", Float) = 0.0
		[HDR]_BloodColor("Blood Color", Color) = (0.3, 0.0, 0.0, 1) // Darker base for realistic blood
		_BloodStrength("Blood Coverage", Range(0, 2)) = 1
		_BloodScale("Blood Scale", Range(1, 50)) = 15
		_BloodThickness("Blood Thickness (Bump)", Range(0, 5)) = 1.5
		_BloodSmoothness("Blood Smoothness", Range(0, 1)) = 0.95
		_BloodFlow("Blood Flow Speed", Range(0, 5)) = 0.2

		// Edge Glow Removed

		[Header(Tip Transparency)]
		[ToggleUI]_UseTipTransparency("Enable Tip Transparency", Float) = 0.0
		[ToggleUI]_TransparencyInvert("Invert Tip Gradient", Float) = 0.0
		_TransparencyRoot("Root Opaque (UV.y)", Range(0,1)) = 0.25
		_TransparencyCurve("Tip Gradient Curve", Range(0.1, 8)) = 2
		_TipTransparency("Tip Transparency Amount", Range(0,1)) = 0.0
		[ToggleUI]_UseTransparencyMask("Use Transparency Mask", Float) = 0.0
		_TransparencyMask("Transparency Mask (R)", 2D) = "white" {}

		[Space]
		_BumpMap("Normals", 2D) = "bump" {}
		_BumpScale("Normal Map Scale", Float) = 1
		[Header(Specular)]
		[Toggle(FINALPASS)]_UseEnergyConserv ("Use Energy Conservation", Range(0, 1)) = 0
		[Toggle(BLOOM)]_UseSpecColor ("Use Specular Color", Range(0, 1)) = 0
		_SpecularColor("Specular Color Primary", Color) = (0.5, 0.5, 0.5, 1.0)
		_SpecularColorB("Specular Color Secondary", Color) = (0.4, 0.4, 0.6, 1.0)
		_SpecularStrengthA("Specular Strength Primary", Range(0, 5)) = 1.0
		_SpecularStrengthB("Specular Strength Secondary", Range(0, 5)) = 1.0
		[Gamma] _Metallic("Metallic", Range(0, 1)) = 0
		_Smoothness("Reflectivity", Range(0, 1)) = 0
		_AnisotropyA("Anisotropy", Range(-1, 1)) = 0
		_TangentA("Tangent Shift A", Range(-1, 1)) = 0.5
		_TangentB("Tangent Shift B", Range(-1, 1)) = 0
		_GlossA("Gloss Power A", Range(0, 1)) = 0.6
		_GlossB("Gloss Power B", Range(0, 1)) = 1
		[Header(Advanced)]
		[Toggle(BLOOM_LOW)]_UseTangentTexture ("Use Tangent Shift Texture", Range(0, 1)) = 0
		_TangentShiftTex("Tangent Shift Texture", 2D) = "black" {}
		_OcclusionMap("Occlusion Map", 2D) = "white" {}
		_OcclusionScale("Occlusion Scale", Range(0,1)) = 1.0
		[Header(System)]
		[Enum(Off, 0, Front, 1, Back, 2)] _Culling ("Culling Mode", Int) = 2
		[ToggleOff(_SPECULARHIGHLIGHTS_OFF)]_SpecularHighlights ("Specular Highlights", Float) = 1.0
		[ToggleOff(_GLOSSYREFLECTIONS_OFF)]_GlossyReflections ("Glossy Reflections", Float) = 1.0
	}
	SubShader
	{
		Tags
		{
			"RenderType"="Transparent"
			"Queue"="Transparent"
			"IgnoreProjector"="True"
			"DisableBatching"="True"
		}

		Cull [_Culling]
		ZWrite On // Write depth to fix sorting issues partially
		Blend SrcAlpha OneMinusSrcAlpha // Smooth blending for fade

		CGINCLUDE
			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "AutoLight.cginc"
			#include "UnityPBSLighting.cginc"

			#pragma target 4.0
			#pragma shader_feature _ _SPECULARHIGHLIGHTS_OFF
			#pragma shader_feature _ _GLOSSYREFLECTIONS_OFF
			#pragma shader_feature _ BLOOM
			#pragma shader_feature _ BLOOM_LOW
			#pragma shader_feature _ FINALPASS

			uniform float4 _Color;
			uniform float4 _SpecularColor;
			uniform float4 _SpecularColorB;
			uniform float _SpecularStrengthA;
			uniform float _SpecularStrengthB;
			uniform float _Metallic;
			uniform float _Smoothness;
			uniform float _AnisotropyA;
			uniform float _AnisotropyB;
			uniform float _TangentA;
			uniform float _TangentB;
			uniform float _GlossA;
			uniform float _GlossB;
			uniform sampler2D _MainTex;
			uniform sampler2D _EmissionMask;
			uniform float4 _EmissionColor;
			uniform float _EmissionStrength;
			uniform float4 _RimColor;
			uniform float _RimStrength;
			uniform float _RimPower;
			uniform float4 _BacklightColor;
			uniform float _BacklightStrength;
			uniform float _BacklightPower;
			uniform float _UseStrands;
			uniform float4 _StrandColor;
			uniform float _StrandStrength;
			uniform float _StrandTiling;
			uniform float _StrandWidth;
			uniform float _StrandSoftness;
			uniform float _StrandNoise;
			uniform float _UseBlood;
			uniform float4 _BloodColor;
			uniform float _BloodStrength;
			uniform float _BloodScale;
			uniform float _BloodThickness;
			uniform float _BloodSmoothness;
			uniform float _BloodFlow;
			// uniform float _UseEdgeGlow; // Removed
			// uniform float4 _EdgeGlowColor;
			// uniform float _EdgeGlowStrength;
			// uniform float _EdgeGlowWidth;
			// uniform float _EdgeGlowNoiseScale;
			// uniform float _EdgeGlowNoiseSpeed;
			// uniform float _EdgeGlowNoiseSnap;
			uniform float _UseTipTransparency;
			uniform float _TransparencyInvert;
			uniform float _TransparencyRoot;
			uniform float _TransparencyCurve;
			uniform float _TipTransparency;
			uniform float _UseTransparencyMask;
			uniform sampler2D _TransparencyMask;
			uniform sampler2D _BumpMap;
			uniform sampler2D _TangentShiftTex;
			uniform sampler2D _OcclusionMap;
			uniform float4 _MainTex_ST;
			uniform float4 _TangentShiftTex_ST;
			uniform float _Cutoff;
			uniform float _ClampCutoff;
			uniform float _AlphaSharp;
			uniform float _BumpScale;
			uniform float _OcclusionScale;

			// Emission / Bloom
			uniform float _UseEmission;
			uniform float _UseEmissionMask;
			uniform float _EmissionMaskSource;
			uniform float _EmissionMaskInvert;
			uniform float _EmissionMaskStrength;
			uniform float _EmissionMaskPower;
			uniform float _EmissionBloomBoost;
			uniform float _EmissionClamp;

			// Workaround for ShaderLab issues with DX11 properties. Thanks, Lyuma!
			#if defined(SHADER_STAGE_VERTEX) || defined(SHADER_STAGE_FRAGMENT) || defined(SHADER_STAGE_DOMAIN) || defined(SHADER_STAGE_HULL) || defined(SHADER_STAGE_GEOMETRY)
			#define TEX2DHALF Texture2D<half4>
			#define TEXLOAD(tex, uvcoord) tex.Load(uvcoord)
			#else
			#define precise
			#define centroid
			#define TEX2DHALF float4
			#define TEXLOAD(tex, uvcoord) half4(1,0,1,1)
			#endif

			struct v2f
			{
				#ifndef UNITY_PASS_SHADOWCASTER
				float4 pos : SV_POSITION;
				float3 normal : NORMAL;
				float3 wPos : TEXCOORD0;
				SHADOW_COORDS(3)
				#else
				V2F_SHADOW_CASTER;
				#endif
				float2 uv : TEXCOORD1;
				centroid float3 tangent : TEXCOORD4_centroid;
				centroid float3 bitangent : TEXCOORD5_centroid;
			};

			struct appdata_full_c {
			    float4 vertex : POSITION;
			    centroid float4 tangent : TANGENT_centroid;
			    float3 normal : NORMAL;
			    float4 texcoord : TEXCOORD0;
			    float4 texcoord1 : TEXCOORD1;
			    float4 texcoord2 : TEXCOORD2;
			    float4 texcoord3 : TEXCOORD3;
			    centroid fixed4 color : COLOR_centroid;
			    UNITY_VERTEX_INPUT_INSTANCE_ID
			};


			v2f vert(appdata_full_c v)
			{
				v2f o = (v2f) 0;
				#ifdef UNITY_PASS_SHADOWCASTER
				TRANSFER_SHADOW_CASTER_NOPOS(o, o.pos);
				#else
				o.wPos = mul(unity_ObjectToWorld, v.vertex);
				o.pos = UnityWorldToClipPos(o.wPos);
				o.normal = UnityObjectToWorldNormal(v.normal);
				TRANSFER_SHADOW(o);
				o.tangent = UnityObjectToWorldDir(v.tangent.xyz);
			    half sign = v.tangent.w * unity_WorldTransformParams.w;
				o.bitangent = cross(o.normal, o.tangent) * sign;
				#endif
				o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
				return o;
			}

//-----------------------------------------------------------------------------
// Helper functions
//-----------------------------------------------------------------------------

// "R2" dithering
float intensity(float2 pixel) {
    const float a1 = 0.75487766624669276;
    const float a2 = 0.569840290998;
    return frac(a1 * float(pixel.x) + a2 * float(pixel.y));
}

// Interleaved Gradient Noise
float GradientNoise(float2 pixel)
{
	const float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
	return frac(magic.z * frac(dot(pixel, magic.xy)));
}

float T(float z) {
    return z >= 0.5 ? 2.-2.*z : 2.*z;
}

float3 ShiftTangent (float3 T, float3 N, float shift) 
{
	float3 shiftedT = T + shift * N;
	return normalize(shiftedT);
}

// Simple pseudo-random hash for blood noise
float2 hash22(float2 p) {
	float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return frac((p3.xx + p3.yz) * p3.zy);
}

// Procedural Liquid / Drops Noise
float BloodNoise(float2 uv, float scale, float flow) {
    float2 p = uv * scale;
    p.y += _Time.y * flow;
    
    float2 i = floor(p);
    float2 f = frac(p);
    
    float res = 0.0;
    
    for(int y = -1; y <= 1; y++) {
        for(int x = -1; x <= 1; x++) {
            float2 neighbor = float2(x, y);
            float2 pointPos = hash22(i + neighbor);
            
            pointPos = 0.5 + 0.4 * sin(_Time.y * 0.2 + 6.28 * pointPos);
            
            float2 diff = neighbor + pointPos - f;
            float dist = length(diff);
            
            res += smoothstep(0.6, 0.0, dist);
        }
    }
    return res;
}

// Generate normal from noise height
float3 BloodNormal(float2 uv, float scale, float flow, float strength) {
	float h = BloodNoise(uv, scale, flow);
	float2 d = float2(0.01, 0.0);
	float hx = BloodNoise(uv + d.xy, scale, flow);
	float hy = BloodNoise(uv + d.yx, scale, flow);
	
	float2 grad = float2(hx - h, hy - h) * strength;
	return normalize(float3(-grad.x, -grad.y, 1.0));
}

// Distort UV based on blood noise - REMOVED (Not used anymore)
// inline float2 BloodDistortUV(float2 uv) { ... }

// From "From mobile to high-end PC: Achieving high quality anime style rendering on Unity"
float StrandSpecular(float3 T, float3 V, float3 L, float3 H, float exponent, float strength)
{
	//float3 H = normalize(L+V);
	float dotTH = dot(T, H);
	float sinTH = sqrt(1.0-dotTH*dotTH)+0.001;
	float dirAtten = smoothstep(-1.0, 0.0, dotTH);
	return dirAtten * pow(sinTH, exponent) * strength;
}

struct Interpolators {
	float3 normal;
	float3 tangent;
	float3 bitangent;
};

//-----------------------------------------------------------------------------
// BRDF based on implementation in Filament.
// https://github.com/google/filament
//-----------------------------------------------------------------------------

float D_GGX_Anisotropic(float NoH, float ToH, float BoH,
		float roughness, float gloss) {

	float anisotropy = _AnisotropyA * gloss;
    // The values at and ab are perceptualRoughness^2
	float at = max(roughness * (1.0 + anisotropy), 0.002);
	float ab = max(roughness * (1.0 - anisotropy), 0.002);
    float a2 = at * ab;
    float3 d = float3(ab * ToH, at * BoH, a2 * NoH);
    d += !d * 1e-4f;
    float d2 = dot(d, d);
    float b2 = a2 / d2;
    return a2 * b2 * b2 * UNITY_INV_PI;
}

float V_SmithGGXCorrelated_Anisotropic(float at, float ab, float ToV, float BoV,
        float ToL, float BoL, float NoV, float NoL) {
    // Heitz 2014, "Understanding the Masking-Shadowing Function in Microfacet-Based BRDFs"
    float lambdaV = NoL * length(float3(at * ToV, ab * BoV, NoV));
    float lambdaL = NoV * length(float3(at * ToL, ab * BoL, NoL));
    float v = 0.5f / (lambdaV + lambdaL + 1e-7f);
    return saturate(v);
}

//-----------------------------------------------------------------------------
// Helper functions for roughness
//-----------------------------------------------------------------------------

float RoughnessToPerceptualRoughness(float roughness)
{
    return sqrt(roughness);
}

float RoughnessToPerceptualSmoothness(float roughness)
{
    return 1.0 - sqrt(roughness);
}

float PerceptualSmoothnessToRoughness(float perceptualSmoothness)
{
    return (1.0 - perceptualSmoothness) * (1.0 - perceptualSmoothness);
}

float PerceptualSmoothnessToPerceptualRoughness(float perceptualSmoothness)
{
    return (1.0 - perceptualSmoothness);
}

float PerceptualRoughnessToPerceptualSmoothness(float perceptualRoughness)
{
    return (1.0 - perceptualRoughness);
}

// Return modified perceptualSmoothness based on provided variance (get from GeometricNormalVariance + TextureNormalVariance)
float NormalFiltering(float perceptualSmoothness, float variance, float threshold)
{
    float roughness = PerceptualSmoothnessToRoughness(perceptualSmoothness);
    // Ref: Geometry into Shading - http://graphics.pixar.com/library/BumpRoughness/paper.pdf - equation (3)
    float squaredRoughness = saturate(roughness * roughness + min(2.0 * variance, threshold * threshold)); // threshold can be really low, square the value for easier control

    return RoughnessToPerceptualSmoothness(sqrt(squaredRoughness));
}

// Reference: Error Reduction and Simplification for Shading Anti-Aliasing
// Specular antialiasing for geometry-induced normal (and NDF) variations: Tokuyoshi / Kaplanyan et al.'s method.
// This is the deferred approximation, which works reasonably well so we keep it for forward too for now.
// screenSpaceVariance should be at most 0.5^2 = 0.25, as that corresponds to considering
// a gaussian pixel reconstruction kernel with a standard deviation of 0.5 of a pixel, thus 2 sigma covering the whole pixel.
float GeometricNormalVariance(float3 geometricNormalWS, float screenSpaceVariance)
{
    float3 deltaU = ddx(geometricNormalWS);
    float3 deltaV = ddy(geometricNormalWS);

    return screenSpaceVariance * (dot(deltaU, deltaU) + dot(deltaV, deltaV));
}

// Return modified perceptualSmoothness
float GeometricNormalFiltering(float perceptualSmoothness, float3 geometricNormalWS, float screenSpaceVariance, float threshold)
{
    float variance = GeometricNormalVariance(geometricNormalWS, screenSpaceVariance);
    return NormalFiltering(perceptualSmoothness, variance, threshold);
}


//-----------------------------------------------------------------------------
// BRDF
// Based on Unity's Standard BRDF 1
//-----------------------------------------------------------------------------

float2 getAnisoD (float NoH, float3 halfDir, float3 tangent, float3 normal, float roughness, float tangentShift) 
{
	half3 b; float ToH; float BoH;

    b = cross(normal + float3(0, 0, _TangentA), tangent);

    ToH = dot(tangent, halfDir);
    BoH = dot(b, halfDir);

	//float3 shiftedTangent1 = ShiftTangent(tangent, normal, 0 + _TangentA);
	float D1 = D_GGX_Anisotropic(NoH, ToH, BoH, roughness, _GlossA);

    b = cross(normal + float3(0, 0, _TangentB), tangent);

    ToH = dot(tangent, halfDir);
    BoH = dot(b, halfDir);

	//float3 shiftedTangent2 = ShiftTangent(tangent, normal, tangentShift + _TangentB);
	float D2 = D_GGX_Anisotropic(NoH, ToH, BoH, roughness, saturate(_GlossB+tangentShift));
	
	return float2(D1, D2);
}

half4 BRDF_Hair_PBS (half3 diffColor, half3 specColor, half oneMinusReflectivity, half smoothness, half tangentShift,
    Interpolators i, float3 viewDir,
    UnityLight light, UnityIndirect gi)
{
    float perceptualRoughness = SmoothnessToPerceptualRoughness (smoothness);
    float3 halfDir = Unity_SafeNormalize (float3(light.dir) + viewDir);

#define UNITY_HANDLE_CORRECTLY_NEGATIVE_NDOTV_local 0

#if UNITY_HANDLE_CORRECTLY_NEGATIVE_NDOTV_local
    // The amount we shift the normal toward the view vector is defined by the dot product.
    half shiftAmount = dot(i.normal, viewDir);
    i.normal = shiftAmount < 0.0f ? i.normal + viewDir * (-shiftAmount + 1e-5f) : i.normal;
    // A re-normalization should be applied here but as the shift is small we don't do it to save ALU.
    //normal = normalize(normal);

    half nv = saturate(dot(i.normal, viewDir)); // TODO: this saturate should no be necessary here
#else
    half nv = abs(dot(i.normal, viewDir));    // This abs allow to limit artifact
#endif

    // Classic approximation for hair scattering light with biased N.L
    float nl = saturate(lerp(.25, 1.0, dot(i.normal, light.dir)));
    float nh = saturate(dot(i.normal, halfDir));

    half lv = saturate(dot(light.dir, viewDir));
    half lh = saturate(dot(light.dir, halfDir));

    // Diffuse term
    half diffuseTerm = DisneyDiffuse(nv, nl, lh, perceptualRoughness) * nl;

    // Specular term
    float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);

    // GGX with roughness at 0 would mean no specular at all, 
    // max(roughness, 0.002) matches HDRP roughness remapping. 
    roughness = max(roughness, 0.002);

    // More accurate visibility term instead of non-anisotropic?
    // Probably not worth it.
	#if 0
	float TdotL = dot(i.tangent, light.dir);
	float BdotL = dot(i.bitangent, light.dir);
	float TdotV = dot(i.tangent, viewDir);
	float BdotV = dot(i.bitangent, light.dir);

	float V = V_SmithGGXCorrelated_Anisotropic (at, ab, TdotV, BdotV, TdotL, BdotL, nv, nl);
	#else
	float V = SmithJointGGXVisibilityTerm (nl, nv, roughness);
	#endif

    //float D = GGXTerm (nh, roughness); // Original 
    float2 D = getAnisoD(nh, halfDir, i.tangent, i.normal, roughness, tangentShift);

	// Split specular term into two colored lobes
	// specColor (input) is the tint from metallic/reflectivity.
	// We want to combine that with our custom colors.
	
	float3 specA = D.x * specColor * _SpecularColor.rgb * _SpecularStrengthA;
	float3 specB = D.y * specColor * _SpecularColorB.rgb * _SpecularStrengthB;
	
    float3 specularTerm = V * (specA + specB) * UNITY_PI; // Torrance-Sparrow model, Fresnel is applied later

#   ifdef UNITY_COLORSPACE_GAMMA
        specularTerm = sqrt(max(1e-4h, specularTerm));
#   endif

    // specularTerm * nl can be NaN on Metal in some cases, use max() to make sure it's a sane value
    // Setting this to zero doesn't work. ???
    specularTerm = max(1e-4h, specularTerm * nl);
#if defined(_SPECULARHIGHLIGHTS_OFF)
    specularTerm = 0.0;
#endif

    // surfaceReduction = Int D(NdotH) * NdotH * Id(NdotL>0) dH = 1/(roughness^2+1)
    half surfaceReduction;
#   ifdef UNITY_COLORSPACE_GAMMA
        surfaceReduction = 1.0-0.28*roughness*perceptualRoughness;      // 1-0.28*x^3 as approximation for (1/(x^4+1))^(1/2.2) on the domain [0;1]
#   else
        surfaceReduction = 1.0 / (roughness*roughness + 1.0);           // fade \in [0.5;1]
#   endif

    // To provide true Lambert lighting, we need to be able to kill specular completely.
    //specularTerm *= any(specColor) ? 1.0 : 0.0; // Disabled to allow custom spec colors even if base spec is low

    half grazingTerm = saturate(smoothness + (1-oneMinusReflectivity));
    half3 color =   
    // This is wrong, but it doesn't look too bad.
    // Remove diffuse light if it's guesstimated.
    				diffColor * (gi.diffuse + (light.color * (!any(_WorldSpaceLightPos0.xyz))) * diffuseTerm)
                    + specularTerm * light.color // FresnelTerm handled in specA/B mixing logic above partially, or applied here
                    + surfaceReduction * gi.specular * FresnelLerp (specColor, grazingTerm, nv);
    return half4(color, 1);
}

// Interleaved Gradient Noise (IGN) - High Quality Dither
inline float IGN(float2 p) {
    float3 v = float3(p.xy, 0);
    return frac(52.9829189 * frac(dot(v.xy, float2(0.06711056, 0.00583715))));
}

inline void applyAlphaClip(inout float alpha, float cutoff, float2 pos, bool sharpen)
{
	// Fade mode: no clip, just alpha modification
	// If you want to force opacity at root, ensure alpha is 1.0 there
	// The fading is handled by the blend mode now
}

inline float ApplyTipTransparency(float alpha, float2 uv)
{
	if (_UseTipTransparency < 0.5)
		return alpha;

	// NOTE: This assumes hair UV.y goes from root->tip. If your UVs are flipped, enable "Invert Tip Gradient".
	float v = lerp(uv.y, 1.0 - uv.y, saturate(_TransparencyInvert));
	float grad = saturate((v - _TransparencyRoot) / max(1e-5, (1.0 - _TransparencyRoot)));
	grad = pow(grad, _TransparencyCurve);

	float mask = tex2D(_TransparencyMask, uv).r;
	float tipFactor = lerp(grad, mask, saturate(_UseTransparencyMask));

	// Reduce alpha only where tipFactor > 0 (keeps roots opaque -> avoids seeing inside the head).
	alpha *= lerp(1.0, (1.0 - _TipTransparency), saturate(tipFactor));
	return alpha;
}

inline float StrandLines(float2 uv)
{
	if (_UseStrands < 0.5)
		return 0;

	float2 uvs = uv;
	// uvs.y += _Time.y * _StrandScroll; // Removed

	// High density tiling
	float u = uvs.x * _StrandTiling;
	
	// Pure sine wave for consistent, evenly spaced lines
	// Remapped to 0-1 sharp lines
	float strand = 0.5 + 0.5 * sin(u * 6.28318);
	
	// Make them thin and sharp
	// Power curve to control thickness
	float sharpStrand = pow(strand, 1.0 / max(0.001, _StrandWidth));
	
	// Smooth edges slightly to avoid aliasing
	return smoothstep(0.5, 0.5 + _StrandSoftness, sharpStrand);
}

inline float EdgeBand(float alpha, float cutoff, float width)
{
	// High near alpha==cutoff, fades away inside/outside.
	float d = abs(alpha - cutoff);
	return 1.0 - smoothstep(0.0, max(1e-5, width), d);
}

// EdgeGlowMask Removed
// inline float EdgeGlowMask(...) { ... }

		#ifndef UNITY_PASS_SHADOWCASTER
		float4 frag(v2f i, uint facing : SV_IsFrontFace) : SV_TARGET
		{
    		half3 normalTangent = UnpackScaleNormal(tex2D (_BumpMap, i.uv), _BumpScale);
		    // Thanks, Xiexe!
		    half3 tspace0 = half3(i.tangent.x, i.bitangent.x, i.normal.x);
		    half3 tspace1 = half3(i.tangent.y, i.bitangent.y, i.normal.y);
		    half3 tspace2 = half3(i.tangent.z, i.bitangent.z, i.normal.z);

		    half3 calcedNormal;
		    calcedNormal.x = dot(tspace0, normalTangent);
		    calcedNormal.y = dot(tspace1, normalTangent);
		    calcedNormal.z = dot(tspace2, normalTangent);
		    
		    float3 normal = normalize(calcedNormal);
		    half3 bumpedTangent = (cross(i.bitangent, calcedNormal));
		    half3 bumpedBitangent = (cross(calcedNormal, bumpedTangent));

		    // Flip normals not facing the camera already, but this is bad for hair...
			//normal.z *= facing? 1 : -1; 
			// Base UV: keep silhouette stable (avoid scalp showing through)
			float2 baseUV = i.uv;
			// float2 bloodUV = (_UseBlood > 0.5) ? BloodDistortUV(baseUV) : baseUV; // REMOVED

			float4 texCol = tex2D(_MainTex, baseUV) * _Color;
			// float4 texColBlood = tex2D(_MainTex, bloodUV) * _Color; // REMOVED
			float occlusion = LerpOneTo(tex2D(_OcclusionMap, baseUV).g, _OcclusionScale);

			float alpha = texCol.a;
			float alphaRaw = alpha;

			alpha = ApplyTipTransparency(alpha, baseUV);
			applyAlphaClip(alpha, _Cutoff, i.pos.xy, _AlphaSharp);

			float2 uv = baseUV;

			UNITY_LIGHT_ATTENUATION(attenuation, i, i.wPos.xyz);

			float3 specularTint;
			float oneMinusReflectivity;
			float smoothness = _Smoothness;
			smoothness = GeometricNormalFiltering(smoothness, normal, 0.5, 0.25);

			#if !defined(BLOOM) // Metalness mode
				oneMinusReflectivity = OneMinusReflectivityFromMetallic(_Metallic);
				float3 albedo = DiffuseAndSpecularFromMetallic(
					texCol, _Metallic, specularTint, oneMinusReflectivity
				);
			#else  // Specular colour mode
				oneMinusReflectivity = 1 - SpecularStrength(_SpecularColor); 
				float3 albedo = texCol;
			#endif

			// ------------------------------------------------------------
			// Blood layer (Volumetric Liquid)
			// ------------------------------------------------------------
			float bloodMask = 0.0;
			float3 bloodNorm = float3(0,0,1);
			
			if (_UseBlood > 0.5)
			{
				float bHeight = BloodNoise(baseUV, _BloodScale, _BloodFlow);
				
				// Threshold to create defined drops vs background
				bloodMask = smoothstep(0.4, 0.6, bHeight * _BloodStrength);
				
				if (bloodMask > 0.01) {
					// Calculate liquid normal for specular
					bloodNorm = BloodNormal(baseUV, _BloodScale, _BloodFlow, _BloodThickness * 10.0);
					
					// Blend normal: if blood is present, override hair normal with liquid normal
					normal = normalize(lerp(normal, bloodNorm, bloodMask));
					
					// Deep liquid color (Beer's law fake): darker in the middle (thicker)
					// Use the color directly but allow it to be dark/deep
					// float3 bloodColor = _BloodColor.rgb; // MOVED DOWN
					
					// Apply color (lerp heavily to override hair color)
					// albedo = lerp(albedo, bloodColor, bloodMask * 0.95); // MOVED DOWN
					
					// Liquid is super smooth and specular
					smoothness = lerp(smoothness, _BloodSmoothness, bloodMask);
					// specularTint and reflectivity handled after energy conservation now
				}
			}

			#if !defined(BLOOM) // Metalness mode
				albedo = EnergyConservationBetweenDiffuseAndSpecular(
				texCol, texCol*_Metallic, oneMinusReflectivity);
				#if defined(FINALPASS) // "Energy convervation"
				specularTint = texCol*_Metallic;
				#else
				specularTint = texCol;
				#endif
			#else  // Specular colour mode
				albedo = EnergyConservationBetweenDiffuseAndSpecular(
				texCol, _SpecularColor*_Metallic, oneMinusReflectivity);
				#if defined(FINALPASS) // "Energy convervation"
				specularTint = _SpecularColor*_Metallic;
				#else
				specularTint = _SpecularColor;
				#endif
			#endif

			// ------------------------------------------------------------
			// BLOOD COLOR OVERRIDE (Fix HUE issue)
			// Apply blood color AFTER energy conservation to ensure it looks exactly as picked
			// ------------------------------------------------------------
			if (bloodMask > 0.01)
			{
				// Override albedo with pure blood color
				albedo = lerp(albedo, _BloodColor.rgb, bloodMask);
				
				// Force non-metallic specs for blood (white/glossy reflections)
				// This prevents the hair color from tinting the blood specular
				specularTint = lerp(specularTint, float3(0.04, 0.04, 0.04), bloodMask); 
			}

			float3 viewDir = normalize(_WorldSpaceCameraPos - i.wPos);
			UnityLight light;
			light.color = attenuation * _LightColor0.rgb;
			light.dir = Unity_SafeNormalize(UnityWorldSpaceLightDir(i.wPos));

			// Direction may be wrong here, but there doesn't seem to be a better alternative
			float3 anisotropicT = normalize(UnityObjectToWorldDir(float3(1, 0, 0)));
			float3 anisotropicB = normalize(cross(i.normal, anisotropicT));

			#if !defined(BLOOM_LOW) // Use shift texture
			float tangentShift = dot(0.2 + texCol.rgb - tex2Dlod(_MainTex, float4(i.uv, 0, 7)).rgb , 0.5);
			#else
			float tangentShift = tex2D(_TangentShiftTex, i.uv * _TangentShiftTex_ST.xy + _TangentShiftTex_ST.zw);
			#endif

			UnityIndirect indirectLight;
			#ifdef UNITY_PASS_FORWARDADD
			indirectLight.diffuse = indirectLight.specular = 0;
			#else
			indirectLight.diffuse = max(0, ShadeSH9(float4(normal, 1)));

			float3  anisotropyDirection = _AnisotropyA >= 0.0 ? anisotropicB : anisotropicT;
			float3  anisotropicTangent  = cross(anisotropyDirection, viewDir);
			float3  anisotropicNormal   = cross(anisotropicTangent, anisotropyDirection);
			float bendFactor          = abs(_AnisotropyA) * saturate(5.0 * SmoothnessToPerceptualRoughness(smoothness));
			float3  bentNormal          = normalize(lerp(i.normal, anisotropicNormal, bendFactor));

			float3 reflectionDir = reflect(-viewDir, bentNormal);

			Unity_GlossyEnvironmentData envData;
			envData.roughness = 1 - smoothness;
			envData.reflUVW = reflectionDir;
			    #ifdef _GLOSSYREFLECTIONS_OFF
			        indirectLight.specular = unity_IndirectSpecColor.rgb;
			    #else
				indirectLight.specular = Unity_GlossyEnvironment(
					UNITY_PASS_TEXCUBE(unity_SpecCube0), unity_SpecCube0_HDR, envData
				);
				#endif
			#endif

			indirectLight.specular *= occlusion;

			#ifdef UNITY_PASS_FORWARDBASE
			// Guesstimate a light direction/color if none exists for specular highlights
			if (!any(_WorldSpaceLightPos0.xyz)) {
				// unity_IndirectSpecColor is derived from the skybox, which doesn't always make sense.
				//light.color = indirectLight.diffuse;
				light.dir = Unity_SafeNormalize(light.dir + unity_SHAr.xyz + unity_SHAg.xyz + unity_SHAb.xyz);
    			light.color = ShadeSH9(half4(light.dir, 1.0));
			}
			#endif

			// Workaround an issue with corrected NdotV where backfaces are megaflares.
			light.color *=  (facing? 1 : 0);

			Interpolators iii = (Interpolators)0;
			iii.normal = normal;
			iii.tangent = normalize(i.tangent);
			iii.bitangent = normalize(i.bitangent);
			float3 col = BRDF_Hair_PBS(
				albedo, specularTint,
				oneMinusReflectivity, smoothness, tangentShift,
				iii, viewDir,
				light, indirectLight
			);

			// ------------------------------------------------------------
			// "Realistic hair" helpers: rim + backlight (cheap & stable)
			// ------------------------------------------------------------
			#ifndef UNITY_PASS_FORWARDADD
			float NoV = saturate(dot(normal, viewDir));
			float rim = pow(1.0 - NoV, _RimPower) * _RimStrength;
			col += rim * _RimColor.rgb;

			// Backlight when light is behind the hair (fake transmission)
			float back = pow(saturate(dot(-light.dir, normal)), _BacklightPower) * _BacklightStrength;
			col += back * _BacklightColor.rgb * albedo;

			// Strands: subtle additive micro-lines (reads like separated hair strands)
			float strands = StrandLines(baseUV);
			// Apply color tint to strands, but also boost their brightness
			col = lerp(col, col * (1.0 + strands * _StrandStrength), 0.8); // Blend mode: Overlay-ish
			// Add specular boost on strands
			col += strands * _StrandColor.rgb * _StrandStrength * 0.3 * smoothness;

			// Edge glow: REMOVED
			// float eg = EdgeGlowMask(baseUV, i.pos.xy, alphaRaw) * _EdgeGlowStrength;
			// col += eg * _EdgeGlowColor.rgb;
			#endif

			#ifndef UNITY_PASS_FORWARDADD
			if (_UseEmission > 0.5)
			{
				float emissionMask = 1.0;
				if (_UseEmissionMask > 0.5)
				{
					// Mask Source:
					// 0 = _EmissionMask (R)
					// 1 = _MainTex Alpha (before tip transparency)
					// 2 = _MainTex Red
					if (_EmissionMaskSource < 0.5)
						emissionMask = tex2D(_EmissionMask, i.uv).r;
					else if (_EmissionMaskSource < 1.5)
						emissionMask = alphaRaw;
					else
						emissionMask = texCol.r;

					emissionMask = lerp(emissionMask, 1.0 - emissionMask, saturate(_EmissionMaskInvert));
					emissionMask = pow(saturate(emissionMask), max(0.01, _EmissionMaskPower));
					emissionMask *= saturate(_EmissionMaskStrength);
				}

				float3 emission = _EmissionColor.rgb * (emissionMask * _EmissionStrength);

				// "Bloom" dans Unity/VRChat = pixels HDR très lumineux -> post-process Bloom.
				// On boost donc l'émission en HDR, puis on peut la clamp pour éviter de cramer.
				emission *= _EmissionBloomBoost;
				if (_EmissionClamp > 0.0)
					emission = min(emission, float3(_EmissionClamp, _EmissionClamp, _EmissionClamp));

				col += emission;
			}
			#endif

			#ifdef UNITY_PASS_FORWARDADD
			return float4(col, 0);
			#else
			return float4(col, alpha);
			#endif
		}
		#else
		float4 frag(v2f i) : SV_Target
		{
			float alpha = _Color.a;
			if (_Color.a > 0)
				alpha *= tex2D(_MainTex, i.uv).a;

			alpha = ApplyTipTransparency(alpha, i.uv);
			applyAlphaClip(alpha, _Cutoff, i.pos.xy, _AlphaSharp);

			SHADOW_CASTER_FRAGMENT(i)
		}
		#endif
		ENDCG

		Pass
		{
			Tags { "LightMode" = "ForwardBase" }
            // AlphaToMask Off - We use Blend now
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fwdbase_fullshadows
			#pragma multi_compile UNITY_PASS_FORWARDBASE
			ENDCG
		}

		Pass
		{
			Tags { "LightMode" = "ForwardAdd" }
			Blend One One
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fwdadd_fullshadows
			#pragma multi_compile UNITY_PASS_FORWARDADD
			ENDCG
		}

		Pass
		{
			Tags { "LightMode" = "ShadowCaster" }
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_shadowcaster
			#pragma multi_compile UNITY_PASS_SHADOWCASTER
			ENDCG
		}
	}
	Fallback "Standard"
	// GUI Custom Editor Link
	CustomEditor "KawaiiStudio.Shaders.Editor.KSHairRealisticGUI"
}