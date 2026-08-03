Shader "Kawaii Studio/VR Stereo Display Debug"
{
    Properties
    {
        _LeftEyeTex ("Left Eye Texture", 2D) = "black" {}
        _RightEyeTex ("Right Eye Texture", 2D) = "black" {}
        _DisplayMode ("Display Mode", Float) = 0 // 0 = SideBySide, 1 = LeftOnly, 2 = RightOnly, 3 = Anaglyph
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _LeftEyeTex;
            sampler2D _RightEyeTex;
            float _DisplayMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                fixed4 col = fixed4(0, 0, 0, 1);

                if (_DisplayMode == 0) // Side-by-Side
                {
                    if (uv.x < 0.5)
                    {
                        // Left half = Left eye
                        float2 leftUV = float2(uv.x * 2.0, uv.y);
                        col = tex2D(_LeftEyeTex, leftUV);
                    }
                    else
                    {
                        // Right half = Right eye
                        float2 rightUV = float2((uv.x - 0.5) * 2.0, uv.y);
                        col = tex2D(_RightEyeTex, rightUV);
                    }
                }
                else if (_DisplayMode == 1) // Left Eye Only
                {
                    col = tex2D(_LeftEyeTex, uv);
                }
                else if (_DisplayMode == 2) // Right Eye Only
                {
                    col = tex2D(_RightEyeTex, uv);
                }
                else if (_DisplayMode == 3) // Anaglyph (Red-Cyan)
                {
                    fixed4 left = tex2D(_LeftEyeTex, uv);
                    fixed4 right = tex2D(_RightEyeTex, uv);
                    
                    col.r = left.r;
                    col.g = right.g;
                    col.b = right.b;
                    col.a = max(left.a, right.a);
                }

                return col;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
