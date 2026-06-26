Shader "NonsensicalKit/DigitalTwin/WarehouseInstance"
{
    Properties
    {
        [MainTexture] _TexItem ("Item", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [Range(0, 1)] _DitherVisibility ("Dither Visibility", Float) = 1
        [Toggle] _Emission ("Emission", Float) = 0
        _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "WarehouseInstanceForward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:WarehouseInstancingSetup

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SimpleInstancing.hlsl"

            TEXTURE2D(_TexItem);
            SAMPLER(sampler_TexItem);

            CBUFFER_START(UnityPerMaterial)
                float4 _TexItem_ST;
                half4 _Color;
                half _DitherVisibility;
                half _Emission;
                half4 _EmissionColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            void WarehouseInstancingSetup()
            {
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(SHADERGRAPH_PREVIEW)
                WarehouseInstancingSetupProcedural();
            #endif
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _TexItem);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 tex = SAMPLE_TEXTURE2D(_TexItem, sampler_TexItem, input.uv);
                half visibility = tex.a * _Color.a * _DitherVisibility * WarehouseGetInstanceVisibility();
                WarehouseApplyDitherClip(visibility, input.positionCS);

                half3 rgb = tex.rgb * _Color.rgb;
                if (_Emission > 0.5h)
                {
                    rgb += _EmissionColor.rgb;
                }

                return half4(rgb, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
