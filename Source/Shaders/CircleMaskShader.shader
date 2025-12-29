Shader "Custom/CircleMaskShader"
{
    Properties
    {
        _CenterX ("Center X", Range(0, 1)) = 0.5
        _CenterY ("Center Y", Range(0, 1)) = 0.5
        _Radius ("Radius", Range(0, 1)) = 0.3
        _EdgeSoftness ("Edge Softness", Range(0, 0.1)) = 0.02
        _Color ("Color", Color) = (0, 0, 0, 1)
    }
    
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        LOD 100
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
            
            float _CenterX;
            float _CenterY;
            float _Radius;
            float _EdgeSoftness;
            fixed4 _Color;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                // 计算到圆心的距离（UV空间）
                float2 center = float2(_CenterX, _CenterY);
                float dist = distance(i.uv, center);
                
                // 圆形遮罩：圆内透明，圆外黑色
                // 使用smoothstep实现柔和边缘
                float alpha = smoothstep(_Radius - _EdgeSoftness, _Radius, dist);
                
                return fixed4(_Color.rgb, alpha * _Color.a);
            }
            ENDCG
        }
    }
}



