// KS Quest Converter: bakes what Kawaii Studio/KSHairRealistic shows into one texture for the Quest (Toon) material.
// Same formula as the PC shader's frag (Shaders~/KSHairRealistic.shader), the lighting replaced by a neutral environment:
//   albedo  = tex * _Color, energy-conserved against the specular colour (specular colour mode) or the metallic
//   colour  = albedo + specularTint * (0.5 * smoothness)   (the tinted reflection, which is most of the colour on PC)
//           + rim * 0.25                                    (the rim light seen on average)
//           + emission (masked exactly like the PC shader: source, invert, power, strength, bloom boost, clamp)
Shader "Hidden/KawaiiStudio/QuestConverter/KSHairRealistic"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Metallic ("Metallic", Float) = 0
        _Smoothness ("Smoothness", Float) = 0
        _UseSpecColor ("Use Specular Color", Float) = 0
        _UseEnergyConserv ("Energy Conservation", Float) = 0
        _SpecularColor ("Specular Color", Color) = (0.5,0.5,0.5,1)
        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimStrength ("Rim Strength", Float) = 0.35
        _UseEmission ("Use Emission", Float) = 0
        _UseEmissionMask ("Use Emission Mask", Float) = 1
        _EmissionMaskSource ("Mask Source", Float) = 0
        _EmissionMaskInvert ("Invert Mask", Float) = 0
        _EmissionMaskStrength ("Mask Strength", Float) = 1
        _EmissionMaskPower ("Mask Power", Float) = 1
        _EmissionMask ("Emission Mask", 2D) = "black" {}
        _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _EmissionStrength ("Emission Strength", Float) = 1
        _EmissionBloomBoost ("Bloom Boost", Float) = 1
        _EmissionClamp ("Emission Clamp", Float) = 0
        _VQT_MainTexBrightness ("Brightness", Float) = 1
        _VQT_GenerateShadow ("Generate Shadow", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            float4 _Color;
            float _Metallic, _Smoothness, _UseSpecColor, _UseEnergyConserv;
            float4 _SpecularColor, _RimColor;
            float _RimStrength;
            float _UseEmission, _UseEmissionMask, _EmissionMaskSource, _EmissionMaskInvert, _EmissionMaskStrength, _EmissionMaskPower;
            sampler2D _EmissionMask;
            float4 _EmissionColor;
            float _EmissionStrength, _EmissionBloomBoost, _EmissionClamp;
            float _VQT_MainTexBrightness;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float SpecStrength(float3 s) { return max(max(s.r, s.g), s.b); }

            float4 frag(v2f i) : SV_Target
            {
                float4 texCol = tex2D(_MainTex, i.uv) * _Color;
                float3 albedo, specTint;
                if (_UseSpecColor > 0.5)
                {
                    // specular colour mode (keyword BLOOM on PC)
                    float3 s = _SpecularColor.rgb * _Metallic;
                    albedo = texCol.rgb * (1.0 - SpecStrength(s));
                    specTint = _UseEnergyConserv > 0.5 ? s : _SpecularColor.rgb;
                }
                else
                {
                    // metalness mode
                    float3 s = texCol.rgb * _Metallic;
                    albedo = texCol.rgb * (1.0 - SpecStrength(s));
                    specTint = _UseEnergyConserv > 0.5 ? s : texCol.rgb;
                }
                float3 col = albedo + specTint * (0.5 * saturate(_Smoothness)) + _RimColor.rgb * _RimStrength * 0.25;

                if (_UseEmission > 0.5)
                {
                    float mask = 1.0;
                    if (_UseEmissionMask > 0.5)
                    {
                        if (_EmissionMaskSource < 0.5) mask = tex2D(_EmissionMask, i.uv).r;
                        else if (_EmissionMaskSource < 1.5) mask = texCol.a;
                        else mask = texCol.r;
                        mask = lerp(mask, 1.0 - mask, saturate(_EmissionMaskInvert));
                        mask = pow(saturate(mask), max(0.01, _EmissionMaskPower));
                        mask *= saturate(_EmissionMaskStrength);
                    }
                    float3 emission = _EmissionColor.rgb * (mask * _EmissionStrength) * _EmissionBloomBoost;
                    if (_EmissionClamp > 0.0) emission = min(emission, _EmissionClamp.xxx);
                    col += emission;
                }

                col *= _VQT_MainTexBrightness;
                return float4(saturate(col), texCol.a);
            }
            ENDCG
        }
    }
}
