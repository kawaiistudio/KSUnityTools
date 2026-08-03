Shader "Kawaii Studio/KSScreenShader"
{
    Properties
    {
        [Header(MAIN SETTINGS)]
        _MainTex ("Video Texture (Atlas)", 2D) = "white" {}
        _Alpha ("Global Alpha", Range(0,1)) = 1.0

        [Space(10)]
        [Header(DISPLAY MODE)]
        [Enum(Auto,0,PC,1,VR,2)] _DisplayMode ("Display Mode", Float) = 0

        [Space(10)]
        [Header(VR CONFIGURATION)]
        _FloatDistance ("Real Distance (meters)", Range(0.1, 50.0)) = 5.0
        _PerceivedDistance ("Depth Fix Multiplier", Range(1.0, 1.5)) = 1.25
        _ScreenSizeMeters ("Video Width (meters)", Range(0.2, 50.0)) = 1.2
        _VRXOffset ("VR Horizontal Offset", Range(-10.0, 10.0)) = 0.0
        _VRYOffset ("VR Vertical Offset", Range(-10.0, 10.0)) = 0.0

        [Space(10)]
        [Header(PC CONFIGURATION)]
        _PCHeightScale ("Screen Height Fill", Range(0.1, 1.0)) = 0.95
        _PCXOffset ("PC Horizontal Offset", Range(-2.0, 2.0)) = 0.0
        _PCYOffset ("PC Vertical Offset", Range(-2.0, 2.0)) = 0.0

        [Space(10)]
        [Header(METADATA INJECTED)]
        _VideoWidth ("Video Width", Float) = 1920
        _VideoHeight ("Video Height", Float) = 1080
    }

    SubShader
    {
        Tags { "Queue"="Overlay+500" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZTest Always   
            ZWrite Off     
            Cull Off       

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST; 

            float _Alpha, _DisplayMode, _FloatDistance, _PerceivedDistance, _ScreenSizeMeters;
            float _PCHeightScale, _VRXOffset, _VRYOffset, _PCXOffset, _PCYOffset;
            float _VideoWidth, _VideoHeight;

            struct appdata
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                bool isVR = (_DisplayMode == 0) ? (abs(unity_CameraProjection[0][2]) > 0.0001) : (_DisplayMode == 2);
                
                float videoAspect = _VideoWidth / _VideoHeight;
                float screenAspect = _ScreenParams.x / _ScreenParams.y;
                float2 localCoord = v.texcoord - 0.5;

                float2 finalUV = v.texcoord;

                if (isVR)
                {
                    // --- LOGIQUE VR ---
                    float zDepth = -_FloatDistance * _PerceivedDistance;
                    float3 viewPos;
                    // Utilise uniquement les offsets VR
                    viewPos.x = (localCoord.x * _ScreenSizeMeters) + _VRXOffset;
                    viewPos.y = (localCoord.y * _ScreenSizeMeters / videoAspect) + _VRYOffset;
                    viewPos.z = zDepth;
                    o.pos = UnityViewToClipPos(viewPos);
                    
                    finalUV = v.texcoord;
                }
                else
                {
                    // --- LOGIQUE PC ---
                    float scaleY = _PCHeightScale;
                    float scaleX = scaleY * (videoAspect / screenAspect);
                    if (scaleX > 1.0) { scaleY /= scaleX; scaleX = 1.0; }

                    float4 clipPos = float4(0, 0, 0, 1);
                    // Utilise uniquement les offsets PC
                    clipPos.x = (localCoord.x * 2.0 * scaleX) + _PCXOffset;
                    clipPos.y = (localCoord.y * 2.0 * scaleY) + _PCYOffset;
                    
                    #if UNITY_REVERSED_Z
                        clipPos.z = 1.0e-5; 
                    #else
                        clipPos.z = -1.0e-5;
                    #endif
                    clipPos.w = 1.0;
                    o.pos = clipPos;

                    finalUV.y = 1.0 - v.texcoord.y;
                }

                o.uv = TRANSFORM_TEX(finalUV, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 col = tex2D(_MainTex, i.uv);
                col.a *= _Alpha;
                if (col.a < 0.01) discard;
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}