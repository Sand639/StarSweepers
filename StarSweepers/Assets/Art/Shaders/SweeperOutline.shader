// 物資のフチを光らせる・物資を白く光らせるシェーダー（URP用）。SweeperItemGlow.cs が使う。（2026/11/4）
//
// 物資と同じ形を少しだけ大きくした写しに使う。光も影も付けず、_Color の色で塗るだけ。
// ・_Cull = 1（裏だけ描く）… 物資の後ろにはみ出した部分だけが見える＝**フチ取り**
// ・_Cull = 2（表だけ描く）… 物資の上にかぶさる＝**物資全体が光って見える**
//
// 色と濃さは SweeperItemGlow が MaterialPropertyBlock で渡す。
Shader "StarSweepers/SweeperOutline"
{
    Properties
    {
        _Color("色", Color) = (1, 0.82, 0.2, 1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("描く面（1 裏＝フチ・2 表＝全体）", Float) = 1
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
            Name "SweeperOutline"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
