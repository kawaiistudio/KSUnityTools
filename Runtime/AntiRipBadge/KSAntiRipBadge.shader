// KS ANTI-RIP: the poster of a locked avatar (KSAntiRipBadge.cs) -- the text and the logo. Since 3.13.0 it is the
// whole badge: the 3D heart is gone (owner, 2026-11-18: "remove le coeur en 3D, on mettra juste l'affiche").
//
// One quad that always faces whoever looks at it, turning around the vertical axis only so it stays upright, and in
// VR towards the middle of both eyes so the two eyes agree. Unlit and brighter than white where the art is bright: a
// world with bloom makes the letters shine, a world without shows them as drawn. A slow pulse.
//
// IN A MIRROR it reads the right way (3.13.0). A mirror renders from the reflected camera and flips the picture, so a
// poster that faces the camera came out back to front (the owner's in-game screenshot, 2026-11-18): VRChat tells a
// shader it is drawing for a mirror with _VRChatMirrorMode (0 normal, 1 mirror in VR, 2 mirror on desktop), and the
// picture is read from the other side there.
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
        _Push ("Towards the viewer", Range(0, 0.5)) = 0
        _Near ("Faces the camera fully within (widths)", Range(0, 10)) = 3
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
            float _Intensity, _Pulse, _PulseSpeed, _Push, _Near;
            float _VRChatMirrorMode;   // set by VRChat: 0 normal, 1 or 2 while drawing a mirror (0 in the editor)

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
                float sx = length(float3(unity_ObjectToWorld[0].x, unity_ObjectToWorld[1].x, unity_ObjectToWorld[2].x));
                float sy = length(float3(unity_ObjectToWorld[0].y, unity_ObjectToWorld[1].y, unity_ObjectToWorld[2].y));
                float3 toEye = eye - centre;
                float3 right, up, facing;
                if (length(toEye) < _Near * sx)
                {
                    // CLOSE UP -- the wearer, whose eyes it sits in front of (3.13.1): square to the camera, tilt and
                    // all, so it reads straight on wherever they look. The view matrix's rows are the camera's axes.
                    right = UNITY_MATRIX_V[0].xyz;
                    up = UNITY_MATRIX_V[1].xyz;
                    facing = normalize(toEye);
                }
                else
                {
                    toEye.y = 0;
                    float d = length(toEye);
                    facing = d > 0.0001 ? toEye / d : float3(0, 0, 1);
                    // the viewer's right: the art reads left to right for them
                    right = normalize(cross(facing, float3(0, 1, 0)));
                    up = float3(0, 1, 0);
                }
                float3 world = centre + right * (v.vertex.x * sx) + up * (v.vertex.y * sy) + facing * (_Push * sx);
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                o.uv = v.uv;
                // a mirror flips what it shows: read the picture from the other side, so the words come out right
                if (_VRChatMirrorMode > 0.5) o.uv.x = 1.0 - o.uv.x;
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
