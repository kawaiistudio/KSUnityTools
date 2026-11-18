// KS ANTI-RIP: the words of the locked badge (KSAntiRipBadge.cs) -- the panel with the text and the logo, in front of
// the 3D heart (KSAntiRipHeart.shader).
//
// One quad that always faces whoever looks at it, turning around the vertical axis only so it stays upright, and in
// VR towards the middle of both eyes so the two eyes agree. It stands a little towards the viewer, so the heart is
// always behind it. Unlit and brighter than white where the art is bright: a world with bloom makes the letters
// shine, a world without shows them as drawn. A slow pulse.
//
// A viewer who blocks custom shaders gets VRChat's unlit cutout fallback: the same picture, not turning.
Shader "Kawaii Studio/Anti-Rip Badge"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Badge", 2D) = "white" {}
        _Intensity ("Brightness", Range(1, 4)) = 1.55
        _Pulse ("Pulse", Range(0, 0.5)) = 0.14
        _PulseSpeed ("Pulse speed", Range(0, 6)) = 2.0
        _Push ("Towards the viewer", Range(0, 0.5)) = 0.2
        _Cutoff ("Alpha cutoff (fallback only)", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
            "PreviewType" = "Plane"
            "VRCFallback" = "UnlitCutoutDoubleSided"
        }
        Cull Off
        ZWrite Off
        Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Intensity, _Pulse, _PulseSpeed, _Push;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 centre = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                #if defined(USING_STEREO_MATRICES)
                    float3 eye = (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]) * 0.5;
                #else
                    float3 eye = _WorldSpaceCameraPos;
                #endif
                float3 toEye = eye - centre;
                toEye.y = 0;
                float d = length(toEye);
                float3 facing = d > 0.0001 ? toEye / d : float3(0, 0, 1);
                // the viewer's right: the art reads left to right for them
                float3 right = normalize(cross(facing, float3(0, 1, 0)));
                float sx = length(float3(unity_ObjectToWorld[0].x, unity_ObjectToWorld[1].x, unity_ObjectToWorld[2].x));
                float sy = length(float3(unity_ObjectToWorld[0].y, unity_ObjectToWorld[1].y, unity_ObjectToWorld[2].y));
                float3 world = centre + right * (v.vertex.x * sx) + float3(0, 1, 0) * (v.vertex.y * sy) + facing * (_Push * sx);
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 art = tex2D(_MainTex, i.uv);
                float pulse = 1.0 + _Pulse * sin(_Time.y * _PulseSpeed);
                return fixed4(art.rgb * _Intensity * pulse, art.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
