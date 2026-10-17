// The colour-vision aid on the UI (ColorVisionFilter): a UI Toolkit filter pass that runs every pixel of the panel
// through the daltonising matrix of Core ColorVision (rows in _RowR/_RowG/_RowB), then the high-contrast boost around
// mid grey (_Contrast). The matrix is linear in the colour and the contrast offset is scaled by alpha, so premultiplied
// pixels stay valid and fully transparent ones (the 3D world shows through there) stay untouched. UI Toolkit's own
// contrast() filter is not used: its shader is not part of player builds.
Shader "Hidden/Gooby/UIColorVision"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _RowR ("Row R", Vector) = (1, 0, 0, 0)
        _RowG ("Row G", Vector) = (0, 1, 0, 0)
        _RowB ("Row B", Vector) = (0, 0, 1, 0)
        _Contrast ("Contrast", Float) = 1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend One Zero
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUIEFilter.cginc"

            sampler2D _MainTex;
            float4 _RowR, _RowG, _RowB;
            float _Contrast;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(FilterVertexInput v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 c = tex2D(_MainTex, i.uv);
                half3 o = saturate(half3(dot(_RowR.xyz, c.rgb), dot(_RowG.xyz, c.rgb), dot(_RowB.xyz, c.rgb)));
                o = saturate(o * _Contrast + 0.5 * (1.0 - _Contrast) * c.a);
                return half4(o, c.a);
            }
            ENDCG
        }
    }
}
