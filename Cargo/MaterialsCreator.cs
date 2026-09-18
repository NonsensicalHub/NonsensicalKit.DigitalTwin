using NonsensicalKit.Core;
using NonsensicalKit.Tools.ObjectPool;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Cargo
{

/// <summary>
/// 响应 CreateNewMaterial / RecycleMaterialModel，用对象池创建与回收货物模型。
/// 场景中挂载并指定 m_materialPrefab 即可供 ConveyorPartMotion、堆垛机等调用。
/// </summary>
public class MaterialsCreator : NonsensicalMono
{
    [SerializeField] private GameObject m_materialPrefab;

    private GameObjectPool _pool;

    private void Awake()
    {
        if (m_materialPrefab == null)
        {
            Debug.LogError("[MaterialsCreator] 未指定物料预制体", this);
            return;
        }

        _pool = new GameObjectPool(m_materialPrefab, OnReset, OnInit);

        AddHandler<Vector3, GameObject>("CreateNewMaterial", CreateNewMaterial);
        Subscribe<GameObject>("RecycleMaterialModel", OnRecycleMaterialModel);
        Subscribe<Transform>("RecycleMaterialModel", OnRecycleMaterialModel);
    }

    private GameObject CreateNewMaterial(Vector3 pos)
    {
        var go = _pool.New();
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        go.SetActive(true);
        return go;
    }

    private void OnRecycleMaterialModel(GameObject material)
    {
        if (material == null) return;
        material.transform.SetParent(transform, false);
        _pool.Store(material);
    }

    private void OnRecycleMaterialModel(Transform material)
    {
        if (material == null) return;
        OnRecycleMaterialModel(material.gameObject);
    }

    private static void OnReset(GameObject go) => go.SetActive(false);

    private static void OnInit(GameObject go) => go.SetActive(true);
}
}
