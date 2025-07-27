Shader "Custom/BicubicSharpen"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _SharpenStrength ("Sharpen Strength", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _SharpenStrength;
            float4 _MainTex_TexelSize;

            float4 frag(v2f_img i) : COLOR
            {
                float2 uv = i.uv;

                // Bicubic-like 3x3 sample
                float3 col = tex2D(_MainTex, uv).rgb * 4.0;
                col -= tex2D(_MainTex, uv + float2(_MainTex_TexelSize.x, 0)).rgb;
                col -= tex2D(_MainTex, uv - float2(_MainTex_TexelSize.x, 0)).rgb;
                col -= tex2D(_MainTex, uv + float2(0, _MainTex_TexelSize.y)).rgb;
                col -= tex2D(_MainTex, uv - float2(0, _MainTex_TexelSize.y)).rgb;

                float3 sharp = tex2D(_MainTex, uv).rgb + _SharpenStrength * col;
                return float4(saturate(sharp), 1.0);
            }
            ENDCG
        }
    }
}

