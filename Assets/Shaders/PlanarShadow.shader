// Appquarium — Planar Shadow (CG legacy, versión Cast device)
// Proyecta los vértices reales del mesh al plano Y=_FloorY en espacio mundo →
// silueta exacta de la deco sobre el suelo (no blob circular).
//
// ⚠ Por qué CG legacy y NO el URP/HLSL del móvil:
//   Los shaders URP con pass "LightMode"="UniversalForward" NO se ejecutan en el
//   renderer del Cast device (Xiaomi WebGL/Chromium) → no pintarían nada, mismo bug
//   que el cuerpo del pez invisible. Solo los CG legacy SIN LightMode tag ejecutan
//   (SRPDefaultUnlit path). Ver DecoLit.shader / FishUnlit.shader.
//
// Uso en runtime (DecorationPlacer.AddShadow):
//   var mat = new Material(Shader.Find("Appquarium/PlanarShadow"));
//   mat.SetFloat("_FloorY", floorSurfaceY);
//
// Registrado en Graphics > Always Included Shaders (GUID 46a24ba3b30170c4fb557014c220c79c).

Shader "Appquarium/PlanarShadow"
{
    Properties
    {
        _FloorY      ("Floor Y",      Float)      = 0.0
        _ShadowAlpha ("Shadow Alpha", Range(0,1)) = 0.78
        // 0 = aplastado del todo contra el plano del suelo (INVISIBLE con esta cámara, ver abajo).
        // ~0.15 = queda una elipse baja que sí ocupa píxeles y se lee como sombra de contacto.
        _Flatten     ("Flatten",      Range(0,1)) = 0.22
        // Empuja la sombra hacia el fondo para que el ZTest la esconda tras su propia deco.
        _ZPush       ("Z Push",       Float)      = 0.35
        // Desvanecido por altura: lo que sube por encima del borde del suelo ya cae sobre el
        // telón del fondo, no sobre la arena. Con _ShadowFade = 0 se comporta como siempre.
        _ShadowTop   ("Borde sup. del suelo (world Y)", Float) = 999
        _ShadowFade  ("Margen de desvanecido (0=off)",  Float) = 0
    }

    SubShader
    {
        // ⚠ 2026-08-11 — ESTO ERA EL BUG DE "LAS SOMBRAS NO SE VEN".
        // Antes: Queue = "Transparent-1" (2999), con el comentario "justo antes de la
        // geometría transparente, TRAS LOS OPACOS". Esa premisa es falsa: el suelo NO es
        // opaco. Medido en el Editor (Appquarium TV → ShadowDiag 2 Dump):
        //     TankFloor            Sprites/Default  queue=3000  order=-5
        //     TankFloorOccluder    Sprites/Default  queue=3000  order=20   boundsY=[-9,24..-2,78]
        //     TankFloorFadeOverlay Sprites/Default  queue=3000  order=22
        //     sombra               PlanarShadow     queue=2999  order=21   floorY=-3,114
        // Las tres capas del suelo están en 3000 y la sombra en 2999 ⇒ la sombra se dibujaba
        // ANTES y el suelo (y el occluder, que cubre de -2,78 hacia abajo, justo donde cae el
        // plano de sombra) la tapaban por completo. La cola manda por encima del sortingOrder.
        // Ahora: misma cola que el suelo (3000) ⇒ decide el sortingOrder, que ya estaba puesto
        // a propósito para esto:  occluder 20  <  SOMBRA 21  <  fadeOverlay 22.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            // Sin LightMode tag — ejecuta en el SRPDefaultUnlit path del Cast device.
            Blend  SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull   Off          // la proyección puede invertir caras
            ZTest  LEqual       // se oculta detrás de geometría sólida (roca, etc.)

            // ⚠ Sin esto la sombra sale MOTEADA. Un coral son ~100.000 triángulos y al
            // aplanarlos se solapan cientos de veces sobre el mismo píxel; con alpha
            // blending cada capa oscurece otra vez y aparecen manchas y bandas según
            // cuánta geometría haya detrás. El stencil hace que cada píxel se pinte UNA
            // sola vez: densidad uniforme y, de paso, muchísimo menos blending en un
            // device que va justo.
            Stencil
            {
                Ref  1
                Comp NotEqual
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _FloorY;
            float _ShadowAlpha;
            float _Flatten;
            float _ZPush;
            // Desvanecido por altura. La sombra se comprime hacia el suelo pero NO colapsa,
            // así que en decos altas lo que sobra asoma por encima del borde del suelo y se lee
            // como una mancha sobre el FONDO (lo reportó el user el 2026-08-21).
            // _ShadowTop  = world Y del borde del suelo. _ShadowFade = margen de desvanecido.
            // Con _ShadowFade = 0 el comportamiento es el de siempre (sin desvanecer).
            float _ShadowTop;
            float _ShadowFade;

            struct appdata { float4 vertex : POSITION; };
            struct v2f     { float4 pos : SV_POSITION; float wy : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                // ⚠ 2026-08-11 — POR QUÉ NO ES UN APLASTADO TOTAL.
                // Antes: wp.y = _FloorY  → toda la sombra en un plano HORIZONTAL.
                // La cámara del acuario es ORTOGRÁFICA y mira en horizontal
                // (medido: ortho=True pos=(0,0,-10) rot=(0,0,0)), así que un plano
                // horizontal se ve exactamente DE CANTO ⇒ proyecta una línea de grosor
                // cero ⇒ 0 píxeles. Se dibujaba (visible=True, alpha=0,5) y no se veía.
                // Esta escena es 2.5D: el "suelo" es un sprite vertical con la arena
                // pintada en perspectiva, no geometría tumbada. Así que la sombra se
                // APLANA HACIA el suelo sin llegar a colapsar: queda una elipse baja
                // pegada a la base de la deco, que es como se lee una sombra de contacto
                // en 2.5D.
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                wp.y      = _FloorY + (wp.y - _FloorY) * _Flatten;
                // ⚠ La sombra conserva la Z de la deco, así que en la cara frontal del
                // objeto gana el ZTest y se pintaba UNA FRANJA OSCURA POR ENCIMA de la roca.
                // Empujándola hacia el fondo queda por detrás en profundidad: el ZTest LEqual
                // la recorta contra el propio objeto y solo asoma lo que cae sobre la arena.
                wp.z     += _ZPush;
                o.wy      = wp.y;          // world Y ya aplanado, para el desvanecido
                o.pos     = UnityWorldToClipPos(wp);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Teal MUY oscuro: sobre arena clara un gris medio se lee como una chapa
                // flotando, no como sombra. Casi negro con un punto de azul del agua.
                // Desvanecer lo que sube por encima del borde del suelo: ahí ya no hay arena
                // donde proyectar, sólo el telón del fondo.
                float a = _ShadowAlpha;
                if (_ShadowFade > 0.0001)
                    a *= saturate((_ShadowTop - i.wy) / _ShadowFade);
                return fixed4(0.0, 0.015, 0.025, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
