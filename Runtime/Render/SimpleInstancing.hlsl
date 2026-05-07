#ifndef BELT_ITEM_SHADER_INCLUDED
#define BELT_ITEM_SHADER_INCLUDED

// ✅ 修复1: 引入 instancing 头文件，确保 unity_InstanceID 被正确声明
#include "UnityInstancing.cginc"

struct InstanceItemData {
    float4x4 worldMatrix;
};

StructuredBuffer<InstanceItemData> _PerInstanceItemData;

void instancingItemSetup() {
    #ifndef SHADERGRAPH_PREVIEW
    // ✅ 修复2: 添加 instancing 启用检查
    #if UNITY_ANY_INSTANCING_ENABLED
    unity_ObjectToWorld = mul(
        unity_ObjectToWorld,
        _PerInstanceItemData[unity_InstanceID].worldMatrix
    );
    #endif
    #endif
}

void GetInstanceItemID_float(out float Out) {
    Out = 0;
    #ifndef SHADERGRAPH_PREVIEW
    #if UNITY_ANY_INSTANCING_ENABLED
    Out = unity_InstanceID;
    #endif
    #endif
}

void InstancingItem_float(float3 Position, out float3 Out) {
    Out = Position;
}

#endif
