// プレイヤー（本実装）の狙いの帯（AOE）を描くシェーダー（URP用）。SweeperAimArea.cs が使う。
//
// 白い矢印の画像（Art/Sprites/AimLengthBody.png・AimLengthTip.png）に、チームの色（_Color）を掛けて描く。
// 帯の長さに合わせた繰り返しの回数は、SweeperAimArea がメッシュの UV に入れる（例：UV × 10）。
// _ScrollSpeed を 0 より大きくすると、矢印が前へ流れて見える（1秒に何枚ぶん流すか）。
Shader "StarSweepers/SweeperAimStrip"
{
    Properties
    {
        _MainTex("矢印の画像（白）", 2D) = "white" {}
        _Color("色", Color) = (1, 1, 1, 1)
        _ScrollSpeed("矢印が流れる速さ（1秒に何枚ぶん）", Float) = 0
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
            Name "SweeperAimStrip"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _ScrollSpeed;
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
                output.uv.x -= _Time.y * _ScrollSpeed;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                return texel * _Color;
            }
            ENDHLSL
        }
    }
}
