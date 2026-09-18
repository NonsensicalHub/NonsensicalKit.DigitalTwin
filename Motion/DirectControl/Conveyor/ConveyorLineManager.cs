using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{

/// <summary>
/// 一层输送线的编辑/可视化管理：收集本层工位、打开俯视方向配置、统一开关 Gizmos。
/// 默认从子物体收集 <see cref="ConveyorPartMotion"/>；提升机等跨层设备放到额外工位，可同时被多层引用。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("DigitalTwin/输送线管理")]
public class ConveyorLineManager : MonoBehaviour
{
    [SerializeField, InspectorName("层名称"), Tooltip("俯视配置窗口标题；为空则用物体名")]
    private string m_layerName;

    [SerializeField, InspectorName("PLC 楼层"), Min(1), Tooltip("逻辑楼层，从 1 起（一/二/三楼）。提升机连线写入对应层的皮带拓扑；与 CurrentFloor 高度值（0/50/100）不同")]
    private int m_floor = 1;

    [SerializeField, InspectorName("从子物体收集"), Tooltip("收集本物体及子层级上的 ConveyorPartMotion")]
    private bool m_collectFromChildren = true;

    [SerializeField, InspectorName("额外工位"), Tooltip("不在子层级中、但属于本层的工位。提升机等可同时挂到多层")]
    private ConveyorPartMotion[] m_extraStations;

    [SerializeField, InspectorName("显示 Gizmos"), Tooltip("统一控制本层工位的场景 Gizmos。多层重叠时可只打开当前层")]
    private bool m_showGizmos = true;

    private static readonly List<ConveyorLineManager> Instances = new List<ConveyorLineManager>(8);

    public string LayerName => string.IsNullOrWhiteSpace(m_layerName) ? name : m_layerName.Trim();

    public int Floor => Mathf.Max(1, m_floor);

    public bool CollectFromChildren => m_collectFromChildren;

    public bool ShowGizmos
    {
        get => m_showGizmos;
        set => m_showGizmos = value;
    }

    public IReadOnlyList<ConveyorPartMotion> ExtraStations => m_extraStations;

    private void OnEnable()
    {
        if (!Instances.Contains(this))
            Instances.Add(this);
    }

    private void OnDisable()
    {
        Instances.Remove(this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        UnityEditor.SceneView.RepaintAll();
    }
#endif

    /// <summary>子物体工位 + 额外工位，去重后写入 <paramref name="results"/>。</summary>
    public void CollectStations(List<ConveyorPartMotion> results)
    {
        if (results == null) return;
        results.Clear();

        var seen = new HashSet<int>();
        if (m_collectFromChildren)
        {
            var children = GetComponentsInChildren<ConveyorPartMotion>(true);
            for (int i = 0; i < children.Length; i++)
            {
                var station = children[i];
                if (station == null) continue;
                var nested = station.GetComponentInParent<ConveyorLineManager>(true);
                if (nested != null && nested != this)
                    continue;
                TryAddStation(results, seen, station);
            }
        }

        if (m_extraStations == null) return;
        for (int i = 0; i < m_extraStations.Length; i++)
            TryAddStation(results, seen, m_extraStations[i]);
    }

    public bool Contains(ConveyorPartMotion station)
    {
        if (station == null) return false;
        if (m_collectFromChildren &&
            (station.transform == transform || station.transform.IsChildOf(transform)))
        {
            // includeInactive：避免父级暂时未激活时误判为「未管理」导致开关失效
            var nested = station.GetComponentInParent<ConveyorLineManager>(true);
            if (nested == this)
                return true;
        }
        return ContainsExtra(station);
    }

    public bool ContainsExtra(ConveyorPartMotion station)
    {
        if (station == null || m_extraStations == null) return false;
        for (int i = 0; i < m_extraStations.Length; i++)
        {
            if (m_extraStations[i] == station)
                return true;
        }

        return false;
    }

    /// <summary>优先父级管理类，其次「额外工位」列表。</summary>
    public static ConveyorLineManager FindOwner(ConveyorPartMotion station)
    {
        if (station == null) return null;

        var parent = station.GetComponentInParent<ConveyorLineManager>(true);
        if (parent != null) return parent;

        PruneInstances();
        EnsureInstancesRegistered();
        for (int i = 0; i < Instances.Count; i++)
        {
            var mgr = Instances[i];
            if (mgr != null && mgr.ContainsExtra(station))
                return mgr;
        }

        return null;
    }

    public static ConveyorLineManager FindOwner(GameObject go)
    {
        if (go == null) return null;

        var mgr = go.GetComponent<ConveyorLineManager>();
        if (mgr != null) return mgr;

        mgr = go.GetComponentInParent<ConveyorLineManager>(true);
        if (mgr != null) return mgr;

        var station = go.GetComponent<ConveyorPartMotion>();
        return FindOwner(station);
    }

    /// <summary>
    /// 被管理时以管理类开关为准；提升机等多层额外工位：任一所属层打开即显示。
    /// 未被任何层管理时保持原行为（显示）。
    /// </summary>
    public static bool ShouldDrawGizmos(ConveyorPartMotion station)
    {
        if (station == null) return false;

        // 热路径：只查父级一次，禁止在此 FindObjectsByType（SceneGUI / OnDrawGizmos 每帧会打数百次）
        var parent = station.GetComponentInParent<ConveyorLineManager>(true);
        bool owned = false;

        if (parent != null)
        {
            owned = true;
            if (parent.enabled && parent.gameObject.activeInHierarchy && parent.m_showGizmos)
                return true;
        }

        PruneInstances();
        if (Instances.Count == 0)
            EnsureInstancesRegistered();

        for (int i = 0; i < Instances.Count; i++)
        {
            var mgr = Instances[i];
            if (mgr == null || mgr == parent) continue;
            if (!mgr.ContainsExtra(station)) continue;

            owned = true;
            if (mgr.enabled && mgr.gameObject.activeInHierarchy && mgr.m_showGizmos)
                return true;
        }

        return !owned;
    }

    /// <summary>仅在静态列表为空时从场景补登记（OnEnable 正常会维护列表）。</summary>
    private static void EnsureInstancesRegistered()
    {
        PruneInstances();
        if (Instances.Count > 0) return;

        var found = Object.FindObjectsByType<ConveyorLineManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            var mgr = found[i];
            if (mgr != null && !Instances.Contains(mgr))
                Instances.Add(mgr);
        }
    }

    private static void TryAddStation(List<ConveyorPartMotion> results, HashSet<int> seen, ConveyorPartMotion station)
    {
        if (station == null) return;
        if (!seen.Add(station.GetInstanceID())) return;
        results.Add(station);
    }

    private static void PruneInstances()
    {
        for (int i = Instances.Count - 1; i >= 0; i--)
        {
            if (Instances[i] == null)
                Instances.RemoveAt(i);
        }
    }
}
}
