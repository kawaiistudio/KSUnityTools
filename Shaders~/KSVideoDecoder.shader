Shader "Kawaii Studio/KSVideoDecoder"
{
    Properties
    {
        _CustomTime("Time", float) = 0.0
        _FrameRate("FrameRate", float) = 30.0
        _AtlasSizeX("Atlas Size X", float) = 16
        _AtlasSizeY("Atlas Size Y", float) = 16
        [NoScaleOffset]_MainTex("Texture 0", 2D) = "black" {}
        [NoScaleOffset]_MainTex1("Texture 1", 2D) = "black" {}
        [NoScaleOffset]_MainTex2("Texture 2", 2D) = "black" {}
        [NoScaleOffset]_MainTex3("Texture 3", 2D) = "black" {}
        [NoScaleOffset]_MainTex4("Texture 4", 2D) = "black" {}
        [NoScaleOffset]_MainTex5("Texture 5", 2D) = "black" {}
        [NoScaleOffset]_MainTex6("Texture 6", 2D) = "black" {}
        [NoScaleOffset]_MainTex7("Texture 7", 2D) = "black" {}
        [NoScaleOffset]_MainTex8("Texture 8", 2D) = "black" {}
        [NoScaleOffset]_MainTex9("Texture 9", 2D) = "black" {}
        [NoScaleOffset]_MainTex10("Texture 10", 2D) = "black" {}
        [NoScaleOffset]_MainTex11("Texture 11", 2D) = "black" {}
        [NoScaleOffset]_MainTex12("Texture 12", 2D) = "black" {}
        [NoScaleOffset]_MainTex13("Texture 13", 2D) = "black" {}
        [NoScaleOffset]_MainTex14("Texture 14", 2D) = "black" {}
        [NoScaleOffset]_MainTex15("Texture 15", 2D) = "black" {}
        [NoScaleOffset]_MainTex16("Texture 16", 2D) = "black" {}
        [NoScaleOffset]_MainTex17("Texture 17", 2D) = "black" {}
        [NoScaleOffset]_MainTex18("Texture 18", 2D) = "black" {}
        [NoScaleOffset]_MainTex19("Texture 19", 2D) = "black" {}
        [NoScaleOffset]_MainTex20("Texture 20", 2D) = "black" {}
        [NoScaleOffset]_MainTex21("Texture 21", 2D) = "black" {}
        [NoScaleOffset]_MainTex22("Texture 22", 2D) = "black" {}
        [NoScaleOffset]_MainTex23("Texture 23", 2D) = "black" {}
        [NoScaleOffset]_MainTex24("Texture 24", 2D) = "black" {}
        [NoScaleOffset]_MainTex25("Texture 25", 2D) = "black" {}
        [NoScaleOffset]_MainTex26("Texture 26", 2D) = "black" {}
        [NoScaleOffset]_MainTex27("Texture 27", 2D) = "black" {}
        [NoScaleOffset]_MainTex28("Texture 28", 2D) = "black" {}
        [NoScaleOffset]_MainTex29("Texture 29", 2D) = "black" {}
        [NoScaleOffset]_MainTex30("Texture 30", 2D) = "black" {}
        [NoScaleOffset]_MainTex31("Texture 31", 2D) = "black" {}
        [NoScaleOffset]_MainTex32("Texture 32", 2D) = "black" {}
        [NoScaleOffset]_MainTex33("Texture 33", 2D) = "black" {}
        [NoScaleOffset]_MainTex34("Texture 34", 2D) = "black" {}
        [NoScaleOffset]_MainTex35("Texture 35", 2D) = "black" {}
        [NoScaleOffset]_MainTex36("Texture 36", 2D) = "black" {}
        [NoScaleOffset]_MainTex37("Texture 37", 2D) = "black" {}
        [NoScaleOffset]_MainTex38("Texture 38", 2D) = "black" {}
        [NoScaleOffset]_MainTex39("Texture 39", 2D) = "black" {}
        [NoScaleOffset]_MainTex40("Texture 40", 2D) = "black" {}
        [NoScaleOffset]_MainTex41("Texture 41", 2D) = "black" {}
        [NoScaleOffset]_MainTex42("Texture 42", 2D) = "black" {}
        [NoScaleOffset]_MainTex43("Texture 43", 2D) = "black" {}
        [NoScaleOffset]_MainTex44("Texture 44", 2D) = "black" {}
        [NoScaleOffset]_MainTex45("Texture 45", 2D) = "black" {}
        [NoScaleOffset]_MainTex46("Texture 46", 2D) = "black" {}
        [NoScaleOffset]_MainTex47("Texture 47", 2D) = "black" {}
        [NoScaleOffset]_MainTex48("Texture 48", 2D) = "black" {}
        [NoScaleOffset]_MainTex49("Texture 49", 2D) = "black" {}
        [NoScaleOffset]_MainTex50("Texture 50", 2D) = "black" {}
        [NoScaleOffset]_MainTex51("Texture 51", 2D) = "black" {}
        [NoScaleOffset]_MainTex52("Texture 52", 2D) = "black" {}
        [NoScaleOffset]_MainTex53("Texture 53", 2D) = "black" {}
        [NoScaleOffset]_MainTex54("Texture 54", 2D) = "black" {}
        [NoScaleOffset]_MainTex55("Texture 55", 2D) = "black" {}
        [NoScaleOffset]_MainTex56("Texture 56", 2D) = "black" {}
        [NoScaleOffset]_MainTex57("Texture 57", 2D) = "black" {}
        [NoScaleOffset]_MainTex58("Texture 58", 2D) = "black" {}
        [NoScaleOffset]_MainTex59("Texture 59", 2D) = "black" {}
        [NoScaleOffset]_MainTex60("Texture 60", 2D) = "black" {}
        [NoScaleOffset]_MainTex61("Texture 61", 2D) = "black" {}
        [NoScaleOffset]_MainTex62("Texture 62", 2D) = "black" {}
        [NoScaleOffset]_MainTex63("Texture 63", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                uint atlas : TEXCOORD1;
            };

            uniform SamplerState sampler_MainTex;
            uniform Texture2D _MainTex;
            uniform Texture2D _MainTex1;
            uniform Texture2D _MainTex2;
            uniform Texture2D _MainTex3;
            uniform Texture2D _MainTex4;
            uniform Texture2D _MainTex5;
            uniform Texture2D _MainTex6;
            uniform Texture2D _MainTex7;
            uniform Texture2D _MainTex8;
            uniform Texture2D _MainTex9;
            uniform Texture2D _MainTex10;
            uniform Texture2D _MainTex11;
            uniform Texture2D _MainTex12;
            uniform Texture2D _MainTex13;
            uniform Texture2D _MainTex14;
            uniform Texture2D _MainTex15;
            uniform Texture2D _MainTex16;
            uniform Texture2D _MainTex17;
            uniform Texture2D _MainTex18;
            uniform Texture2D _MainTex19;
            uniform Texture2D _MainTex20;
            uniform Texture2D _MainTex21;
            uniform Texture2D _MainTex22;
            uniform Texture2D _MainTex23;
            uniform Texture2D _MainTex24;
            uniform Texture2D _MainTex25;
            uniform Texture2D _MainTex26;
            uniform Texture2D _MainTex27;
            uniform Texture2D _MainTex28;
            uniform Texture2D _MainTex29;
            uniform Texture2D _MainTex30;
            uniform Texture2D _MainTex31;
            uniform Texture2D _MainTex32;
            uniform Texture2D _MainTex33;
            uniform Texture2D _MainTex34;
            uniform Texture2D _MainTex35;
            uniform Texture2D _MainTex36;
            uniform Texture2D _MainTex37;
            uniform Texture2D _MainTex38;
            uniform Texture2D _MainTex39;
            uniform Texture2D _MainTex40;
            uniform Texture2D _MainTex41;
            uniform Texture2D _MainTex42;
            uniform Texture2D _MainTex43;
            uniform Texture2D _MainTex44;
            uniform Texture2D _MainTex45;
            uniform Texture2D _MainTex46;
            uniform Texture2D _MainTex47;
            uniform Texture2D _MainTex48;
            uniform Texture2D _MainTex49;
            uniform Texture2D _MainTex50;
            uniform Texture2D _MainTex51;
            uniform Texture2D _MainTex52;
            uniform Texture2D _MainTex53;
            uniform Texture2D _MainTex54;
            uniform Texture2D _MainTex55;
            uniform Texture2D _MainTex56;
            uniform Texture2D _MainTex57;
            uniform Texture2D _MainTex58;
            uniform Texture2D _MainTex59;
            uniform Texture2D _MainTex60;
            uniform Texture2D _MainTex61;
            uniform Texture2D _MainTex62;
            uniform Texture2D _MainTex63;

            uniform float4 _MainTex_TexelSize;
            uniform uint _AtlasSizeX;
            uniform uint _AtlasSizeY;
            uniform float _CustomTime;
            uniform float _FrameRate;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);

                float2 tileSize = 1.0 / float2(_AtlasSizeX, _AtlasSizeY);
                float2 frameSize = _MainTex_TexelSize.zw * tileSize;
                float2 pixelSize = 1.0 / frameSize;
                float2 uv = v.uv * (frameSize - 1) / frameSize + 0.5 * pixelSize;

                uint frame = _CustomTime * _FrameRate;
                uint framesPerSlice = _AtlasSizeX * _AtlasSizeY;
                o.atlas = frame / framesPerSlice;
                frame = frame % framesPerSlice;
                uint2 cell = uint2(frame % _AtlasSizeX, _AtlasSizeY - 1 - frame / _AtlasSizeX);

                o.uv = (cell + uv) * tileSize;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                switch (i.atlas)
                {
                case  0: return  _MainTex.Sample(sampler_MainTex, i.uv);
                case  1: return  _MainTex1.Sample(sampler_MainTex, i.uv);
                case  2: return  _MainTex2.Sample(sampler_MainTex, i.uv);
                case  3: return  _MainTex3.Sample(sampler_MainTex, i.uv);
                case  4: return  _MainTex4.Sample(sampler_MainTex, i.uv);
                case  5: return  _MainTex5.Sample(sampler_MainTex, i.uv);
                case  6: return  _MainTex6.Sample(sampler_MainTex, i.uv);
                case  7: return  _MainTex7.Sample(sampler_MainTex, i.uv);
                case  8: return  _MainTex8.Sample(sampler_MainTex, i.uv);
                case  9: return  _MainTex9.Sample(sampler_MainTex, i.uv);
                case 10: return  _MainTex10.Sample(sampler_MainTex, i.uv);
                case 11: return  _MainTex11.Sample(sampler_MainTex, i.uv);
                case 12: return  _MainTex12.Sample(sampler_MainTex, i.uv);
                case 13: return  _MainTex13.Sample(sampler_MainTex, i.uv);
                case 14: return  _MainTex14.Sample(sampler_MainTex, i.uv);
                case 15: return  _MainTex15.Sample(sampler_MainTex, i.uv);
                case 16: return  _MainTex16.Sample(sampler_MainTex, i.uv);
                case 17: return  _MainTex17.Sample(sampler_MainTex, i.uv);
                case 18: return  _MainTex18.Sample(sampler_MainTex, i.uv);
                case 19: return  _MainTex19.Sample(sampler_MainTex, i.uv);
                case 20: return  _MainTex20.Sample(sampler_MainTex, i.uv);
                case 21: return  _MainTex21.Sample(sampler_MainTex, i.uv);
                case 22: return  _MainTex22.Sample(sampler_MainTex, i.uv);
                case 23: return  _MainTex23.Sample(sampler_MainTex, i.uv);
                case 24: return  _MainTex24.Sample(sampler_MainTex, i.uv);
                case 25: return  _MainTex25.Sample(sampler_MainTex, i.uv);
                case 26: return  _MainTex26.Sample(sampler_MainTex, i.uv);
                case 27: return  _MainTex27.Sample(sampler_MainTex, i.uv);
                case 28: return  _MainTex28.Sample(sampler_MainTex, i.uv);
                case 29: return  _MainTex29.Sample(sampler_MainTex, i.uv);
                case 30: return  _MainTex30.Sample(sampler_MainTex, i.uv);
                case 31: return  _MainTex31.Sample(sampler_MainTex, i.uv);
                case 32: return  _MainTex32.Sample(sampler_MainTex, i.uv);
                case 33: return  _MainTex33.Sample(sampler_MainTex, i.uv);
                case 34: return  _MainTex34.Sample(sampler_MainTex, i.uv);
                case 35: return  _MainTex35.Sample(sampler_MainTex, i.uv);
                case 36: return  _MainTex36.Sample(sampler_MainTex, i.uv);
                case 37: return  _MainTex37.Sample(sampler_MainTex, i.uv);
                case 38: return  _MainTex38.Sample(sampler_MainTex, i.uv);
                case 39: return  _MainTex39.Sample(sampler_MainTex, i.uv);
                case 40: return  _MainTex40.Sample(sampler_MainTex, i.uv);
                case 41: return  _MainTex41.Sample(sampler_MainTex, i.uv);
                case 42: return  _MainTex42.Sample(sampler_MainTex, i.uv);
                case 43: return  _MainTex43.Sample(sampler_MainTex, i.uv);
                case 44: return  _MainTex44.Sample(sampler_MainTex, i.uv);
                case 45: return  _MainTex45.Sample(sampler_MainTex, i.uv);
                case 46: return  _MainTex46.Sample(sampler_MainTex, i.uv);
                case 47: return  _MainTex47.Sample(sampler_MainTex, i.uv);
                case 48: return  _MainTex48.Sample(sampler_MainTex, i.uv);
                case 49: return  _MainTex49.Sample(sampler_MainTex, i.uv);
                case 50: return  _MainTex50.Sample(sampler_MainTex, i.uv);
                case 51: return  _MainTex51.Sample(sampler_MainTex, i.uv);
                case 52: return  _MainTex52.Sample(sampler_MainTex, i.uv);
                case 53: return  _MainTex53.Sample(sampler_MainTex, i.uv);
                case 54: return  _MainTex54.Sample(sampler_MainTex, i.uv);
                case 55: return  _MainTex55.Sample(sampler_MainTex, i.uv);
                case 56: return  _MainTex56.Sample(sampler_MainTex, i.uv);
                case 57: return  _MainTex57.Sample(sampler_MainTex, i.uv);
                case 58: return  _MainTex58.Sample(sampler_MainTex, i.uv);
                case 59: return  _MainTex59.Sample(sampler_MainTex, i.uv);
                case 60: return  _MainTex60.Sample(sampler_MainTex, i.uv);
                case 61: return  _MainTex61.Sample(sampler_MainTex, i.uv);
                case 62: return  _MainTex62.Sample(sampler_MainTex, i.uv);
                case 63: return  _MainTex63.Sample(sampler_MainTex, i.uv);
                default: return 0;
                }
            }
            ENDCG
        }
    }
}
