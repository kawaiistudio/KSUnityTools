Shader "Kawaii Studio/KS_ScreenLine"
{
    Properties
    {
        [Header(Visuals)]
        [HDR] _Color ("Color (HDR)", Color) = (1,1,1,1)
        _Brightness ("Brightness Multiplier", Range(0, 10)) = 1.0
        
        [Header(Hue Shift)]
        _Hue ("Hue Shift", Range(0, 1)) = 0.0
        [Toggle] _AutoHue ("Auto Hue Loop", Float) = 0
        _HueSpeed ("Auto Hue Speed", Range(0, 5)) = 0.5
        
        [Header(Line Settings)]
        _Width ("Line Width", Range(0.0, 1.0)) = 0.02
        _Softness ("Edge Softness", Range(0.0, 0.5)) = 0.01
        
        [Header(Positioning)]
        _PosX ("Position X", Range(0, 1)) = 0.5
        _PosY ("Position Y", Range(0, 1)) = 0.5
        _Rotation ("Rotation (Degrees)", Range(0, 360)) = 0
        
        [Header(Blending)]
        _Alpha ("Alpha Transparency", Range(0, 1)) = 1.0
        
        [Header(Rendering Range)]
        [Toggle] _UseRange ("Enable Distance Range (Camera)", Float) = 0
        _MinRange ("Min Camera Distance", Float) = 0.0
        _MaxRange ("Max Camera Distance", Float) = 10000.0

        [Header(Infinity Mode)]
        [Toggle] _Infinity ("Infinite Size (Skybox Mode)", Float) = 0
    }
    SubShader
    {
        Tags 
        { 
            "Queue"="Overlay+2000" 
            "RenderType"="Transparent" 
            "IgnoreProjector"="True" 
            "PreviewType"="Plane" 
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            float4 _Color;
            float _Brightness;
            float _Hue;
            float _AutoHue;
            float _HueSpeed;
            float _Width;
            float _PosX;
            float _PosY;
            float _Rotation;
            float _Alpha;
            float _Softness;

            float _UseRange;
            float _MinRange;
            float _MaxRange;

            float _Infinity;

            v2f vert (appdata v)
            {
                v2f o;

                if (_Infinity > 0.5)
                {
                    // Infinite Size Mode (Skybox-like)
                    // 1. Center object on camera
                    // 2. Scale vertex to be large
                    float3 worldScale = float3(100.0, 100.0, 100.0); // Large scale
                    float3 viewSpacePos = v.vertex.xyz * worldScale; // Scale local vertex
                    
                    // Add camera position to make it stay with camera
                    float3 worldPos = _WorldSpaceCameraPos + viewSpacePos;
                    
                    o.vertex = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                    o.worldPos = worldPos;
                }
                else
                {
                    o.vertex = UnityObjectToClipPos(v.vertex);
                    o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                }

                o.screenPos = ComputeScreenPos(o.vertex);
                
                return o;
            }

            // Function to convert RGB to HSV, shift Hue, then back to RGB
            float3 ShiftHue(float3 color, float shift, float autoHueActive)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(color.bg, K.wz), float4(color.gb, K.xy), step(color.b, color.g));
                float4 q = lerp(float4(p.xyw, color.r), float4(color.r, p.yzx), step(p.x, color.r));

                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                float3 hsv = float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
                
                // Smart Saturation: If color is white/gray (low saturation) and we are shifting hue,
                // force saturation to 1.0 so the color actually changes.
                // autoHueActive is 1 if shifting is enabled (manual or auto)
                float lowSat = step(hsv.y, 0.1);
                hsv.y = lerp(hsv.y, 1.0, lowSat * autoHueActive);
                
                // Shift Hue
                hsv.x = frac(hsv.x + shift);
                
                // Back to RGB
                float4 K2 = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p2 = abs(frac(hsv.xxx + K2.xyz) * 6.0 - K2.www);
                return hsv.z * lerp(K2.xxx, clamp(p2 - K2.xxx, 0.0, 1.0), hsv.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float camDist = distance(i.worldPos, _WorldSpaceCameraPos);

                float2 uv = i.screenPos.xy / i.screenPos.w;

                // Aspect Ratio
                float aspect = abs(unity_CameraProjection[1][1] / unity_CameraProjection[0][0]);

                // Position & Rotation
                float2 center = float2(_PosX, _PosY);
                float2 p = uv - center;
                p.x *= aspect;

                float rad = radians(_Rotation);
                float s = sin(rad);
                float c = cos(rad);
                float2 rotated = float2(
                    p.x * c - p.y * s,
                    p.x * s + p.y * c
                );

                // Distance
                float dist = abs(rotated.y);
                float halfWidth = _Width * 0.5;

                // Softness
                float edge = max(_Softness, 0.001);
                float lineAlpha = 1.0 - smoothstep(halfWidth - edge, halfWidth + edge, dist);

                // Color Calculation with Hue Shift
                float3 rgb = _Color.rgb;
                
                // Calculate Hue Shift
                float hueShift = _Hue;
                float isShifting = 0.0;
                
                if (_AutoHue > 0.5)
                {
                    hueShift += _Time.y * _HueSpeed;
                    isShifting = 1.0;
                }
                else if (abs(_Hue) > 0.001)
                {
                    isShifting = 1.0;
                }
                
                // Apply Shift
                rgb = ShiftHue(rgb, hueShift, isShifting);

                float4 finalColor = float4(rgb * _Brightness, 1.0);
                finalColor.a = _Color.a * lineAlpha * _Alpha;

                // --- Rendering Range Logic (Camera Distance) ---
                if (_UseRange > 0.5)
                {
                    float rangeAlpha = smoothstep(_MinRange, _MinRange + 0.1, camDist) * 
                                       smoothstep(_MaxRange, _MaxRange - 0.5, camDist);
                    finalColor.a *= rangeAlpha;
                }
                // -----------------------------

                return finalColor;
            }
            ENDCG
        }
    }
    // CustomEditor "KS_ScreenLineGUI"
}

/*
    -----------------------------------------------------------------------
    Kawaii Studio
    
    Join us on Discord: https://discord.gg/xAeJrSAgqG
    Telegram: https://t.me/kawaiistudio | VRChat Group: https://vrchat.com/home/group/grp_7bf987ee-2f4a-4eae-b9b5-c060b97250ab
    
    Logo: Assets/Kawaii Studio/Editor/Cache/logo.png
    
    This shader is made for AKCESSIVE <3
    -----------------------------------------------------------------------
*/
