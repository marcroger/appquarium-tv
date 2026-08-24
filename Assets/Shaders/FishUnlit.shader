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

            // ⚠ 2026-08-24 — Los peces tampoco se enteraban del ciclo día/noche.
            // En el móvil van con `Universal Render Pipeline/Lit` (`FishSpawner.cs:306`), que
            // lee la escena y por tanto SÍ se apagan de noche. Aquí el shader es plano a
            // propósito (los peces son el protagonista y el device va justo), así que el
            // ciclo entra por el mismo global que en `DecoLit`, pero con SU PROPIO suelo:
            // los peces conservan más brillo que las decos para que la noche no los borre.
            //
            // Es un DARKEN, no un tint: un global que nadie publica vale 0, así que el
            // default deja el pez exactamente como siempre. Ver el comentario largo de
            // `DecoLit.shader` — el razonamiento es el mismo y no se repite aquí.
            float4    _AqFishDarken;

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
                c.rgb *= saturate(1.0 - _AqFishDarken.rgb);   // fase del ciclo (0 = día)
                return fixed4(c.rgb, 1.0); // forzar opaco
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}
