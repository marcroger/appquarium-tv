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
        _ShadowAlpha ("Shadow Alpha", Range(0,1)) = 0.50
    }

    SubShader
    {
        // Transparent-1 = justo antes de la geometría transparente, tras los opacos.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-1" }

        Pass
        {
            // Sin LightMode tag — ejecuta en el SRPDefaultUnlit path del Cast device.
            Blend  SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull   Off          // la proyección puede invertir caras
            ZTest  LEqual       // se oculta detrás de geometría sólida (roca, etc.)

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _FloorY;
            float _ShadowAlpha;

            struct appdata { float4 vertex : POSITION; };
            struct v2f     { float4 pos : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                // objeto → mundo → snap al plano del suelo → clip
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                wp.y      = _FloorY;
                o.pos     = UnityWorldToClipPos(wp);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Teal oscuro, encaja con la paleta del acuario
                return fixed4(0.0, 0.04, 0.06, _ShadowAlpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
