using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NonsensicalKit.Tools.EasyTool;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NonsensicalKit.DigitalTwin.Cargo
{

/// <summary>
/// 泛型工位货物控制器
/// TKey 可以是 int、string、enum、Vector2Int 等任意类型
/// </summary>
public class StationCargoController<TKey> : IStationCargoUpdater
{
    readonly Dictionary<TKey, LinkedList<NonsensicalMover>> _dic = new();
    readonly Dictionary<LinkedList<NonsensicalMover>, TKey> _listToKey = new();
    readonly Dictionary<NonsensicalMover, LinkedListNode<NonsensicalMover>> _nodeOf = new();
    readonly HashSet<NonsensicalMover> _set = new();
    NonsensicalMover[] _buf = new NonsensicalMover[64];
    readonly Queue<LinkedListNode<NonsensicalMover>> _pool;
    LinkedListNode<NonsensicalMover> _tpl;
    bool _local;
    readonly Func<TKey, bool, TKey> _neighbor;

    /// <summary>货物被回收时的外部回调，可对接 MaterialsCreator 等对象池。</summary>
    public Action<NonsensicalMover> RecycleMaterialEvent;

    /// <param name="useDefaultPool">是否启用内置对象池。</param>
    /// <param name="useLocalModel">货物坐标使用本地/世界坐标。</param>
    /// <param name="getNeighbor">邻居计算策略，用于 HasMaterialComing 等邻域查询。</param>
    public StationCargoController(
        bool useDefaultPool = false,
        bool useLocalModel = true,
        Func<TKey, bool, TKey> getNeighbor = null)
    {
        _local = useLocalModel;
        _neighbor = getNeighbor;
        if (useDefaultPool)
        {
            _pool = new Queue<LinkedListNode<NonsensicalMover>>();
            RecycleMaterialEvent += RecycleMover;
        }
    }

    #region 索引器

    /// <summary>获取指定工位的第一个货物（无货物返回 null）。</summary>
    /// <param name="checkMoving">为 true 时跳过正在移动的货物。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NonsensicalMover Get(TKey key, bool checkMoving = true)
    {
        if (!_dic.TryGetValue(key, out var list) || list.Count == 0)
            return null;
        return checkMoving ? FirstNonMoving(list) : list.First.Value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(TKey key, out NonsensicalMover mover, bool checkMoving = true)
    {
        mover = null;
        if (!_dic.TryGetValue(key, out var l) || l.Count <= 0) return false;
        mover = checkMoving ? FirstNonMoving(l) : l.First.Value;
        return mover != null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static NonsensicalMover FirstNonMoving(LinkedList<NonsensicalMover> list)
    {
        for (var node = list.First; node != null; node = node.Next)
        {
            if (!node.Value.Moving)
                return node.Value;
        }

        return null;
    }

    /// <summary>将货物挂到指定工位末尾，已在其他工位则 O(1) 迁移。</summary>
    public void Set(TKey key, NonsensicalMover m)
    {
        if (m == null) return;

        if (_nodeOf.TryGetValue(m, out var n))
        {
            if (n.List != null && _dic.TryGetValue(key, out var target) && n.List == target)
            {
                _set.Add(m);
                return;
            }

            n.List?.Remove(n);
        }
        else
        {
            n = new LinkedListNode<NonsensicalMover>(m);
        }

        AttachToStation(key, n);
        _nodeOf[m] = n;
        _set.Add(m);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void AttachToStation(TKey key, LinkedListNode<NonsensicalMover> n)
    {
        if (_dic.TryGetValue(key, out var l))
            l.AddLast(n);
        else
        {
            var newList = new LinkedList<NonsensicalMover>();
            newList.AddLast(n);
            _dic[key] = newList;
            _listToKey[newList] = key;
        }
    }

    #endregion

    #region 帧更新

    /// <summary>驱动所有活跃货物的移动插值，由 Bootstrap 每帧调用。</summary>
    public void UpdatePos()
    {
        int c = _set.Count;
        if (c == 0) return;

        if (c > _buf.Length)
            _buf = new NonsensicalMover[Mathf.NextPowerOfTwo(c)];

        _set.CopyTo(_buf, 0);
        for (int i = 0; i < c; i++)
            _buf[i]?.UpdateMove();
    }

    #endregion

    #region 创建

    public void CreateMover(TKey key, Vector3 pos, NonsensicalMover m = null)
    {
        if (m != null)
        {
            Set(key, m);
            if (_local) m.Obj.localPosition = pos;
            else m.Obj.position = pos;
        }
        else if (_pool != null)
        {
            m = PoolGet();
            if (m != null)
            {
                Set(key, m);
                if (_local) m.Obj.localPosition = pos;
                else m.Obj.position = pos;
            }
        }
    }

    #endregion

    #region 查询

    /// <summary>沿邻接链查询指定深度内是否有货物（需构造时提供 getNeighbor）。</summary>
    public bool HasMaterialComing(TKey key, bool dir, int depth = 1)
    {
        if (_neighbor == null || depth <= 0) return false;

        TKey cur = key;
        for (int i = 0; i < depth; i++)
        {
            cur = _neighbor(cur, dir);
            if (_dic.TryGetValue(cur, out var l) && l.Count > 0)
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasMaterialInRange(TKey startKey, bool dir, int maxDepth = 3)
        => HasMaterialComing(startKey, dir, maxDepth);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasMaterialComingDirect(TKey key)
        => _dic.TryGetValue(key, out var l) && l.Count > 0;

    public bool HasMaterialInKeys(params TKey[] keys)
    {
        for (int i = 0; i < keys.Length; i++)
            if (_dic.TryGetValue(keys[i], out var l) && l.Count > 0)
                return true;
        return false;
    }

    /// <summary>获取货物当前所在的工位 key（O(1) 反向索引）。</summary>
    public TKey GetPos(NonsensicalMover m)
    {
        if (!_nodeOf.TryGetValue(m, out var n) || n.List == null)
            return default;

        return _listToKey.TryGetValue(n.List, out var key) ? key : default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCount(TKey key)
    {
        return _dic.TryGetValue(key, out var l) ? l.Count : -1;
    }

    #endregion

    #region 转移

    /// <summary>将货物从当前工位迁移到目标工位，复用同一链表节点。</summary>
    public void Replace(TKey key, NonsensicalMover m)
    {
        if (!_nodeOf.TryGetValue(m, out var n))
        {
            Set(key, m);
            return;
        }

        n.List?.Remove(n);
        AttachToStation(key, n);
    }

    #endregion

    #region 回收

    void RecycleMover(NonsensicalMover mover)
    {
        if (mover == null) return;
        mover.OnArrived = null;
        mover.Obj.gameObject.SetActive(false);
        _pool?.Enqueue(new LinkedListNode<NonsensicalMover>(mover));
    }

    /// <summary>回收指定工位上的全部货物，触发 RecycleMaterialEvent。</summary>
    public void RecycleMaterials(TKey key)
    {
        if (!_dic.TryGetValue(key, out var l) || l.Count == 0) return;

        while (l.Count > 0)
        {
            var node = l.First;
            var m = node.Value;

            l.RemoveFirst();
            _nodeOf.Remove(m);
            _set.Remove(m);

            m.Obj.name = $"Recycle_{key}";
            RecycleMaterialEvent?.Invoke(m);
        }
    }

    /// <summary>
    /// 将货物从控制器中摘除：停止 UpdatePos 驱动，但不触发回收事件。
    /// 用于货物被外部接管（如提升机 parent 挂载）。
    /// </summary>
    public void Detach(NonsensicalMover m)
    {
        if (m == null) return;

        if (_nodeOf.TryGetValue(m, out var n))
        {
            n.List?.Remove(n);
            _nodeOf.Remove(m);
        }

        _set.Remove(m);
    }

    bool TryRemoveFromIndexedOrKeyList(NonsensicalMover m, TKey key)
    {
        if (_nodeOf.TryGetValue(m, out var n))
        {
            n.List?.Remove(n);
            _nodeOf.Remove(m);
            return true;
        }

        if (_dic.TryGetValue(key, out var l))
            l.Remove(m);
        return false;
    }

    void RemoveResidualFromAllLists(NonsensicalMover m)
    {
        foreach (var l in _dic.Values)
        {
            for (var node = l.First; node != null;)
            {
                var next = node.Next;
                if (node.Value == m)
                    l.Remove(node);
                node = next;
            }
        }

        _nodeOf.Remove(m);
    }

    /// <summary>回收指定工位上的单个货物。</summary>
    public void RecycleMaterial(TKey key, NonsensicalMover m)
    {
        if (m == null) return;

        if (!TryRemoveFromIndexedOrKeyList(m, key))
            RemoveResidualFromAllLists(m);
        _set.Remove(m);
        RecycleMaterialEvent?.Invoke(m);
    }

    public void RecycleMaterial(NonsensicalMover m)
    {
        if (m == null) return;

        if (!_nodeOf.TryGetValue(m, out var n))
            RemoveResidualFromAllLists(m);
        else
        {
            n.List?.Remove(n);
            _nodeOf.Remove(m);
        }

        _set.Remove(m);
        RecycleMaterialEvent?.Invoke(m);
    }

    #endregion

    #region 对象池

    public void AddToPool(NonsensicalMover m)
    {
        m.Obj.gameObject.SetActive(false);
        _pool.Enqueue(new LinkedListNode<NonsensicalMover>(m));
    }

    public void SetTemplate(NonsensicalMover m, bool local)
    {
        _tpl = new LinkedListNode<NonsensicalMover>(m);
        m.Obj.gameObject.SetActive(false);
        _local = local;
    }

    public NonsensicalMover PoolGet(Action<NonsensicalMover> onArrived = null)
    {
        NonsensicalMover m = null;

        if (_pool is { Count: > 0 })
        {
            m = _pool.Dequeue().Value;
            m.Obj.gameObject.SetActive(true);
        }
        else if (_tpl != null)
        {
            var o = Object.Instantiate(_tpl.Value.Obj, _tpl.Value.Obj.parent);
            m = new NonsensicalMover(o, _tpl.Value.Speed, _local)
            {
                OnArrived = _tpl.Value.OnArrived
            };
            o.gameObject.SetActive(true);
        }

        if (m != null && onArrived != null)
            m.OnArrived += onArrived;

        return m;
    }

    #endregion

    public NonsensicalMover this[TKey key]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_dic.TryGetValue(key, out var l) && l.Count > 0)
                return l.First.Value;
            return null;
        }
    }
}
}
