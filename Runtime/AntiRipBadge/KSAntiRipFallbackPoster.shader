// KS ANTI-RIP 3.14: the poster for a viewer who blocks the wearer's shaders.
//
// When KS Anti-Rip closes meshes through the avatar's own shader, the repair lives in that shader -- so a viewer who
// blocks the wearer's shaders gets VRChat's fallback shaders and no repair. The patched materials say "Hidden" to the
// fallback system (that viewer does not see the protected parts at all), and this quad shows them the Kawaii Studio
// poster instead (owner, 2026-11-18: "les gens qui me show pas l'avatar doivent voir le logo anti-rip").
//
// Nothing has to tell who is looking. With custom shaders shown, THIS shader runs: every vertex lands on one point and
// the fragment discards, so the quad is never drawn. With shaders blocked, VRChat swaps it for its unlit cut-out,
// double-sided fallback, which draws _MainTex -- the poster.
Shader "Kawaii Studio/Anti-Rip Fallback Poster"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Poster", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff (fallback only)", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags
        {
            "Queue" = "AlphaTest"
            "RenderType" = "TransparentCutout"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "VRCFallback" = "UnlitCutoutDoubleSided"
        }
        Pass
        {
            ZWrite Off
            ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // every vertex on one point: a triangle of no area, never rasterised
                o.pos = float4(0, 0, 0, 1);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                clip(-1);
                return 0;
            }
            ENDCG
        }
    }
}
