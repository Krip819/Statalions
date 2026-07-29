Shader "Stalions/Monochrome Map"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            fixed4 frag(v2f_img input) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, input.uv);
                fixed luminance = dot(color.rgb, fixed3(0.2126, 0.7152, 0.0722));
                luminance = saturate(luminance * 0.78 + 0.035);
                color.rgb = fixed3(luminance, luminance, luminance);
                return color;
            }
            ENDCG
        }
    }

    Fallback Off
}
