// FishUnlit — shader para fish pack (Global Reef Fish Pack) en WebGL Cast receiver.
//
// Por qué CG legacy y no URP HLSL:
// - El pass "LightMode"="UniversalForward" no se ejecuta en el renderer del Cast device
// - Sprites/Default (que sí funciona) no tiene LightMode tag
// - Los shaders CG sin LightMode ejecutan en cualquier renderer (SRPDefaultUnlit path)
//
// Por qué Cull Off:
// - Las normales del fish pack están invertidas — Cull Back elimina TODA la geometría
//
// Por qué sin clip() y alpha=1 forzado:
// - El alpha channel del body texture no es opaco en zonas que deberían serlo
// - Con clip() el body entero desaparece
Shader "Appquarium/FishUnlit"
{
    Properties
    {
        _MainTex   ("Texture", 2D)      = "white" {}
        _Color     ("Color", Color)     = (1,1,1,1)
        _Brightness("Brightness", Float) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" }
        Cull  Off
        ZWrite On
        ZTest  LEqual

        Pass
        {
            // Sin LightMode tag — ejecuta en cualquier SRP renderer (igual que Sprites/Default)
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;
            float     _Brightness;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                c.rgb *= _Brightness;
                return fixed4(c.rgb, 1.0); // forzar opaco
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}
