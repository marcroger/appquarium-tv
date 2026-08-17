// DecoLit — shader para decoraciones GLB (corales, conchas, estatuas, columnas)
// en el WebGL Cast receiver.
//
// Por qué existe (igual razonamiento que FishUnlit):
// - Los shaders URP/HLSL y glTF/PbrMetallicRoughness usan pass "UniversalForward"
//   que NO se ejecuta en el renderer del Cast device → magenta/invisible.
// - Solo los shaders CG legacy SIN LightMode tag ejecutan (SRPDefaultUnlit path).
//
// Diferencia con FishUnlit:
// - FishUnlit es plano (sin luz) — vale para peces.
// - DecoLit añade un lambert N·L con una luz direccional FIJA + ambiente, para que
//   los corales/estatuas conserven relieve. La luz es fija (no depende del binding
//   de luces del SRP, que no corre en el Cast). Sin normal map (coste GPU: el device
//   va justo) — el relieve sale de la geometría del mesh, que en los GLB es densa.
//
// Cull Back (no Off): los GLB tienen winding correcto y el device va a 7fps — evitar overdraw.
Shader "Appquarium/DecoLit"
{
    Properties
    {
        _MainTex   ("Texture", 2D)       = "white" {}
        _Color     ("Color", Color)      = (1,1,1,1)
        _Brightness("Brightness", Float) = 1.0
        _Ambient   ("Ambient", Range(0,1)) = 0.32
        // ⚠ 2026-08-17 — SIN ESTA PROPIEDAD LA BIOLUMINISCENCIA ERA CÓDIGO MUERTO.
        // `DecorationPlacer` recoge los materiales de un coral filtrando por
        // `mat.HasProperty("_EmissionColor")` (línea ~413) y, como ningún shader del proyecto
        // la declaraba, la lista salía vacía. Y la luz puntual se crea DENTRO del
        // `if (mats.Count > 0)`, así que tampoco se creaba: cero efecto por dos vías.
        // Medido en la tele el 2026-08-17: el coral no variaba (−0,2 %) mientras el agua
        // caía un 42 % al pasar a noche. Ver la memoria `pending_biolum`.
        // Negro = sin cambio, así que es inocua para las 48 decos que no la usan.
        [HDR] _EmissionColor ("Emission Color (HDR)", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull  Back
        ZWrite On
        ZTest  LEqual

        Pass
        {
            // Sin LightMode tag — ejecuta en cualquier SRP renderer (igual que Sprites/Default / FishUnlit)
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float2 uv      : TEXCOORD0;
                float3 wnormal : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;
            float     _Brightness;
            float     _Ambient;
            // float4, no fixed4: la emisión llega en HDR (tintColor × 1,125 con los valores
            // por defecto), y fixed4 la recortaría a 1,0 justo donde empieza a notarse.
            float4    _EmissionColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos     = UnityObjectToClipPos(v.vertex);
                o.uv      = TRANSFORM_TEX(v.uv, _MainTex);
                o.wnormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv) * _Color;
                // Luz direccional fija (arriba-frente) — independiente del binding de luces del SRP.
                float3 N    = normalize(i.wnormal);
                float3 L    = normalize(float3(0.3, 1.0, -0.4));
                float  ndl  = saturate(dot(N, L));

                // ⚠ 2026-08-11 — AMBIENTE HEMISFÉRICO en vez de una constante.
                // Antes: lite = _Ambient + (1-_Ambient)*ndl con _Ambient=0,45. Ese suelo
                // plano de 0,45 lavaba las sombras propias y dejaba las decos mates: los
                // corales tienen ~100.000 triángulos de relieve real y no se les notaba.
                // Ahora el ambiente depende de hacia dónde mira la normal —más luz por
                // arriba (agua iluminada), menos por abajo (arena en sombra)—, que es como
                // se ilumina algo bajo el agua. Cuesta un lerp: gratis en GPU.
                float  hemi = saturate(N.y * 0.5 + 0.5);          // 1 arriba, 0 abajo
                float  amb  = _Ambient * lerp(0.5, 1.3, hemi);
                float  lite = saturate(amb + (1.0 - _Ambient) * ndl);
                float3 col  = tex.rgb * lite * _Brightness;

                // Emisión aditiva: se suma DESPUÉS de la iluminación a propósito, para que el
                // coral siga brillando aunque esté en la cara en sombra. `DecorationPlacer`
                // pone aquí tintColor × bioGlowIntensity × BioLumEmissionScale × strength,
                // con strength animado 0→1 por `FadeBioLum` al pasar a noche.
                col += _EmissionColor.rgb;

                return fixed4(col, 1.0); // forzar opaco
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}
