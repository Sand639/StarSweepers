// つかんだ物資のまわりを、液体のリムライトで縁取るシェーダー（URP用）。
// LiquidWrap.cs が、物資の形（メッシュ）をそのままこのマテリアルで重ねて描く。物資のマテリアルは変えない。
//
// 2つの描き方を重ねている。
//
//   ふちの光（1つめのパス）… 物資の表面のうち、**カメラから見て横を向いている所ほど光る**（フレネル）。
//                            加算で描くので、リムライトのように縁が明るく浮かぶ
//   外側の膜（2つめのパス）… 物資を少しふくらませた形を**裏側だけ**描く。物資に隠れない外側だけが残るので、
//                            シルエットのまわりを液体の膜がぐるっと囲って見える（よくある「輪郭線」の作り方）
//
// どちらも、**くっついた所（_Contact）から _Reveal メートルの範囲だけ**描く。
// LiquidWrap が _Reveal を 0 から物資全体まで広げると、液体がくっついた所から広がって物資を包む。
//
// 物資ごとに変わる値（色・広がり具合など）は、LiquidWrap が MaterialPropertyBlock で毎フレーム渡す。
// 下の Properties の値は、その予備（マテリアル単体で見たとき用）。
Shader "StarSweepers/LiquidRim"
{
    Properties
    {
        [HDR] _Color("液体の色", Color) = (1, 0.6, 0.2, 1)
        _RimPower("ふちの細さ（大きいほど細い）", Range(0.5, 8)) = 2.5
        _RimStrength("ふちの強さ", Range(0, 4)) = 1.2
        _FillStrength("物資全体にかかる色の強さ", Range(0, 1)) = 0.06
        _OutlineWidth("外側の膜の厚さ（m）", Range(0, 0.3)) = 0.06
        _OutlineAlpha("外側の膜の濃さ", Range(0, 1)) = 0.8
        _ShellOffset("ふちの光を浮かせる距離（m）", Range(0, 0.05)) = 0.005
        _FlowSpeed("液体の模様が流れる速さ", Float) = 2
        _FlowScale("液体の模様の細かさ", Float) = 4
        _Wobble("膜の厚さのゆれ", Range(0, 1)) = 0.35
        _EdgeSoftness("広がる先のぼかし（m）", Float) = 0.25
        _FrontGlow("広がる先の明るさ", Float) = 1.5

        // LiquidWrap が毎フレーム入れる
        _Contact("くっついた所（ワールド座標）", Vector) = (0, 0, 0, 0)
        _Center("物資の中心（ワールド座標）", Vector) = (0, 0, 0, 0)
        _Reveal("広がった距離（m）", Float) = 100
        _Fade("濃さの倍率", Float) = 1
        _Expand("ふくらませる量（m）", Float) = 0
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _RimPower;
            half _RimStrength;
            half _FillStrength;
            float _OutlineWidth;
            half _OutlineAlpha;
            float _ShellOffset;
            float _FlowSpeed;
            float _FlowScale;
            half _Wobble;
            float _EdgeSoftness;
            half _FrontGlow;
            float4 _Contact;
            float4 _Center;
            float _Reveal;
            half _Fade;
            float _Expand;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
        };

        struct Varyings
        {
            float4 positionHCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
        };

        // 物資の中心から外へ押し出す。法線で押すと、箱の角で面がばらばらに離れてすき間ができるため
        float3 PushOut(float3 positionWS, float amount)
        {
            float3 offset = positionWS - _Center.xyz;
            float len = length(offset);
            float3 dir = len > 1e-4 ? offset / len : float3(0, 1, 0);
            return positionWS + dir * amount;
        }

        // 液体の模様（0〜1）。3つの波を重ねて、ゆっくり流す
        half Flow(float3 positionWS)
        {
            float3 p = positionWS * _FlowScale;
            float t = _Time.y * _FlowSpeed;
            half f = sin(p.x + p.y * 1.7 + t)
                   + sin(p.y * 1.3 - p.z * 1.1 - t * 1.3)
                   + sin(p.z * 1.9 + p.x * 0.7 + t * 0.7);
            return f / 6.0 + 0.5;
        }

        // くっついた所から広がった範囲か（0〜1）。front は広がる先のふち（そこだけ明るくする）
        half Reveal(float3 positionWS, out half front)
        {
            float d = distance(positionWS, _Contact.xyz);

            // 広がる先を、液体らしく波打たせる
            float n = sin(positionWS.x * 7.0 + _Time.y * 3.0) * sin(positionWS.y * 6.0 - _Time.y * 2.3) * sin(positionWS.z * 5.0 + 1.3);
            float r = _Reveal + n * _EdgeSoftness * 0.5;
            float soft = max(_EdgeSoftness, 1e-3);

            front = 1.0 - smoothstep(0.0, soft, abs(d - r));
            return 1.0 - smoothstep(r - soft, r, d);
        }

        Varyings MakeVaryings(Attributes input, float push)
        {
            Varyings output;
            float3 positionWS = PushOut(TransformObjectToWorld(input.positionOS.xyz), push);
            output.positionWS = positionWS;
            output.positionHCS = TransformWorldToHClip(positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return output;
        }
        ENDHLSL

        // ---- 1つめ：ふちの光（加算） ----
        Pass
        {
            Name "Rim"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            Varyings vert(Attributes input)
            {
                return MakeVaryings(input, _ShellOffset + _Expand);
            }

            half4 frag(Varyings input) : SV_Target
            {
                half front;
                half mask = Reveal(input.positionWS, front);

                float3 normalWS = normalize(input.normalWS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                half fresnel = 1.0 - saturate(dot(normalWS, viewWS));

                half flow = Flow(input.positionWS);
                half rim = pow(fresnel, _RimPower * lerp(0.7, 1.3, flow)) * _RimStrength;

                half strength = (rim + _FillStrength + front * _FrontGlow * 0.5) * mask * _Fade;
                return half4(_Color.rgb * strength, 1.0);
            }
            ENDHLSL
        }

        // ---- 2つめ：外側の膜（ふくらませた形の裏側だけ。物資に隠れない外側だけが残る） ----
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            Varyings vert(Attributes input)
            {
                // 膜の厚さを、場所と時間でぷよぷよ変える
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float wave = sin(dot(positionWS, float3(3.1, 2.3, 1.7)) * 1.5 + _Time.y * _FlowSpeed * 1.7);
                float width = _OutlineWidth * (1.0 + _Wobble * wave);

                return MakeVaryings(input, width + _Expand);
            }

            half4 frag(Varyings input) : SV_Target
            {
                half front;
                half mask = Reveal(input.positionWS, front);
                half flow = Flow(input.positionWS);

                half3 color = _Color.rgb * lerp(0.8, 1.3, flow) * (1.0 + front * _FrontGlow);
                half alpha = _OutlineAlpha * lerp(0.75, 1.0, flow) * mask * _Fade;
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
