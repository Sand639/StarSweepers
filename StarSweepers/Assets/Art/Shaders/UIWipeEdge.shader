// ラウンド勝利の転場（RoundWinWipe.cs）で使う、純色の帯のUIシェーダー。
//
// 四角い画像（Image）を画面いっぱいに置いて、**画像自体は動かさない。**
// 代わりに RoundWinWipe が毎フレーム「境界の位置（_Boundary）」と「どちら側を塗るか（_CoverSign）」を入れるので、
// それを見てピクセルごとに「塗る／塗らない」を決める。これで縁の形を自由に変えられる。
//
// ## 縁の形（_EdgeStyle）
//
// | 値 | 見た目 |
// | --- | --- |
// | 0 | まっすぐ（ふつうのワイプ） |
// | 1 | **水波** … 上下にゆっくり進む2つの波＋弱いノイズ。縁がなめらかにうねる |
// | 2 | **炎** … 『Balatro』のスコア盤の上に乗っている炎のような、**とがった火舌の3枚重ね**。
// |   | 外側＝帯の色、真ん中＝_FlameMidColor、芯＝_FlameCoreColor。3枚が別々にちらつく |
//
// 炎の火舌は**帯の進む向きへ突き出る**（左→右の転場なら右向きの火の壁になる）。
// とがった形は三角波（abs(frac(x)*2-1)）を3つ重ねて作っているので、ノイズのようなぼやけではなく
// カートゥーン調のくっきりした舌になる。
//
// ## 火星（_SparkAmount）
//
// **帯が去った側**（塗られていない側）の、境界の近くだけにちらばる光の点。
// _SparkAmount が 0 なら何も出ない。RoundWinWipe が退場のあいだだけ 0→1 に上げる。
// パーティクルではなくシェーダーで描いているのは、**Overlay の Canvas の下にパーティクルが隠れてしまう**ため。
//
// ※ 混ぜかた（Blend）は UI/Default と同じ「掛け合わせ済み」の形にしてあるので、
//   半透明の UI の上に重ねても色が二重に暗くならない。
Shader "StarSweepers/UIWipeEdge"
{
    Properties
    {
        [PerRendererData] _MainTex("画像（無ければ白）", 2D) = "white" {}
        _Color("色", Color) = (1, 1, 1, 1)

        [Header(Wipe)]
        _Boundary("境界の位置（横 0〜1。-0.2 なら何も塗らない）", Float) = 0.5
        _CoverSign("塗る側（+1＝境界より左 / -1＝境界より右）", Float) = 1
        _Softness("境界のぼかし（横 0〜1 に対する幅）", Float) = 0.012
        _EdgeStyle("縁の形（0＝まっすぐ / 1＝水波 / 2＝炎）", Float) = 2
        _ScreenAspect("画面の縦横比（スクリプトが入れる）", Float) = 1.777

        [Header(Wave)]
        _EdgeAmplitude("水波のうねりの大きさ", Float) = 0.06
        _EdgeDetail("水波のうねりの細かさ", Float) = 8
        _EdgeSpeed("水波のうねりの速さ", Float) = 1.5

        [Header(Flame)]
        _FlameHeight("火舌の長さ（横 0〜1 に対する割合）", Float) = 0.10
        _FlameDetail("火舌の本数の細かさ（縦に何本並ぶか）", Float) = 6
        _FlameSpeed("火舌のちらつく速さ", Float) = 2.2
        _FlameMidScale("真ん中の層の大きさ（外側の何割か）", Float) = 0.62
        _FlameCoreScale("芯の層の大きさ（外側の何割か）", Float) = 0.32
        _FlameMidColor("炎の真ん中の層の色", Color) = (1, 0.75, 0.2, 1)
        _FlameCoreColor("炎の芯の色", Color) = (1, 0.95, 0.75, 1)

        [Header(Rim)]
        _RimColor("縁の光の色", Color) = (1, 0.85, 0.5, 1)
        _RimWidth("縁の光の幅", Float) = 0.03
        _RimStrength("縁の光の強さ（0 で光らない）", Float) = 0

        [Header(Spark)]
        _SparkColor("火星の色", Color) = (1, 0.7, 0.3, 1)
        _SparkAmount("火星の量（0 で無し）", Float) = 0
        _SparkDensity("火星の細かさ", Float) = 24
        _SparkSpread("火星が散る距離", Float) = 0.18
        _SparkSpeed("火星の速さ", Float) = 2
        _SparkThreshold("火星の少なさ（小さいほど多い）", Float) = 0.55

        [Header(Stencil)]
        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref[_Stencil]
            Comp[_StencilComp]
            Pass[_StencilOp]
            ReadMask[_StencilReadMask]
            WriteMask[_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest[unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask[_ColorMask]

        Pass
        {
            Name "UIWipeEdge"

        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            float _Boundary;
            float _CoverSign;
            float _Softness;
            float _EdgeStyle;
            float _ScreenAspect;

            float _EdgeAmplitude;
            float _EdgeDetail;
            float _EdgeSpeed;

            float _FlameHeight;
            float _FlameDetail;
            float _FlameSpeed;
            float _FlameMidScale;
            float _FlameCoreScale;
            fixed4 _FlameMidColor;
            fixed4 _FlameCoreColor;

            fixed4 _RimColor;
            float _RimWidth;
            float _RimStrength;

            fixed4 _SparkColor;
            float _SparkAmount;
            float _SparkDensity;
            float _SparkSpread;
            float _SparkSpeed;
            float _SparkThreshold;

            // ------------------------------------------------------------
            // ノイズ
            // ------------------------------------------------------------

            /// 2つの数から決まる、0〜1 のバラバラな値（同じ入力なら必ず同じ結果）。
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            /// 格子点のバラバラな値をなめらかにつないだノイズ（0〜1）。
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // ------------------------------------------------------------
            // 縁の形
            // ------------------------------------------------------------

            /// 水波のうねり（-1〜1 くらい）。縦（uv.y）を基準に、逆向きに進む2つの波＋弱いノイズ。
            float WaveOffset(float2 uv, float t)
            {
                float y = uv.y * _EdgeDetail;
                float x = uv.x * _EdgeDetail * max(0.05, _ScreenAspect);

                float w = sin(y + t * _EdgeSpeed) * 0.55;
                w += sin(y * 2.7 - t * _EdgeSpeed * 1.37) * 0.28;
                w += (ValueNoise(float2(x * 0.5, y * 0.5) + t * _EdgeSpeed * 0.25) - 0.5) * 0.45;
                return w;
            }

            ///
            /// **とがった火舌の形（0〜1）。** 『Balatro』の炎のような、くっきりとがった舌。
            ///
            /// 三角波（abs(frac(x)*2-1)）を、細かさと進む速さを変えて3つ重ねる。
            /// 三角波は山がとがっているので、ノイズのようなぼやけた縁ではなくカートゥーン調の舌になる。
            /// 最後にゆっくりしたノイズを掛けて、舌の高さがそろいすぎないようにする。
            ///
            float Tongue(float v, float t, float seed)
            {
                float a = abs(frac(v + t * 0.31 + seed) * 2.0 - 1.0);
                float b = abs(frac(v * 2.17 - t * 0.47 + seed * 1.31) * 2.0 - 1.0);
                float c = abs(frac(v * 4.31 + t * 0.63 + seed * 2.17) * 2.0 - 1.0);

                float m = max(a, b * 0.85);
                m = max(m, c * 0.6);

                // 舌ごとの高さの違い（縦になめらかに変わる）
                float h = 0.55 + 0.45 * ValueNoise(float2(seed * 17.3 + 3.7, v * 0.5 - t * 0.15));
                return m * h;
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition.xyz);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float t = _Time.y;

                // 帯の本体の縁（まっすぐ or 水波）。炎のときはここから火舌が突き出る
                float edge = _Boundary;
                if (_EdgeStyle > 0.5 && _EdgeStyle < 1.5)
                {
                    edge += WaveOffset(uv, t) * _EdgeAmplitude;
                }

                // d > 0 が塗られていない側（帯の進行方向の先）、d < 0 が塗られている側
                float d = (uv.x - edge) * _CoverSign;

                // 帯の本体
                float bodyAlpha = 1.0 - smoothstep(-_Softness, _Softness, d);

                // ------------------------------------------------------------
                // 炎（3枚重ねのとがった火舌）
                // ------------------------------------------------------------
                float3 flameColor = float3(0.0, 0.0, 0.0);
                float flameAlpha = 0.0;

                if (_EdgeStyle > 1.5)
                {
                    // 帯の縁より先（進行方向）に出ている距離
                    float f = max(0.0, d);
                    float v = uv.y * max(0.5, _FlameDetail);
                    float ft = t * max(0.0, _FlameSpeed);

                    // 3枚の火舌。別々の種と速さでちらつかせ、必ず「外＞中＞芯」の順に収まるように切り詰める
                    float tipOuter = Tongue(v, ft, 0.0) * _FlameHeight;
                    float tipMid = min(Tongue(v + 0.37, ft * 1.23, 5.1) * _FlameHeight * _FlameMidScale, tipOuter * 0.9);
                    float tipCore = min(Tongue(v + 0.71, ft * 1.47, 11.7) * _FlameHeight * _FlameCoreScale, tipMid * 0.8);

                    // 境目を1ピクセルぶんだけぼかして、ギザギザのちらつきを抑える
                    float soft = max(0.0008, _Softness * 0.5);
                    float mOuter = 1.0 - smoothstep(tipOuter - soft, tipOuter + soft, f);
                    float mMid = 1.0 - smoothstep(tipMid - soft, tipMid + soft, f);
                    float mCore = 1.0 - smoothstep(tipCore - soft, tipCore + soft, f);

                    // **帯の本体の中（d < 0）では炎を出さない。**
                    // f は本体の中で 0 になるので、そのままでは本体ぜんぶが「炎の内側」になってしまう。
                    // 本体の縁で 0→1 になる門をかけて、本体が消えると同時に炎が出るようにする
                    float gate = smoothstep(-_Softness, _Softness, d);
                    mOuter *= gate;
                    mMid *= gate;
                    mCore *= gate;

                    // 外側＝帯の色（本体とつながって見える）、真ん中＝明るい色、芯＝いちばん明るい色
                    flameColor = IN.color.rgb * (mOuter - mMid)
                               + _FlameMidColor.rgb * (mMid - mCore)
                               + _FlameCoreColor.rgb * mCore;
                    flameAlpha = mOuter;
                }

                // ------------------------------------------------------------
                // 火星（帯が去った側の、縁の近くだけにちらばる）
                // ------------------------------------------------------------
                float ember = 0.0;
                if (_SparkAmount > 0.001 && d > 0.0)
                {
                    float2 p = float2(d * _SparkDensity, uv.y * _SparkDensity - t * _SparkSpeed);
                    float n = ValueNoise(p) * 0.6 + ValueNoise(p * 2.7 + 11.3) * 0.4;
                    ember = smoothstep(_SparkThreshold, _SparkThreshold + 0.22, n);
                    ember *= exp(-d / max(0.001, _SparkSpread));
                    ember *= _SparkAmount;
                }

                // ------------------------------------------------------------
                // 縁の光（ふだんは 0。水波の波頭の白さなどに使う）
                // ------------------------------------------------------------
                float rim = 0.0;
                if (_RimStrength > 0.001)
                {
                    rim = exp(-abs(d) / max(0.0005, _RimWidth)) * _RimStrength;
                }

                // Image の色（＝チームの色）と、全体の半透明さを掛ける
                half4 tex = tex2D(_MainTex, uv) + _TextureSampleAdd;
                float va = IN.color.a;

                float body = bodyAlpha * va;
                float3 rgb = IN.color.rgb * tex.rgb * body;
                rgb += flameColor * (flameAlpha * va);
                rgb += _SparkColor.rgb * (ember * va);
                rgb += _RimColor.rgb * (rim * va);

                // Mask などで切り抜かれた部分を落とす
                float mask = 1.0;
                #ifdef UNITY_UI_CLIP_RECT
                mask = UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                float a = saturate(body + flameAlpha * va + ember * va + rim * va) * mask;

                #ifdef UNITY_UI_ALPHACLIP
                clip(a - 0.001);
                #endif

                // Blend One OneMinusSrcAlpha なので、すでに掛けたままの rgb をそのまま返す
                return fixed4(rgb * mask, a);
            }
        ENDCG
        }
    }
}
