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
        _Ambient   ("Ambient", Range(0,1)) = 0.45
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
                float3 L    = normalize(float3(0.3, 1.0, -0.4));
                float  ndl  = saturate(dot(normalize(i.wnormal), L));
                float  lite = _Ambient + (1.0 - _Ambient) * ndl; // ambiente evita sombras negras
                fixed3 col  = tex.rgb * lite * _Brightness;
                return fixed4(col, 1.0); // forzar opaco
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}
