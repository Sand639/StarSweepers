// プレイヤー（本実装）の足元のリングを描くシェーダー（URP用）。SweeperRing.cs が使う。
//
// 床の少し上に置いた正方形の板に、**形を計算で描く**（画像を使わない）。
// _Shape で形を選ぶ：0 = 円、1 = 三角、2 = 四角、3 = 星、4 = 六角形。
// 形のふち（_Thickness の太さ）を濃く、内側を薄く（_FillAlpha）塗る。色は _Color。
//
// 板の大きさ・ふちの太さ・色は、SweeperRing が MaterialPropertyBlock で渡す。
// 下の Properties の値は、その予備（マテリアル単体で見たとき用）。
Shader "StarSweepers/SweeperRing"
{
    Properties
    {
        _Color("色", Color) = (0.3, 0.6, 0.95, 1)
        _Shape("形（0 円・1 三角・2 四角・3 星・4 六角形）", Float) = 0
        _Thickness("ふちの太さ（板の半分を1とした割合）", Range(0.01, 1)) = 0.16
        _FillAlpha("内側の濃さ", Range(0, 1)) = 0.15
        _Rotation("形を回す角度（度）", Float) = 0
        _StarInner("星のくぼみ（小さいほど細い星）", Range(0.2, 0.9)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SweeperRing"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Shape;
                float _Thickness;
                half _FillAlpha;
                float _Rotation;
                float _StarInner;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // 正多角形（頂点が真上）までの距離。中はマイナス
            float SdPolygon(float2 p, float r, float n)
            {
                float sector = 6.28318530718 / n;
                float a = atan2(p.x, p.y);
                float b = (floor(a / sector) + 0.5) * sector;
                return dot(p, float2(sin(b), cos(b))) - r * cos(3.14159265359 / n);
            }

            // 五芒星（先が真上）までの距離。中はマイナス（Inigo Quilez さんの式）
            float SdStar5(float2 p, float r, float rf)
            {
                const float2 k1 = float2(0.809016994375, -0.587785252292);
                const float2 k2 = float2(-k1.x, k1.y);
                p.x = abs(p.x);
                p -= 2.0 * max(dot(k1, p), 0.0) * k1;
                p -= 2.0 * max(dot(k2, p), 0.0) * k2;
                p.x = abs(p.x);
                p.y -= r;
                float2 ba = rf * float2(-k1.y, k1.x) - float2(0, 1);
                float h = clamp(dot(p, ba) / dot(ba, ba), 0.0, r);
                return length(p - ba * h) * sign(p.y * ba.x - p.x * ba.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 板の中心を 0、ふちを ±1 にする
                float2 p = input.uv * 2.0 - 1.0;

                float angle = radians(_Rotation);
                float s = sin(angle);
                float c = cos(angle);
                p = float2(c * p.x - s * p.y, s * p.x + c * p.y);

                float r = 0.97;
                float shape = round(_Shape);
                float d;
                if (shape < 0.5)      d = length(p) - r;
                else if (shape < 1.5) d = SdPolygon(p, r, 3.0);
                else if (shape < 2.5) d = SdPolygon(p, r, 4.0);
                else if (shape < 3.5) d = SdStar5(p, r, _StarInner);
                else                  d = SdPolygon(p, r, 6.0);

                // ふちの帯（形の外周から内側へ _Thickness の幅）
                float band = abs(d + _Thickness * 0.5) - _Thickness * 0.5;
                float aa = max(fwidth(d), 0.0001) * 1.5;
                float ring = 1.0 - smoothstep(-aa, aa, band);
                float inside = 1.0 - smoothstep(-aa, aa, d);

                half alpha = max(ring, inside * _FillAlpha) * _Color.a;
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
