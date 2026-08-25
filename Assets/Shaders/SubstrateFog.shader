// SubstrateFog — el suelo del tanque, con la MISMA niebla de agua que decos y peces.
//
// ⚠⚠ POR QUÉ EXISTE (2026-08-25). El primer intento de niebla la metió en `DecoLit` y
// `FishUnlit` pero NO en el suelo, porque el suelo usa `Sprites/Default`, que es de Unity y
// no se puede tocar. Resultado medido en la tele: las decos salían teñidas de turquesa
// mientras la arena sobre la que se apoyan seguía blanca y brillante. Es decir, las decos
// parecían estar bajo el agua y el suelo no — se cambió una incoherencia por otra peor.
//
// La arena es la superficie más grande del encuadre, así que o entra en el mismo medio que
// todo lo demás o la niebla no sirve de nada.
//
// De regalo arregla la juntura suelo/fondo, que era el otro problema medido: el fondo cae a
// luminancia 1,9-10,6 justo donde el suelo arranca en 56 y sube a 100,9 (un salto de x12 a
// x30 en 40 píxeles). Como el suelo llega hasta Z=+4,2 y la niebla satura ahí, el borde de
// atrás del suelo se funde con el color del agua en vez de cortarse a cuchillo.
//
// Por qué CG legacy y sin LightMode: lo mismo que `DecoLit` y `FishUnlit` — los pases con
// `LightMode` no ejecutan en el renderer del Cast device. Ver `tv_shaders_cg_legacy_obligatorio`.
//
// ⚠ Replica `Sprites/Default` EXACTAMENTE (Blend One OneMinusSrcAlpha premultiplicado,
// ZWrite Off, Cull Off, Queue Transparent). El suelo depende de ese blend y de ese orden de
// dibujado, y cambiarlo descoloca las sombras de decos y peces, que se dibujan sobre él.
// Lo único que se añade es la niebla.
//
// ⚠ `BuildFloorMaterial` lo busca PRIMERO y cae a `Sprites/Default` si no está, así que si
// este shader se estropea o lo stripean, el suelo vuelve al comportamiento de siempre en vez
// de salir magenta. Aun asi va en Always Included para que no lo stripeen.
Shader "Appquarium/SubstrateFog"
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
            // Sin LightMode tag — a proposito. Ver la cabecera.
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
                float  wz    : TEXCOORD1;   // Z del mundo, para la niebla
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;

            // Los MISMOS globales que `DecoLit` y `FishUnlit`. Comparten el medio a proposito:
            // si el suelo tuviera su propia niebla, volveria a despegarse de las decos.
            // Default 0 = sin cambio (un global que nadie publica vale 0, nunca 1).
            float4    _AqWaterFog;        // rgb = color del agua · a = densidad (0 = apagado)
            float4    _AqWaterFogRange;   // x = Z donde empieza · y = Z donde satura

            v2f vert(appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv, _MainTex);
                // ⚠ El vertex color NO es decorativo: lleva el degradado del sustrato
                // (colorA→colorB) y el alpha del fundido. Se multiplica igual que en
                // `Sprites/Default`; perderlo deja el suelo plano y sin fade.
                o.color = v.color * _Color;
                o.wz    = mul(unity_ObjectToWorld, v.vertex).z;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;

                // Niebla ANTES de premultiplicar por alpha: si se hiciera despues, las zonas
                // en fundido recibirian el color del agua a plena intensidad y el borde de
                // atras del suelo saldria como una banda dura, que es justo lo contrario de
                // lo que se busca.
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
