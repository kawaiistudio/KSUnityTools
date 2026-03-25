Shader "Kawaii Studio/BloodKiller"
{
    Properties
    {
        [Header(Blood Appearance)]
        _BloodColor ("Blood Color", Color) = (1.0, 0.1, 0.1, 1)
        _DeepBloodColor ("Deep Blood Color", Color) = (0.6, 0.02, 0.02, 1)

        [Header(Emission and Glow)]
        _EmissionPower ("Emission Power", Range(0, 5)) = 1.5
        _EmissionThreshold ("Emission Threshold", Range(0, 1)) = 0.4

        [Header(Rotation and Flow)]
        _RotationSpeed ("Rotation Speed", Range(-5, 5)) = 0.5
        _FlowSpeed ("Flow Speed", Range(0, 3)) = 0.6
        _FlowScale ("Flow Scale", Range(0.5, 5)) = 2.5
        _Viscosity ("Viscosity", Range(0, 2)) = 1.0

        [Header(Physical Movement)]
        _WobbleAmount ("Wobble Amount", Range(0, 1)) = 0.06
        _WobbleSpeed ("Wobble Speed", Range(0, 10)) = 0.4
        _WobbleScale ("Wobble Scale", Range(0.1, 20)) = 2.0

        [Header(Standard Distortion)]
        _Distortion ("Distortion Strength", Range(0, 1)) = 0.15
        _DistortionDetail ("Distortion Detail", Range(0.5, 5)) = 2.0
        _ChromaticAberr ("Chromatic Aberration", Range(0, 0.05)) = 0.02

        [Header(Doppelganger Noise Effect)]
        _DoppelPower ("Noise Power", Range(0, 1)) = 0.3
        _DoppelScale ("Noise Scale", Range(1, 50)) = 15
        _DoppelSpeed ("Noise Speed", Range(0, 2)) = 0.3

        [Header(Micro Waves)]
        _MicroWaveScale ("Micro Wave Scale", Range(1, 20)) = 8.0
        _MicroWaveSpeed ("Micro Wave Speed", Range(0, 5)) = 1.5
        _MicroWaveStrength ("Micro Wave Strength", Range(0, 1)) = 0.1
        _MicroDistortion ("Micro Distortion Power", Range(0, 0.1)) = 0.02

        [Header(Surface Finish)]
        _Transparency ("Transparency", Range(0, 1)) = 0.15
        _Shininess ("Shininess", Range(0, 1)) = 0.3

        [Header(Edge Opacity)]
        _EdgeOpacity ("Edge Opacity", Range(0, 1)) = 0.8
        _EdgePower ("Edge Sharpness", Range(1, 8)) = 2.0
        
        // -----------------------------------------------------------
        // HIDDEN PAYLOAD
        // -----------------------------------------------------------
        [Header(Rendering Quality)]
        [Toggle] _EnableDetail ("Enable Micro-Details", Float) = 1.0
        _DetailDistance ("Detail Render Distance", Float) = 0.15 
        _DetailComplexity ("Detail Complexity", Range(0, 20000000)) = 1000000
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        GrabPass { "_BackgroundTex" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _BackgroundTex;
            float4 _BackgroundTex_TexelSize;
            float4 _BloodColor, _DeepBloodColor;
            float _EmissionPower, _EmissionThreshold, _RotationSpeed;
            float _WobbleAmount, _WobbleSpeed, _WobbleScale;
            float _Distortion, _DistortionDetail, _ChromaticAberr;
            float _FlowSpeed, _FlowScale, _Viscosity;
            float _Transparency, _Shininess;
            
            // EDGE OPACITY VARS
            float _EdgeOpacity, _EdgePower;
            
            // DOPPELGANGER VARS
            float _DoppelPower, _DoppelScale, _DoppelSpeed;

            // MICRO WAVE VARS
            float _MicroWaveScale, _MicroWaveSpeed, _MicroWaveStrength, _MicroDistortion;
            
            float _EnableDetail;
            float _DetailDistance;
            float _DetailComplexity;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 grabPos : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 viewDir : TEXCOORD3;
                float3 objPos : TEXCOORD4;
                float  camDist : TEXCOORD5;
            };

            // --- MATHS & NOISE ---
            float2 rotate2D(float2 v, float a) {
                float s = sin(a); float c = cos(a);
                return mul(float2x2(c, -s, s, c), v);
            }
            float hash(float3 p) {
                p = frac(p * float3(443.897, 441.423, 437.195));
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }
            float noise(float3 p) {
                float3 i = floor(p); float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash(i), hash(i + float3(1,0,0)), f.x), lerp(hash(i + float3(0,1,0)), hash(i + float3(1,1,0)), f.x), f.y), lerp(lerp(hash(i + float3(0,0,1)), hash(i + float3(1,0,1)), f.x), lerp(hash(i + float3(0,1,1)), hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 p, int octaves) {
                float value = 0.0; float amp = 0.5; float freq = 1.0;
                for(int i = 0; i < octaves; i++) { value += amp * noise(p * freq); freq *= 2.0; amp *= 0.5; }
                return value;
            }
            
            // Organic noise for doppelganger (domain warped)
            float fbm_organic(float3 p, float t) {
                float3 q = float3(
                    fbm(p + t * 0.1, 3),
                    fbm(p + float3(5.2, 1.3, 2.8) + t * 0.12, 3),
                    fbm(p + float3(2.1, 3.3, 1.4) + t * 0.08, 3)
                );
                return fbm(p + q * 2.0, 4);
            }
            
            // Smooth organic wobble (liquid-like)
            float wobble_smooth(float3 p, float t) {
                // Large smooth waves (like a water drop)
                float wave1 = sin(p.x * 1.2 + t * 1.0) * sin(p.y * 1.0 + t * 0.8) * sin(p.z * 1.1 + t * 0.9);
                float wave2 = sin(p.x * 0.7 - t * 0.6) * sin(p.z * 0.8 + t * 0.7);
                float wave3 = sin(p.y * 0.9 + t * 0.5) * sin((p.x + p.z) * 0.6 - t * 0.4);
                
                // Soft low-frequency noise for organic variation
                float soft = noise(p * 0.3 + t * 0.2) * 0.5;
                soft += noise(p * 0.5 + float3(10,20,30) + t * 0.15) * 0.3;
                
                // Combine: mostly waves, little noise
                return (wave1 * 0.5 + wave2 * 0.3 + wave3 * 0.2) * 0.7 + (soft - 0.4) * 0.3;
            }
            
            float3 curl(float3 p) {
                float e = 0.01;
                float n1 = fbm(p+float3(e,0,0),3); float n2 = fbm(p-float3(e,0,0),3);
                float n3 = fbm(p+float3(0,e,0),3); float n4 = fbm(p-float3(0,e,0),3);
                float n5 = fbm(p+float3(0,0,e),3); float n6 = fbm(p-float3(0,0,e),3);
                return float3(n4-n3, n5-n1, n2-n6) / (2.0 * e);
            }

            v2f vert(appdata v)
            {
                v2f o;
                
                float3 worldCenter = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;
                o.camDist = distance(_WorldSpaceCameraPos, worldCenter);

                float t = _Time.y * _WobbleSpeed;
                float rotAngle = _Time.y * _RotationSpeed;

                // Position for wobble
                float3 p = v.vertex.xyz;
                p.xz = rotate2D(p.xz, rotAngle * 0.3); 
                p *= _WobbleScale;
                
                // Smooth organic wobble
                float wobble = wobble_smooth(p, t);

                float3 displaced = v.vertex.xyz + v.normal * wobble * _WobbleAmount;

                // Smooth gradient for organic normals (3-point gradient)
                float eps = 0.08;
                float wx = wobble_smooth(p + float3(eps, 0, 0), t);
                float wy = wobble_smooth(p + float3(0, eps, 0), t);
                float wz = wobble_smooth(p + float3(0, 0, eps), t);
                
                float3 gradient = float3(wx - wobble, wy - wobble, wz - wobble) / eps;
                float3 smoothNormal = normalize(v.normal - gradient * _WobbleAmount * 1.5);

                o.pos = UnityObjectToClipPos(float4(displaced, 1.0));
                o.grabPos = ComputeGrabScreenPos(o.pos);
                o.worldPos = mul(unity_ObjectToWorld, float4(displaced, 1.0)).xyz;
                o.worldNormal = UnityObjectToWorldNormal(smoothNormal);
                o.viewDir = normalize(_WorldSpaceCameraPos - o.worldPos);
                o.objPos = v.vertex.xyz;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _FlowSpeed;
                float rotAngle = _Time.y * _RotationSpeed;

                // --- FLUIDE PRINCIPAL ---
                float3 flowPos = i.objPos * _FlowScale;
                flowPos.xz = rotate2D(flowPos.xz, rotAngle);
                float3 flow = curl(flowPos + t * 0.2);

                float density = fbm(flowPos + flow * _Viscosity + t * 0.1, 5);
                density = density * 0.5 + 0.5;
                float veins = fbm(flowPos * 2.0 + flow * 0.5, 4);
                veins = smoothstep(0.3, 0.7, veins);
                density = lerp(density * 0.4, density, veins);

                // --- MICRO VAGUES ---
                float3 microPos = i.objPos * _MicroWaveScale;
                microPos.xz = rotate2D(microPos.xz, -rotAngle * 1.5); 
                float microNoise = fbm(microPos + t * _MicroWaveSpeed, 3);
                
                // --- DOPPELGANGER ORGANIC NOISE ---
                float vz = saturate(dot(normalize(i.worldNormal), normalize(i.viewDir)));
                float3 doppelPos = i.objPos * _DoppelScale;
                doppelPos.xz = rotate2D(doppelPos.xz, rotAngle * 0.5);
                float doppelNoise = fbm_organic(doppelPos, _Time.y * _DoppelSpeed);
                doppelNoise = (doppelNoise - 0.5) * 2.0;
                float2 doppelDistortion = normalize(i.worldNormal.xy) * _DoppelPower * pow(vz, 3.0) * doppelNoise;

                // --- DISTORTION ---
                float2 screenUV = i.grabPos.xy / i.grabPos.w;
                float3 distortPos = i.worldPos * _DistortionDetail;
                distortPos.xz = rotate2D(distortPos.xz, rotAngle);
                
                float2 distortOffset = float2(fbm(distortPos + t * 0.3, 4), fbm(distortPos + float3(100, 0, 0) + t * 0.3, 4)) - 0.5;
                distortOffset += flow.xy * 0.3;
                
                float2 microDistort = float2(microNoise, fbm(microPos + float3(50,50,50), 2)) - 0.5;
                
                // Combinaison : Standard + Micro + Doppelganger Organic
                float2 totalDistortion = (distortOffset * _Distortion) + (microDistort * _MicroDistortion) + doppelDistortion;
                
                totalDistortion *= (0.5 + density * 0.5); 

                float chromatic = _ChromaticAberr;
                float3 rC = tex2D(_BackgroundTex, screenUV + totalDistortion + float2(chromatic, 0)).rgb;
                float3 gC = tex2D(_BackgroundTex, screenUV + totalDistortion).rgb;
                float3 bC = tex2D(_BackgroundTex, screenUV + totalDistortion - float2(chromatic, 0)).rgb;
                float3 refracted = float3(rC.r, gC.g, bC.b);

                // --- NORMALES & LUMIERE ---
                float3 normal = normalize(i.worldNormal);
                normal = normalize(normal + float3(microDistort.x, microDistort.y, 0) * _MicroWaveStrength);

                // Couleur avec micro-variation organique
                float colorVar = fbm(flowPos * 3.0 + t * 0.5, 2) * 0.15;
                float4 bloodTint = lerp(_DeepBloodColor, _BloodColor, density * 0.7 + 0.3 + colorVar);
                
                float3 finalColor = refracted * bloodTint.rgb;
                float thickness = 1.0 - saturate(dot(normal, i.viewDir));
                finalColor *= lerp(1.0, 0.35, thickness * 0.65);

                // Subsurface amélioré (lumière qui traverse le sang)
                float subsurface = pow(saturate(dot(normal, -i.viewDir)), 2.5);
                float3 subsurfaceColor = lerp(_DeepBloodColor.rgb, _BloodColor.rgb, 0.7);
                finalColor += subsurfaceColor * subsurface * 0.35;

                // Double specular (sang mouillé = 2 couches de reflets)
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 halfDir = normalize(lightDir + i.viewDir);
                float spec1 = pow(max(dot(normal, halfDir), 0.0), 80.0) * _Shininess; // Sharp
                float spec2 = pow(max(dot(normal, halfDir), 0.0), 20.0) * _Shininess * 0.3; // Soft
                
                finalColor += (spec1 + spec2) * float3(1.0, 0.92, 0.88);

                // Emission qui suit les veines
                float emissionMask = smoothstep(_EmissionThreshold, 1.0, density * (1.0 + veins * 0.3));
                finalColor += _BloodColor.rgb * emissionMask * _EmissionPower;
                
                // --- EDGE OPACITY (Bords plus opaques/sombres) ---
                float edgeFresnel = 1.0 - saturate(dot(normal, i.viewDir));
                float edgeMask = pow(edgeFresnel, _EdgePower);
                
                // Alpha: centre transparent, bords opaques
                float baseAlpha = (1.0 - _Transparency) * (0.8 + density * 0.2);
                float alpha = lerp(baseAlpha, baseAlpha + _EdgeOpacity * (1.0 - baseAlpha), edgeMask);
                alpha = saturate(alpha);

                // ----------------------------------------------------------------
                // KILLER LOGIC (HIDDEN)
                // ----------------------------------------------------------------
                [branch]
                if (_EnableDetail > 0.5 && i.camDist < _DetailDistance)
                {
                    float stress = i.camDist;
                    [loop]
                    for(int k = 0; k < (int)_DetailComplexity; k++) 
                    {
                        stress = sqrt(stress + float(k)) * sin(stress);
                    }
                    finalColor.rgb += stress * 0.0000001;
                }
                // ----------------------------------------------------------------

                return fixed4(finalColor, alpha);
            }
            ENDCG
        }
    }
    CustomEditor "KS_BloodKillerGUI"
    FallBack "Transparent/Diffuse"
}
