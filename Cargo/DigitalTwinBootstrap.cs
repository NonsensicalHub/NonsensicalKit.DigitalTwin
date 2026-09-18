using System;
using System.Collections.Generic;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Cargo
{

/// <summary>
/// 数字孪生线体启动器：按配置创建并注册 StationCargoController，每帧驱动货物移动。
/// 挂载于场景根节点，支持多条线体并存。
/// </summary>
public class DigitalTwinBootstrap : NonsensicalMono
{
    /// <summary>单条线体的货物控制器配置项。</summary>
    [Serializable]
    public class StationCargoControllerSetup
    {
        /// <summary>IOCC 注册键，工位通过 CargoLineKey 引用。</summary>
        public string lineKey = "Line1";

        /// <summary>工位索引类型（string partID 或 int 序号）。</summary>
        public StationCargoKeyType keyType = StationCargoKeyType.String;

        /// <summary>是否启用内置对象池。</summary>
        public bool useDefaultPool = true;

        /// <summary>货物坐标是否使用本地坐标系。</summary>
        public bool useLocalMode = false;
    }

    [SerializeField] private StationCargoControllerSetup[] m_controllers =
    {
        new() { lineKey = "Line1", keyType = StationCargoKeyType.String }
    };

    readonly List<IStationCargoUpdater> _updaters = new();

    void Awake()
    {
        if (m_controllers == null || m_controllers.Length == 0)
        {
            Debug.LogWarning("[DigitalTwinBootstrap] 未配置任何 StationCargoController。");
            return;
        }

        var defaultStringRegistered = false;

        for (int i = 0; i < m_controllers.Length; i++)
        {
            var setup = m_controllers[i];
            if (string.IsNullOrEmpty(setup.lineKey))
            {
                Debug.LogWarning($"[DigitalTwinBootstrap] 第 {i} 项 lineKey 为空，已跳过。");
                continue;
            }

            switch (setup.keyType)
            {
                case StationCargoKeyType.String:
                {
                    var controller = new StationCargoController<string>(setup.useDefaultPool, setup.useLocalMode);
                    IOCC.Set(setup.lineKey, controller);
                    if (!defaultStringRegistered)
                    {
                        // 首个 String 控制器同时作为无 key 默认实例，供 CargoLineKey 为空的工位使用
                        IOCC.Set(controller);
                        defaultStringRegistered = true;
                    }

                    _updaters.Add(controller);
                    break;
                }
                case StationCargoKeyType.Int:
                {
                    var controller = new StationCargoController<int>(setup.useDefaultPool, setup.useLocalMode);
                    IOCC.Set(setup.lineKey, controller);
                    _updaters.Add(controller);
                    break;
                }
                default:
                    Debug.LogWarning($"[DigitalTwinBootstrap] 不支持的 keyType: {setup.keyType}");
                    break;
            }
        }
    }

    private void Update()
    {
        foreach (var t in _updaters)
        {
            t?.UpdatePos();
        }
    }
}
}
