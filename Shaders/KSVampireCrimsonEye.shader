// Lost Eye Shader - Désobfusqué
// Original par Neon#6653
Shader "Kawaii Studio/KSVampireCrimsonEye"
{
    Properties
    {
        [HideInInspector]_MainSettings("Main Settings Block", float) = 0
        [HideInInspector]_HueSettings("Hue Settings Block", float) = 0
        [HideInInspector]_RingSettings("Ring Settings Block", float) = 0
        [HideInInspector]_VignetteSettings("Vignette Settings Block", float) = 0

        [HideInInspector]_MainTex("Texture", 2D) = "white" {}

        [Toggle]_DistanceBasedParallaxScaling("Main Settings/DBPS", float) = 0
        _MainParallax("Main Settings/Parallax", Range(0, 1)) = 1
        [Vector2]_ParallaxCenter("Main Settings/Parallax Center", Vector) = (0.5, 0.5, 0, 0)
        [Toggle]_ParallaxMirror("Main Settings/Mirror for Right Eye", float) = 0
        [HDR]_MainColor("Main Settings/Color", Color) = (1,1,1,1)
        _SnakePupil("Main Settings/Snake Pupil", Range(0, 1)) = 1
        _PupilSmokeSize("Main Settings/Pupil Smoke Size", Range(0, 3)) = 0.5
        _PupilSmokeSpeed("Main Settings/Pupil Smoke Speed", Range(0, 0.6)) = 0.5
        _PupilSmokeColor("Main Settings/Pupil Smoke Color", Color) = (1,0,0,1)
        _PupilSize("Main Settings/Pupil Size", Range(0, 1)) = 0.5
        _PupilThickness("Main Settings/Pupil Thickness", Range(0, 1)) = 0.5

        _MainHueShift("Main Settings/Hue Settings/Hue Offset", Range(0, 1)) = 0
        _MainHueSpeed("Main Settings/Hue Settings/Hue Speed", Range(0, 2)) = 0

        [IntRange]_RingCount("Ring Settings/Count", Range(0, 20)) = 10
        _RingThickness("Ring Settings/Thickness", Range(0, 1)) = 0.1
        _RingSize("Ring Settings/Size", Range(0, 1)) = 1
        [Toggle]_LimbusMode("Ring Settings/Limbus Mode", Float) = 0

        _SurfaceVignette ("Vignette Settings/Surface Vignette", Range(0, 5)) = 1
        _ParallaxVignette ("Vignette Settings/Parallax Vignette", Range(0, 2)) = 0.2
        _SmokeSpeed("Smoke Speed", Range(0, 0.6)) = 0.5
        _SmokeFrequency("Smoke Frequency", Range(0.1, 20)) = 10
        [Enum(Heart,0,Sparkle,1,Diamond,2,Star,3)]_PupilShape ("Alternate Shape Selection", float) = 0
        _PupilShapeLerp ("Pupil Alternate Shape", range(0,1)) = 0
        _PupilVerticalOffset ("Pupil Vertical Offset", range(-3,3)) = 0
        [Vector2] _PupilAspectRatio ("Pupil Aspect Ratio", Vector) = (1,1,1,1)
        _PupilParallax ("Pupil Parallax", Range(-0.5, 1)) = 0.12
        _PupilSolidColor ("Pupil Solid Color", Color) = (0,0,0,1)
        _PupilSolidOpacity ("Pupil Solid Opacity", Range(0,1)) = 0.5
        _PupilShapeSize ("Pupil Shape Size", Range(0.05, 1)) = 0.5
        _PupilShapeSharpness ("Pupil Shape Sharpness", Range(0.01, 1)) = 0.25
        [HDR]_PupilGlowColor ("Pupil Glow Color", Color) = (1,0.3,0.1,1)
        _PupilGlowIntensity ("Pupil Glow Intensity", Range(0, 3)) = 0.5
        
        [Header(Sparkles)]
        [HDR]_SparkleColor ("Sparkles Glow Color", Color) = (1,1,1,1)
        _SparkleHueShift ("Sparkles Glow Hueshift", Range(0,1)) = 0
        _SparkleBrightness ("Sparkles Brightness", Float) = 1
        _SparkleSize ("Sparkles Size", Float) = 1
        _SparkleCount ("Sparkles Particle Number", Range(0, 130)) = 50
        _SparkleSpeed ("Sparkles Speed", Range(-10, 10)) = 1
        _SparkleTwinkle ("Sparkles Twinkle Speed", Range(0, 10)) = 1
        _SparkleFOV ("Sparkles FOV", Float) = 2
        _SparkleSeed ("Sparkles Seed", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "PreviewType"="Plane" }
        LOD 100

        Pass
        {
            AlphaTest Greater 0.5
            AlphaToMask On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.0

            #include "UnityCG.cginc"

            #define PI 3.14159265358979232
            #define glsl_mod(x,y) (((x)-(y)*floor((x)/(y))))

            float dot2(in float2 v) { return dot(v,v); }

            float hash(float n) { return frac(sin(n) * 1e4); }

            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 hsv2rgb_smooth(float3 c)
            {
                float3 rgb = clamp(abs(fmod(c.x * 6.0 + float3(0.0, 4.0, 2.0), 6.0) - 3.0) - 1.0, 0.0, 1.0);
                rgb = rgb * rgb * (3.0 - 2.0 * rgb); // cubic smoothing
                return c.z * lerp(float3(1.0, 1.0, 1.0), rgb, c.y);
            }

            float sdSparkle(float2 center, float2 UV, float2 Div, float2 Pows){
                return pow(abs((UV.x-center.x)/Div.x), Pows.x) + pow(abs((UV.y-center.y)/Div.x), Pows.x);
            }

            float sdStar5(float2 p, float r, float rf)
            {
                const float2 k1 = float2(0.809016994375, -0.587785252292);
                const float2 k2 = float2(-k1.x,k1.y);
                p.x = abs(p.x);
                p -= 2.0*max(dot(k1,p),0.0)*k1;
                p -= 2.0*max(dot(k2,p),0.0)*k2;
                p.x = abs(p.x);
                p.y -= r;
                float2 ba = rf*float2(-k1.y,k1.x) - float2(0,1);
                float h = clamp( dot(p,ba)/dot(ba,ba), 0.0, r );
                return length(p-ba*h) * sign(p.y*ba.x-p.x*ba.y);
            }

            float sdHeart( in float2 p )
            {
                p.x = abs(p.x);

                if( p.y+p.x>1.0 )
                    return sqrt(dot2(p-float2(0.25,0.75))) - sqrt(2.0)/4.0;
                return sqrt(min(dot2(p-float2(0.00,1.00)),
                                dot2(p-0.5*max(p.x+p.y,0.0)))) * sign(p.x-p.y);
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                float3 viewDir : TEXCOORD2;
                float dist : TEXCOORD3;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainColor;
            float _DistanceBasedParallaxScaling;
            float2 _ParallaxCenter;
            float _ParallaxMirror;
            float _MainHueShift;
            float _MainHueSpeed;
            float _MainParallax;
            float _PupilSize;
            float _PupilThickness;
            float _PupilSmokeSize;
            float _PupilSmokeSpeed;
            float4 _PupilSmokeColor;
            float _SnakePupil;
            int _RingCount;
            float _RingThickness;
            float _RingSize;
            float _LimbusMode;
            float _SurfaceVignette;
            float _ParallaxVignette;
            float _SmokeSpeed;
            float _SmokeFrequency;
            float _PupilShape;
            float _PupilShapeLerp;
            float _PupilVerticalOffset;
            float2 _PupilAspectRatio;
            float _PupilParallax;
            float4 _PupilSolidColor;
            float _PupilSolidOpacity;
            float _PupilShapeSize;
            float _PupilShapeSharpness;
            float4 _PupilGlowColor;
            float _PupilGlowIntensity;
            
            float4 _SparkleColor;
            float _SparkleHueShift;
            float _SparkleBrightness;
            float _SparkleSize;
            float _SparkleCount;
            float _SparkleSpeed;
            float _SparkleTwinkle;
            float _SparkleFOV;
            float _SparkleSeed;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.dist = distance(_WorldSpaceCameraPos, mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz);
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                float3 worldBinormal = cross(worldNormal, worldTangent) * v.tangent.w;
                float3x3 worldToTangent = float3x3(worldTangent, worldBinormal, worldNormal);
                o.viewDir = mul(worldToTangent, WorldSpaceViewDir(v.vertex));
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            float2 GenerateParallaxUV(float2 UVs, float3 viewDirection, float dist, float parallaxScale)
            {
                // Parallaxe simulée sans dépendre de l'angle de vue :
                // on converge simplement vers le centre pour donner un effet de profondeur constant.
                float2 plane = -UVs;
                UVs += plane * parallaxScale;
                return UVs;
            }
            
            // Parallax sans Distance Scaling - garde toujours l'effet de profondeur centré sur la pupille
            float2 GenerateParallaxUV_Depth(float2 UVs, float3 viewDirection, float parallaxScale, float mirrorX)
            {
                // Version "toujours centrée" : pas de dépendance au viewDir, mais une convergence vers la pupille.
                float2 plane = -UVs;
                if (mirrorX > 0.5)
                    plane.x = -plane.x;
                UVs += plane * parallaxScale;
                return UVs;
            }

            float2x2 Rot(float angle)
            {
                float s = sin(radians(angle));
                float c = cos(radians(angle));
                return float2x2(c, -s, s, c);
            }

            float rand(float2 uv)
            {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }

            float2 rand22(float2 uv)
            {
                return float2(rand(float2(uv.x, uv.x * 0.5)), rand(float2(uv.y, uv.y * 0.5)));
            }

            float PerlinNoise(float2 uv, float offset)
            {
                float2 id = floor(uv);
                float2 lv = frac(uv);
                float tl = rand(id) * 6.283;
                float tr = rand(id + float2(1, 0)) * 6.283;
                float bl = rand(id + float2(0, 1)) * 6.283;
                float br = rand(id + float2(1, 1)) * 6.283;
                float2 tlVec = float2(-sin(tl + offset), cos(tl + offset));
                float2 trVec = float2(-sin(tr + offset), cos(tr + offset));
                float2 blVec = float2(-sin(bl + offset), cos(bl + offset));
                float2 brVec = float2(-sin(br + offset), cos(br + offset));
                float tlDot = dot(tlVec, lv);
                float trDot = dot(trVec, lv - float2(1, 0));
                float blDot = dot(blVec, lv - float2(0, 1));
                float brDot = dot(brVec, lv - float2(1, 1));
                float2 cubic = lv * lv * (3.0 - 2.0 * lv);
                float topMix = lerp(tlDot, trDot, cubic.x);
                float bottomMix = lerp(blDot, brDot, cubic.x);
                float wholeMix = lerp(topMix, bottomMix, cubic.y) + 0.5;
                return wholeMix;
            }

            float fbm(float2 uv, int octaves, float offset)
            {
                float value = 0.0;
                float normalize_factor = 0.0;
                float scale = 0.5;
                for (int i = 0; i < octaves; i++)
                {
                    value += PerlinNoise(uv, offset) * scale;
                    normalize_factor += scale;
                    uv *= 2.0;
                    scale *= 0.5;
                }
                value /= normalize_factor;
                return value;
            }

            float3 hueShift(float3 col, float hueAdjust)
            {
                hueAdjust *= 2.0 * PI;
                const float3 k = float3(0.57735, 0.57735, 0.57735);
                float cosAngle = cos(hueAdjust);
                return col * cosAngle + cross(k, col) * sin(hueAdjust) + k * dot(k, col) * (1.0 - cosAngle);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = fixed4(0, 0, 0, 1);
                float3 finalPupilCol = 0.0;
                float inter = 20.0; // Plus de couches = meilleure qualité
                float _BackSmokeParallax = 0.25; // Plus de profondeur
                float _BackSmokeScale = _SmokeFrequency;
                float _AnimSpeed = saturate(_SmokeSpeed / 0.6);
                _AnimSpeed = max(_AnimSpeed, 0.0001);
                float _PupilAnim = saturate(_PupilSmokeSpeed / 0.6);
                _PupilAnim = max(_PupilAnim, 0.0001);

                // Pupil smoke layers - HAUTE QUALITÉ avec convergence vers le centre
                // Mirror : inverse le X du centre pour l'œil droit
                float2 parallaxCenterFinal = _ParallaxCenter;
                if (_ParallaxMirror > 0.5)
                    parallaxCenterFinal.x = 1.0 - _ParallaxCenter.x;
                float2 uvCentered = i.uv - parallaxCenterFinal; // Centre du parallax configurable
                for (int r = 0; r < (int)inter; r++)
                {
                    // Parallaxe progressive qui converge vers le centre - TOUJOURS avec effet de profondeur
                    float layerRatio = (float)r / inter;
                    float convergeFactor = _PupilParallax * (1.0 + layerRatio * 0.5); // Augmente la convergence en profondeur
                    // Utilise GenerateParallaxUV_Depth pour garder l'effet 3D centré sur la pupille
                    float2 tmpUVOri = GenerateParallaxUV_Depth(uvCentered, i.viewDir, _BackSmokeParallax + (r * convergeFactor) / inter, _ParallaxMirror);
                    
                    // Alternate Shape Calculation pour le smoke
                    float2 shapeUV = tmpUVOri / (_PupilSmokeSize * 2.0);
                    shapeUV.y -= _PupilVerticalOffset * 0.1;
                    shapeUV *= _PupilAspectRatio;
                    
                    float altDist = 0;
                    if (_PupilShape == 0) // Heart
                        altDist = sdHeart(float2(shapeUV.x, shapeUV.y + 0.3)); 
                    else if (_PupilShape == 1) // Sparkle
                        altDist = sdSparkle(float2(0,0), shapeUV, float2(1,1), float2(0.6,0.6)) - 0.5;
                    else if (_PupilShape == 2) // Diamond
                        altDist = sdSparkle(float2(0,0), shapeUV, float2(1,1), float2(1,1)) - 0.3;
                    else if (_PupilShape == 3) // Star
                        altDist = sdStar5(shapeUV, 0.5, 0.25);

                    float baseDistSnake = length((abs(tmpUVOri) + float2(0.1, 0)) * float2(1.111, 0.6) / (_PupilSmokeSize * 2.0));
                    float baseDistRound = length(tmpUVOri / (_PupilSmokeSize * 2.0));
                    float baseDist = lerp(baseDistRound, baseDistSnake, _SnakePupil);
                    
                    float finalDist = lerp(baseDist, altDist, _PupilShapeLerp);

                    // Mask avec transition plus douce
                    float pupilMask = smoothstep(0.3, 0.0, finalDist);
                    // Intensité qui augmente vers le centre pour l'effet de convergence
                    float depthIntensity = 1.0 + layerRatio * 0.3;
                    
                    // FBM haute qualité (12 octaves)
                    float smoke = 0.22 * depthIntensity / max(0.001, abs(fbm((tmpUVOri * _BackSmokeScale) + float2(
                        fbm(tmpUVOri * 6.0 + _Time.y * 2.0 * _PupilAnim, 3, 0.0),
                        fbm(tmpUVOri * 12.0 + _Time.y * 2.0 * _PupilAnim, 3, 0.0)
                    ) * 0.35, 12, _Time.y * 20.0 * _PupilAnim + r * 0.15) - 0.5)) * pupilMask;
                    
                    smoke = saturate(smoke);
                    smoke *= 1.0 / inter;
                    finalPupilCol += smoke * _PupilSmokeColor.rgb;
                }
                // Pupil outline
                float2 outlineUV = GenerateParallaxUV(uvCentered, i.viewDir, i.dist, -_BackSmokeParallax);
                
                // Alternate Shape for Outline
                float2 shapeUV_Out = outlineUV / (_PupilSize * 2.0); // Note: Uses _PupilSize here for outline
                shapeUV_Out.y -= _PupilVerticalOffset * 0.1;
                shapeUV_Out *= _PupilAspectRatio;
                
                float altDistOut = 0;
                if (_PupilShape == 0) // Heart
                    altDistOut = sdHeart(float2(shapeUV_Out.x, shapeUV_Out.y + 0.3)) + 0.2; 
                else if (_PupilShape == 1) // Sparkle
                    altDistOut = sdSparkle(float2(0,0), shapeUV_Out, float2(1,1), float2(0.6,0.6)) - 0.5 + 0.2;
                else if (_PupilShape == 2) // Diamond
                    altDistOut = sdSparkle(float2(0,0), shapeUV_Out, float2(1,1), float2(1,1)) - 0.3 + 0.2;
                else if (_PupilShape == 3) // Star
                    altDistOut = sdStar5(shapeUV_Out, 0.5, 0.25) + 0.2;

                float baseDistOutSnake = length((abs(outlineUV) + float2(0.1, 0)) * float2(1.111, 0.6) / (_PupilSize * 2.0));
                float baseDistOutRound = length(outlineUV / (_PupilSize * 2.0));
                float baseDistOut = lerp(baseDistOutRound, baseDistOutSnake, _SnakePupil);

                float finalDistOut = lerp(baseDistOut, altDistOut, _PupilShapeLerp);

                float outline = abs(finalDistOut - 0.2);
                float pixelWidthOut = fwidth(finalDistOut);
                float minOutline = max(0.0001, pixelWidthOut * 2.0);
                
                float smoke = 0.02 * _PupilThickness / abs(fbm(
                    mul(Rot(_Time.y * 50.0 * _AnimSpeed), (outlineUV * 5.0) + float2(
                        fbm(outlineUV * 5.0 + _Time.y * 2.0 * _AnimSpeed, 2, 0.0),
                        fbm(outlineUV * 10.0 + _Time.y * 2.0 * _AnimSpeed, 2, 0.0)
                    ) * 0.3),
                    10, _Time.y * _AnimSpeed) - 0.5);
                
                // Stabilisation du glow de l'outline
                col.rgb += clamp(smoke / max(outline, minOutline) * 0.001 / max(outline, minOutline), 0.0, 3.0);

                // Ring circles
                float finalCircleCol = 0.0;
                int circleCount = _RingCount;
                float circleSmoke = 0.02 * _RingThickness / abs(fbm(
                    mul(Rot(_Time.y * 50.0 * _AnimSpeed), (outlineUV * 5.0) + float2(
                        fbm(outlineUV * 5.0 + _Time.y * 2.0 * _AnimSpeed, 2, 0.0),
                        fbm(outlineUV * 10.0 + _Time.y * 2.0 * _AnimSpeed, 2, 0.0)
                    ) * 0.3),
                    10, _Time.y * _AnimSpeed) + 0.001);

                for (int e = 0; e < circleCount; e++)
                {
                    float2 circleUV = GenerateParallaxUV(uvCentered, i.viewDir, i.dist, -(0.25 / max(circleCount, 1) * e));
                    float2 offs = (rand22(float2(e + 1, e * e + 1)) - 0.5) * 0.1;
                    offs += float2(sin(_Time.y + e), cos(_Time.y + e)) * 0.02;
                    
                    if (_LimbusMode > 0.5)
                    {
                        // Mode Limbus (Bordure épaisse avec profondeur)
                        float dist = length(circleUV - offs);
                        // Anti-aliasing pour le bord net
                        float aa = fwidth(dist);
                        float border = smoothstep(0.6 * _RingSize - _RingThickness * 0.5 - aa, 0.6 * _RingSize - _RingThickness * 0.5 + aa, dist) 
                                     - smoothstep(0.6 * _RingSize - aa, 0.6 * _RingSize + aa, dist);
                        // On garde smoothstep simple si l'épaisseur est grande, mais si c'est fin ça aide.
                        // Simplification : smoothstep standard suffit souvent si Thickness n'est pas minuscule.
                        border = smoothstep(0.6 * _RingSize - _RingThickness * 0.5, 0.6 * _RingSize, dist);
                        
                        finalCircleCol += border * circleSmoke * 0.5;
                    }
                    else
                    {
                        // Mode Anneaux classique - Stabilisé
                        float circleDist = length(circleUV - offs);
                        float circleCol = abs(circleDist - 0.6 * _RingSize);
                        
                        // Anti-aliasing: empécher le dénominateur d'être trop petit par rapport à la taille d'un pixel
                        float pixelWidth = fwidth(circleDist);
                        float minWidth = max(0.001, pixelWidth * 2.0);
                        
                        finalCircleCol += circleSmoke / max(circleCol, minWidth);
                    }
                }
                col.rgb += clamp(finalCircleCol, 0.0, 2.0);

                // Pupil Solid Fill - COMPLÈTEMENT indépendant de la fumée ET de la distance
                // Utilise UV stable sans parallaxe pour que la forme reste visible de loin
                float2 stableUV = i.uv - parallaxCenterFinal; // UV centré configurable SANS parallaxe
                float2 shapeUV_Solid = stableUV / _PupilShapeSize; 
                shapeUV_Solid.y -= _PupilVerticalOffset * 0.1;
                shapeUV_Solid *= _PupilAspectRatio;
                
                float altDistSolid = 0;
                if (_PupilShape == 0) altDistSolid = sdHeart(float2(shapeUV_Solid.x, shapeUV_Solid.y + 0.3));
                else if (_PupilShape == 1) altDistSolid = sdSparkle(float2(0,0), shapeUV_Solid, float2(1,1), float2(0.6,0.6)) - 0.5;
                else if (_PupilShape == 2) altDistSolid = sdSparkle(float2(0,0), shapeUV_Solid, float2(1,1), float2(1,1)) - 0.3;
                else if (_PupilShape == 3) altDistSolid = sdStar5(shapeUV_Solid, 0.5, 0.25);
                
                float baseDistSolidSnake = length((abs(stableUV) + float2(0.1, 0)) * float2(1.111, 0.6) / _PupilShapeSize);
                float baseDistSolidRound = length(stableUV / _PupilShapeSize);
                float baseDistSolid = lerp(baseDistSolidRound, baseDistSolidSnake, _SnakePupil);
                
                float finalDistSolid = lerp(baseDistSolid, altDistSolid, _PupilShapeLerp);
                // Sharpness contrôlable pour une forme plus nette
                float solidMask = smoothstep(_PupilShapeSharpness, 0.0, finalDistSolid);
                // Glow autour de la forme (bords lumineux) - limité à une zone proche de la pupille
                float glowWidth = _PupilShapeSharpness * 1.5; // Glow plus serré
                float glowMask = smoothstep(glowWidth, _PupilShapeSharpness * 0.5, finalDistSolid);
                glowMask *= (1.0 - solidMask); // Pas de glow dans la zone solide
                glowMask = saturate(glowMask);
                
                // Color and glare
                col.rgb *= _MainColor.rgb;

                // Glare recentré sur la pupille
                float2 glare = uvCentered;
                col.rgb *= saturate(0.1 / length((glare - float2(0.0, 0.0)) * float2(0.4, 0.8)));
                // Réinjecte la fumée de pupille après le MainColor pour garder sa teinte indépendante
                col.rgb += finalPupilCol;
                
                // Pupil Glow - visible de loin (ajouté AVANT le solid pour qu'il soit autour)
                col.rgb += glowMask * _PupilGlowColor.rgb * _PupilGlowIntensity;
                
                // Pupil Shape appliquée EN DERNIER pour rester visible par-dessus la fumée
                col.rgb = lerp(col.rgb, _PupilSolidColor.rgb, solidMask * _PupilSolidOpacity);
                
                // Sparkles Logic (Cosmic Style)
                // Déclaration préalable de OffsetUV nécessaire pour les sparkles
                float2 OffsetUV = uvCentered * 2;
                // Parallax désactivée
                float2 parallax_base = float2(0, 0);
                
                // Sauvegarde UV
                float2 tuv = OffsetUV; 
                OffsetUV = uvCentered + 0.5; // espace 0..1 recentré sur la pupille pour les sparkles
                
                float stars = 1;
                float starCount = floor(_SparkleCount);
                starCount = min(starCount, 130);
                
                // Hueshift Sparkle Color
                float3 sparkleColHSV = rgb2hsv(_SparkleColor.rgb);
                float4 sparkleColorFinal = float4(hsv2rgb_smooth(float3(sparkleColHSV.r + _SparkleHueShift, sparkleColHSV.g, sparkleColHSV.b)), 1);
                
                for(int m=0; m < starCount; m++){
                    float speed = .1 + hash(cos(m + _SparkleSeed)) * (0.7 + 0.5 * cos(m / (200.0 * 0.25)));
                    
                    // Calcul position et animation
                    float2 ref = .5 + (float2(hash(m + _SparkleSeed) + 0.1 * cos(sin(m / starCount)), 
                                      glsl_mod(sin(m) - speed * (1.5 * 0.1 * _Time.y * _SparkleSpeed), 1)) * _SparkleFOV) - ((_SparkleFOV * .5));
                    
                    // Calcul de l'éclat
                    // Utilise sdSparkle (déjà définie)
                    // OffsetUV + parallax_base * ... -> Parallaxe des étoiles
                    // Cosmic utilise parallax_base * abs(sin(...)) pour donner des profondeurs variées
                    
                    stars *= float4(smoothstep(.1 * (.6 - m / (starCount)), 
                                    .25 + .07 * sin(m + _Time.y * m / starCount + (_SparkleTwinkle * _Time.y)), 
                                    sdSparkle(ref, OffsetUV + parallax_base * (abs(sin(m / starCount))), float2(_SparkleSize, _SparkleSize), float2(.6, .6))).xxxx);
                }
                stars = abs(stars) < .001 ? .001 : stars;
                
                // Add Sparkles to Color
                // Formule de mixage adaptée pour être additive sans teinter le fond
                // Quand pas d'étoile, stars = 1. La formule donne Brightness / 0.9.
                // On soustrait cette valeur de fond pour ne garder que les éclats.
                float backgroundGlow = _SparkleBrightness / 0.9;
                float currentGlow = _SparkleBrightness / max(0.001, abs(stars - 0.1));
                float finalGlow = max(0, currentGlow - backgroundGlow);
                
                float3 starGlow = finalGlow * sparkleColorFinal.rgb;
                
                // Masquer les étoiles par la pupille ?
                // Cosmic divise le tout. Ici on va ajouter.
                // Attention : stars est multiplicatif (commence à 1 et diminue là où il y a une étoile car sdSparkle augmente ?)
                // sdSparkle est distance. smoothstep(low, high, dist).
                // Si dist < low (dans l'étoile), smoothstep -> 0.
                // stars *= ... -> stars tend vers 0 dans les étoiles.
                // 1 / abs(stars - 0.1) -> Si stars est proche de 0.1, ça explose (glow).
                
                // On applique un masque pour que les étoiles ne soient pas sur la pupille solide si on veut, 
                // mais Cosmic les mélange globalement.
                // On va simplement ajouter le glow des étoiles.
                
                col.rgb += starGlow * 0.1; // Facteur 0.1 pour calmer l'intensité additive si besoin, à ajuster avec Brightness.
                
                OffsetUV = tuv; // Restaure UV
                
                // Vignette (Cosmic Style Parallax)
                // OffsetUV est déjà déclaré plus haut
                float c = length(OffsetUV);
                c = smoothstep(2, 0.0, c);
                float h = length(OffsetUV * _SurfaceVignette);
                h = smoothstep(1, .6, h);
                
                // Calcul du vecteur de parallaxe basé sur la vue (inversé comme dans Cosmic)
                // float2 parallax_base = -((i.viewDir.xy) / i.viewDir.z); // Déjà déclaré plus haut
                float h2 = length(OffsetUV + parallax_base * _ParallaxVignette * c);
                h2 = smoothstep(1, .6, h2);
                
                // Application de la vignette (assombrissement)
                col.rgb = lerp(float3(0,0,0), col.rgb, h * h2);

                // Gamma correction and hue shift
                col.rgb = pow(col.rgb, 2.2);
                col.rgb = hueShift(col.rgb, _MainHueShift + _Time.y * _MainHueSpeed);

                // Alpha (Opaque)
                col.a = 1.0;

                return col;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
    CustomEditor "KawaiiStudio.Shaders.Editor.KS_VampireEyeGUI"
}
