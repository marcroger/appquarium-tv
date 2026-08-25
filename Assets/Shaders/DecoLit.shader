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
                float  wz      : TEXCOORD2;   // Z del mundo, para la niebla de agua
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;
            float     _Brightness;
            float     _Ambient;
            // float4, no fixed4: la emisión llega en HDR (tintColor × 1,125 con los valores
            // por defecto), y fixed4 la recortaría a 1,0 justo donde empieza a notarse.
            float4    _EmissionColor;

            // ⚠ 2026-08-24 — EL CICLO DÍA/NOCHE NO LLEGABA A LAS DECOS.
            // Medido: suelo y decos daban 92,09 y 47,98 de luminancia en los OCHO momentos
            // del ciclo, idénticos a dos decimales, mientras el fondo caía a 0,45× y el agua
            // a 0,23×. La causa es la luz fija de más abajo: ni `RenderSettings.ambientLight`
            // ni la intensidad del `Light` direccional —que es lo que anima
            // `AmbientModeController`— tenían ningún camino hasta aquí. En el móvil las decos
            // van con `Universal Render Pipeline/Lit`, que sí lee la escena: de ahí el
            // desajuste que reportó el user.
            //
            // Se resuelve con un global que publica `AmbientModeController` cada frame de la
            // transición, en vez de leer los globals de luz del SRP: en un pass CG sin
            // LightMode (que es lo que somos) no hay garantía de que el pipeline los tenga
            // bindeados, y este proyecto ya ha pagado caro depender de cosas que "deberían"
            // estar puestas.
            //
            // ⚠⚠ ES UN *DARKEN*, NO UN *TINT*, Y ESO ES DELIBERADO: un global que nadie
            // publica vale 0, no 1. Con un tint, cualquier escena sin `AmbientModeController`
            // (o un fallo de orden de inicialización) renderizaría las decos EN NEGRO. Con
            // darken, el valor por defecto 0 significa "no toques nada" y el aspecto es
            // exactamente el de siempre. Fallar hacia lo de antes, no hacia lo roto.
            float4    _AqDecoDarken;

            // ── AGUA: la misma niebla que los peces (2026-08-25) ────────────────────
            // Las decos ya estan razonablemente integradas (croma C* 25,5 contra 23,1 del
            // agua), asi que aqui la niebla aporta poco color y mucha PROFUNDIDAD: sin ella,
            // una deco al fondo tiene el mismo contraste que una pegada al cristal.
            // Va con los MISMOS globales que `FishUnlit` a proposito: si decos y peces no
            // comparten el medio, se vuelve a ver el collage que se intenta quitar.
            // Default 0 = sin cambio. Ver el bloque largo de `FishUnlit.shader`.
            float4    _AqWaterFog;        // rgb = color del agua · a = densidad (0 = apagado)
            float4    _AqWaterFogRange;   // x = Z donde empieza · y = Z donde satura

            // ⚠⚠ LAS DECOS LLEVAN SU PROPIO MULTIPLICADOR, Y ARRANCA EN 0 (2026-08-25).
            // Decision del user viendo las capturas en la tele: con la niebla completa las
            // decos "pierden demasiado" — un ancla negra salia turquesa y una estrella azul
            // marino salia celeste.
            //
            // El primer intento fue acortar el rango de Z para que la niebla empezara por
            // DETRAS de las decos. No vale, y lo cazo el user: las decos se colocan en
            // cualquier Z hasta ZDecoBack=+3,0, asi que una puesta al fondo volveria a
            // comerse la niebla. Un corte por profundidad no puede proteger algo que se
            // mueve en profundidad.
            //
            // Con un multiplicador propio el suelo puede fundir su juntura con el fondo y los
            // peces pueden ganar profundidad, mientras las decos conservan su color EXACTO
            // esten donde esten. Y sigue siendo ajustable en caliente por el mensaje FOG
            // (`decoFog`) si algun dia se quiere un punto de niebla en ellas.
            float     _AqDecoFogMul;      // 0 = las decos NO reciben niebla · 1 = como los peces

            v2f vert(appdata v)
            {
                v2f o;
                o.pos     = UnityObjectToClipPos(v.vertex);
                o.uv      = TRANSFORM_TEX(v.uv, _MainTex);
                o.wnormal = UnityObjectToWorldNormal(v.normal);
                o.wz      = mul(unity_ObjectToWorld, v.vertex).z;
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

                // El ciclo día/noche entra AQUÍ y sólo aquí: se conserva intacta la fórmula
                // de iluminación validada (relieve, hemisférico, sombras propias) y se
                // multiplica por el color de la fase. En día el global vale 0 → factor
                // (1,1,1) → la imagen es EXACTAMENTE la de antes, bit a bit; el riesgo de
                // regresión sobre el aspecto ya validado es nulo por construcción.
                float3 fase = saturate(1.0 - _AqDecoDarken.rgb);
                float3 col  = tex.rgb * lite * _Brightness * fase;

                // Emisión aditiva: se suma DESPUÉS de la iluminación a propósito, para que el
                // coral siga brillando aunque esté en la cara en sombra. `DecorationPlacer`
                // pone aquí tintColor × bioGlowIntensity × BioLumEmissionScale × strength,
                // con strength animado 0→1 por `FadeBioLum` al pasar a noche.
                col += _EmissionColor.rgb;

                // Niebla de agua. Se aplica DESPUES de la emision a proposito: un coral
                // bioluminiscente al fondo tambien tiene agua delante, y si la emision se
                // saltara la niebla volveria a despegarse de la escena.
                float den = _AqWaterFogRange.y - _AqWaterFogRange.x;
                float t   = saturate((i.wz - _AqWaterFogRange.x) / (abs(den) < 1e-4 ? 1e-4 : den));
                col = lerp(col, _AqWaterFog.rgb, saturate(t * _AqWaterFog.a * _AqDecoFogMul));

                return fixed4(col, 1.0); // forzar opaco
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}
