// FishFin — las aletas de los peces, con el MISMO tono y la MISMA niebla que su cuerpo.
//
// ⚠⚠ POR QUÉ EXISTE (2026-08-25). Las aletas del fish pack usan `Sprites/Default`, no
// `FishUnlit`. Se vio en el control extremo sobre la tele: con `fishDesat=1.0` los CUERPOS
// salian en escala de grises y las ALETAS seguian amarillas y azules fluorescentes. Ridiculo
// con valores altos, y sutilmente raro con los valores buenos.
//
// Es `Sprites/Default` clonado EXACTAMENTE (Blend One OneMinusSrcAlpha premultiplicado,
// ZWrite Off, Cull Off, Queue Transparent) mas los tres globales que ya usa `FishUnlit`.
// Se replica el blend al pie de la letra porque las aletas son planos con alpha y cualquier
// cambio de mezcla u orden las rompe.
//
// Lo asigna `FishSpawner.FixBundleShaders`, que ya recorre los materiales del pez: alli, un
// material que apunte a `Sprites/Default` es por definicion parte del pez, asi que el mapeo
// es exacto y no puede alcanzar al suelo ni al fondo (que tambien usan `Sprites/Default`
// pero no pasan por ese metodo).
//
// FallBack a `Sprites/Default`: si este shader faltara, las aletas vuelven a ser lo que eran
// en vez de salir magenta.
Shader "Appquarium/FishFin"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull   Off
        Lighting Off
        ZWrite Off
        Blend  One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float2 uv    : TEXCOORD0;
                float  wz    : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;

            // Exactamente los mismos globales que `FishUnlit`: si la aleta no recibiera el
            // mismo trato que el cuerpo volveriamos al problema que este shader viene a
            // arreglar. Todos default 0 = sin cambio.
            float4    _AqFishDarken;      // fase del ciclo dia/noche
            float4    _AqWaterFog;        // rgb = color del agua · a = densidad
            float4    _AqWaterFogRange;   // x = Z donde empieza · y = Z donde satura
            float     _AqFishDim;
            float     _AqFishDesat;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                o.wz    = mul(unity_ObjectToWorld, v.vertex).z;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;

                // Mismo orden que en `FishUnlit`: ciclo → tono → niebla. Si el orden difiere
                // entre cuerpo y aleta, los dos se separan de nuevo con valores altos.
                c.rgb *= saturate(1.0 - _AqFishDarken.rgb);

                float luma = dot(c.rgb, float3(0.2126, 0.7152, 0.0722));
                c.rgb = lerp(c.rgb, luma.xxx, saturate(_AqFishDesat));
                c.rgb *= saturate(1.0 - _AqFishDim);

                float den = _AqWaterFogRange.y - _AqWaterFogRange.x;
                float t   = saturate((i.wz - _AqWaterFogRange.x) / (abs(den) < 1e-4 ? 1e-4 : den));
                c.rgb = lerp(c.rgb, _AqWaterFog.rgb, saturate(t * _AqWaterFog.a));

                c.rgb *= c.a;   // premultiplicado, igual que Sprites/Default
                return c;
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}
