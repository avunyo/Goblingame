Shader "Goblingame/GradientSky"
{
    Properties
    {
        _Top ("Zenith", Color) = (0.05, 0.07, 0.22, 1)
        _Mid ("Middle", Color) = (0.55, 0.28, 0.52, 1)
        _Horizon ("Horizon", Color) = (1.0, 0.6, 0.35, 1)
        _Ground ("Below horizon", Color) = (0.2, 0.18, 0.32, 1)
        _MidPoint ("Middle height", Range(0.05, 0.9)) = 0.28
        _SunDir ("Sun direction", Vector) = (0, 0.2, 1, 0)
        _SunGlow ("Sun glow", Color) = (1, 0.65, 0.35, 1)
        _Stars ("Star amount", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            fixed4 _Top, _Mid, _Horizon, _Ground, _SunGlow;
            float _MidPoint, _Stars;
            float4 _SunDir;

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash (float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                fixed4 c;
                if (h < 0) c = lerp(_Horizon, _Ground, saturate(-h * 5.0));
                else if (h < _MidPoint) c = lerp(_Horizon, _Mid, smoothstep(0.0, _MidPoint, h));
                else c = lerp(_Mid, _Top, smoothstep(_MidPoint, 0.95, h));

                // warm glow toward the low sun
                float s = saturate(dot(d, normalize(_SunDir.xyz)));
                c.rgb += _SunGlow.rgb * (pow(s, 5.0) * 0.45 + pow(s, 60.0) * 0.6);

                // a few stars high up
                float3 cell = floor(d * 140.0);
                float star = step(0.9985, hash(cell)) * saturate((h - 0.25) * 3.0) * _Stars;
                c.rgb += star;
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
