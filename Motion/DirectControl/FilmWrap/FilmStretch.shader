Shader "NonsensicalKit/DigitalTwin/FilmStretch"
{
    Properties
    {
        _BaseColor ("基础颜色（含透明度）", Color) = (0.55, 0.78, 0.95, 0.28)
        _EdgeColor ("边缘高光色（菲涅尔）", Color) = (0.85, 0.95, 1.0, 0.55)
        _FresnelPower ("菲涅尔强度：越大边缘越亮", Range(0.5, 8)) = 2.5
        _ScrollSpeed ("条纹滚动速度：模拟薄膜纹理流动", Float) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FilmUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma prefer_hlslcc gles

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EdgeColor;
                float _FresnelPower;
                float _ScrollSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.normalWS = normalInputs.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(posInputs.positionWS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewDirWS);
                float fresnel = pow(1.0 - saturate(dot(n, v)), _FresnelPower);

                float stripe = 0.5 + 0.5 * sin((input.uv.x + _Time.y * _ScrollSpeed) * 30.0);
                float4 col = lerp(_BaseColor, _EdgeColor, fresnel);
                col.a *= lerp(0.75, 1.0, stripe * 0.25 + fresnel);
                return col;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
