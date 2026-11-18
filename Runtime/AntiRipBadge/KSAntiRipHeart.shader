// KS ANTI-RIP: the heart of the locked badge (KSAntiRipBadge.cs) -- a real 3D heart, pink, glowing.
//
// It turns slowly on itself, beats, and is lit from nowhere: a candy-pink body that goes hot towards its edge, a
// highlight, and a soft halo drawn as a second, slightly bigger shell, so it glows even in a world without bloom
// (and shines in one that has it: the bright parts go past white).
//
// A viewer who blocks custom shaders gets VRChat's unlit fallback: the same heart in flat pink, not turning.
Shader "Kawaii Studio/Anti-Rip Heart"
{
    Properties
    {
        _Color ("Pink", Color) = (1, 0.36, 0.72, 1)
        [NoScaleOffset] _MainTex ("(fallback only)", 2D) = "white" {}
        _Rim ("Edge", Color) = (1, 0.78, 0.93, 1)
        _RimPower ("Edge width", Range(0.5, 6)) = 2.2
        _Intensity ("Brightness", Range(1, 4)) = 1.5
        _Halo ("Halo size", Range(0, 0.2)) = 0.055
        _HaloStrength ("Halo strength", Range(0, 3)) = 1.15
        _Spin ("Turns per second", Range(0, 1)) = 0.14
        _Beat ("Beat", Range(0, 0.2)) = 0.06
        _HeartY ("Heart centre (object Y)", Float) = 0.3
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+10"
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
            "VRCFallback" = "Unlit"
        }
        Lighting Off

        CGINCLUDE
        #include "UnityCG.cginc"

        fixed4 _Color, _Rim;
        float _RimPower, _Intensity, _Halo, _HaloStrength, _Spin, _Beat, _HeartY;

        struct appdata
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float3 normal : TEXCOORD0;
            float3 view : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        // two quick beats and a rest, like a heart
        float Beat()
        {
            float t = frac(_Time.y * 0.85);
            return exp(-pow((t - 0.08) * 14.0, 2.0)) + 0.6 * exp(-pow((t - 0.3) * 14.0, 2.0));
        }

        v2f Place(appdata v, float grow)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_OUTPUT(v2f, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

            float s, c;
            sincos(_Time.y * _Spin * 6.2831853, s, c);
            float3 p = v.vertex.xyz;
            float3 n = v.normal;
            p.y -= _HeartY;
            p *= 1.0 + _Beat * Beat();
            p += n * grow;
            p.xz = float2(p.x * c + p.z * s, -p.x * s + p.z * c);
            n.xz = float2(n.x * c + n.z * s, -n.x * s + n.z * c);
            p.y += _HeartY + 0.012 * sin(_Time.y * 1.6);

            float4 world = mul(unity_ObjectToWorld, float4(p, 1));
            o.pos = mul(UNITY_MATRIX_VP, world);
            o.normal = UnityObjectToWorldNormal(n);
            o.view = _WorldSpaceCameraPos - world.xyz;
            return o;
        }
        ENDCG

        // the heart itself
        Pass
        {
            Cull Back
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            v2f vert(appdata v) { return Place(v, 0.0); }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 v = normalize(i.view);
                float facing = saturate(dot(n, v));
                float rim = pow(1.0 - facing, _RimPower);
                // a highlight from up and to the side, as on something glossy
                float3 h = normalize(v + normalize(float3(-0.35, 0.85, 0.25)));
                float shine = pow(saturate(dot(n, h)), 48.0);
                float3 col = _Color.rgb * (0.78 + 0.34 * facing) + _Rim.rgb * rim * 0.9 + shine * 0.85;
                return fixed4(col * _Intensity * (1.0 + 0.25 * Beat()), 1);
            }
            ENDCG
        }

        // its halo: the same heart a little bigger, seen from inside, added on top
        Pass
        {
            Cull Front
            ZWrite Off
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            v2f vert(appdata v) { return Place(v, _Halo); }

            fixed4 frag(v2f i) : SV_Target
            {
                float inside = saturate(-dot(normalize(i.normal), normalize(i.view)));
                float glow = pow(inside, 1.6) * _HaloStrength * (1.0 + 0.6 * Beat());
                return fixed4(_Color.rgb * glow, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
