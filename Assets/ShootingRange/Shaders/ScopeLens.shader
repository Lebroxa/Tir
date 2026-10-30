// Scope lens: shows the scope camera Render Texture with a round vignette and reticle.
// Unlit shader, works in Built-in and URP.
// UNITY_VERTEX_* macros make it render in both VR eyes (Single Pass Instanced).
Shader "ShootingRange/ScopeLens"
{
    Properties
    {
        _MainTex ("Scope View", 2D) = "black" {}
        _ReticleColor ("Reticle Color", Color) = (0, 0, 0, 1)
        _DotColor ("Center Dot Color", Color) = (1, 0.1, 0.1, 1)
        _Active ("Active", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _ReticleColor;
            fixed4 _DotColor;
            float _Active;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 c = i.uv - 0.5;
                float r = length(c);

                fixed4 col = tex2D(_MainTex, i.uv) * _Active;
                col.a = 1;

                // round vignette
                col.rgb *= smoothstep(0.5, 0.43, r);

                // thin crosshair with a gap in the center
                float thin = 0.0035;
                float lineH = step(abs(c.y), thin) * step(0.03, abs(c.x));
                float lineV = step(abs(c.x), thin) * step(0.03, abs(c.y));
                // thick posts from the edge
                float thick = 0.012;
                float postH = step(abs(c.y), thick) * step(0.28, abs(c.x));
                float postV = step(abs(c.x), thick) * step(0.28, -c.y);
                float reticle = saturate(lineH + lineV + postH + postV) * step(r, 0.5);
                col.rgb = lerp(col.rgb, _ReticleColor.rgb, reticle);

                // red center dot
                float dotMask = step(r, 0.009);
                col.rgb = lerp(col.rgb, _DotColor.rgb, dotMask);

                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
