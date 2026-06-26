#ifndef WAREHOUSE_SIMPLE_INSTANCING_INCLUDED
#define WAREHOUSE_SIMPLE_INSTANCING_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

struct InstanceItemData
{
    float4x4 worldMatrix;
    // 显隐因子 [0,1]，在片元阶段与 Bayer 矩阵比较后 clip 丢弃像素（非 Alpha 混合）。
    float visibility;
    float3 _Padding;
};

StructuredBuffer<InstanceItemData> _PerInstanceItemData;

void WarehouseInstancingSetupProcedural()
{
#ifndef SHADERGRAPH_PREVIEW
    #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    InstanceItemData item = _PerInstanceItemData[unity_InstanceID];
    unity_ObjectToWorld = mul(unity_ObjectToWorld, item.worldMatrix);
    #endif
#endif
}

float WarehouseGetInstanceVisibility()
{
#ifndef SHADERGRAPH_PREVIEW
    #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    return _PerInstanceItemData[unity_InstanceID].visibility;
    #endif
#endif
    return 1.0;
}

// 4x4 Bayer 阈值；visibility 越小丢弃像素越多。
// 注意：HLSL clip(x) 仅在 x < 0 时丢弃，x == 0 仍会通过，故 visibility=0 须提前全丢弃。
void WarehouseApplyDitherClip(float visibility, float4 positionCS)
{
    const float kHideEpsilon = 1e-4;
    const float kShowEpsilon = 1e-4;

    if (visibility <= kHideEpsilon)
    {
        clip(-1.0);
        return;
    }

    if (visibility >= 1.0 - kShowEpsilon)
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

// 兼容旧 Shader Graph Custom Function 名称。
void instancingItemSetup()
{
    WarehouseInstancingSetupProcedural();
}

float WarehouseGetInstanceAlpha()
{
    return WarehouseGetInstanceVisibility();
}

void GetInstanceItemID_float(out float Out)
{
    Out = 0;
#ifndef SHADERGRAPH_PREVIEW
    #if UNITY_ANY_INSTANCING_ENABLED
    Out = unity_InstanceID;
    #endif
#endif
}

void InstancingItem_float(float3 Position, out float3 Out)
{
    Out = Position;
}

#endif
