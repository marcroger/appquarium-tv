// Appquarium — FishShadow (CG legacy, versión Cast device)
// Blob elíptico de sombra de contacto para los peces. Lo usa TvFishShadows.
//
// ⚠ POR QUÉ NO SE REUTILIZA Sprites/Default
// Fue el primer intento, precisamente por venir garantizado en el build. No pinta:
// con un material creado en runtime, el quad rojo OPACO, el renderer enabled=True,
// visible=True, dentro del viewport (0,70 · 0,04) y forzado a sortingOrder 500 y cola
// 4000 —o sea dibujado el último de todos— salían CERO píxeles en la captura. No era
// tapado ni orden: sencillamente no se dibujaba. Los tres shaders CG legacy propios de
// este proyecto sí pintan siempre, aquí y en el device, así que la sombra usa el mismo
// camino que ya está probado en vez de depender de un built-in que se comporta raro.
//
// ⚠ Sin tag LightMode: los pases "UniversalForward" NO se ejecutan en el renderer del
// Cast (Xiaomi WebGL/Chromium). Ver DecoLit.shader / FishUnlit.shader / PlanarShadow.shader.
//
// ⚠ Va en Graphics > Always Included Shaders. Sin eso el build de WebGL lo strippea
// (no lo referencia ningún material del proyecto, solo Shader.Find en runtime) y las
// sombras desaparecerían SOLO en la tele, funcionando en el Editor.

Shader "Appquarium/FishShadow"
{
    Properties
    {
        _MainTex     ("Elipse (alpha)", 2D)        = "white" {}
        _ShadowAlpha ("Shadow Alpha",   Range(0,1)) = 0.50
    }

    SubShader
    {
        // Misma cola que el suelo (Sprites/Default = 3000): dentro de la cola decide el
        // sortingOrder, y TvFishShadows usa 21 — el mismo hueco que la sombra de las decos,
        // por encima del occluder del suelo (20) y por debajo del fundido trasero (22).
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Blend  SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull   Off
            ZTest  LEqual

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float     _ShadowAlpha;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // La textura solo aporta la CAÍDA de la elipse en su alpha; el color lo pone
                // el shader (teal casi negro, igual que PlanarShadow) para que las dos sombras
                // de la escena —decos y peces— sean del mismo tono.
                fixed a = tex2D(_MainTex, i.uv).a * _ShadowAlpha;
                return fixed4(0.0, 0.015, 0.025, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
