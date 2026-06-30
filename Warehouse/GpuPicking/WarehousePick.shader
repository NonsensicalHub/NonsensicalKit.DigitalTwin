Shader "NonsensicalKit/DigitalTwin/WarehousePick"
{
    // URP 专用 Pick Pass；内置管线 / HDRP 需另写 Shader 并改 WarehouseGpuPicker 中的 PickShaderName。
    Properties
    {
        [Range(0, 1)] _DitherVisibility ("Dither Visibility", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
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
            Name "WarehousePick"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZWrite On
            ZTest [_ZTest]
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _DitherVisibility;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(WarehousePickProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _WarehousePickColor)
            UNITY_INSTANCING_BUFFER_END(WarehousePickProps)

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            void WarehouseApplyDitherClip(float visibility, float4 positionCS)
            {
                const float hideEpsilon = 1e-4;
                const float showEpsilon = 1e-4;

                if (visibility <= hideEpsilon)
                {
                    clip(-1.0);
                    return;
                }

                if (visibility >= 1.0 - showEpsilon)
                {
                    return;
                }

                uint2 pixel = uint2(positionCS.xy);
                static const float4x4 bayerMatrix = float4x4(
                    0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
                    12.0 / 16.0, 4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                    3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
                    15.0 / 16.0, 7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
                );

                float threshold = bayerMatrix[pixel.x % 4][pixel.y % 4];
                if (visibility <= threshold)
                {
                    clip(-1.0);
                }
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 pickColor = UNITY_ACCESS_INSTANCED_PROP(WarehousePickProps, _WarehousePickColor);
                if (pickColor.r + pickColor.g + pickColor.b + pickColor.a <= 0.0)
                {
                    clip(-1.0);
                }

                half visibility = saturate(_DitherVisibility);
                WarehouseApplyDitherClip(visibility, input.positionCS);

                return half4(pickColor);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
