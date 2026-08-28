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
                float  wz  : TEXCOORD1;   // Z del mundo, para la niebla de agua
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

            // ── AGUA: niebla por profundidad + ajuste de tono (2026-08-25) ──────────
            // Medido en la tele: los peces van a croma C* 42,6 contra 23,1 del agua que los
            // rodea (1,8x) y L* 59 contra 47. Las DECOS, en cambio, estan integradas (25,5).
            // Es decir: el problema de "esto parece un collage" son los peces, no el decorado.
            //
            // Causa: este shader es textura x _Brightness (2.0) y nada mas. Ningun shader del
            // proyecto lee la profundidad, asi que un pez del fondo tiene exactamente el mismo
            // contraste y saturacion que uno pegado al cristal. Bajo el agua eso no pasa nunca.
            //
            // ⚠ La camara es ORTOGRAFICA, asi que la distancia a la camara no sirve como
            // profundidad. Se usa la Z DEL MUNDO, que es lo que define la escena 2.5D:
            // ZFront=-1,0 · decos hasta +3,0 · suelo hasta +4,2 · fondo en +5,0.
            //
            // ⚠⚠ TODOS LOS DEFAULTS SON 0 = SIN CAMBIO, y es deliberado: un global que nadie
            // publica vale 0, nunca 1. Con densidad 0 la imagen es EXACTAMENTE la de siempre,
            // asi que este cambio no puede provocar una regresion visual por construccion —
            // el mismo criterio que ya se uso para el darken del ciclo dia/noche.
            float4    _AqWaterFog;        // rgb = color del agua · a = densidad (0 = apagado)
            float4    _AqWaterFogRange;   // x = Z donde empieza · y = Z donde satura
            float     _AqFishDim;         // 0 = sin cambio · 1 = negro   (baja el L*)
            float     _AqFishDesat;       // 0 = sin cambio · 1 = gris    (baja el croma)

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                o.wz  = mul(unity_ObjectToWorld, v.vertex).z;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                c.rgb *= _Brightness;
                c.rgb *= saturate(1.0 - _AqFishDarken.rgb);   // fase del ciclo (0 = día)

                // Tono: bajar croma y brillo acerca el pez al agua que lo rodea.
                float luma = dot(c.rgb, float3(0.2126, 0.7152, 0.0722));
                c.rgb = lerp(c.rgb, luma.xxx, saturate(_AqFishDesat));
                c.rgb *= saturate(1.0 - _AqFishDim);

                // Niebla de agua: cuanto mas al fondo, mas se funde con el color del agua.
                float den = _AqWaterFogRange.y - _AqWaterFogRange.x;
                float t   = saturate((i.wz - _AqWaterFogRange.x) / (abs(den) < 1e-4 ? 1e-4 : den));
                c.rgb = lerp(c.rgb, _AqWaterFog.rgb, saturate(t * _AqWaterFog.a));

                return fixed4(c.rgb, 1.0); // forzar opaco
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}
