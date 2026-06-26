using System;
using System.Collections.Generic;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    [CreateAssetMenu(fileName = "PartConfigData", menuName = "ScriptableObjects/PartConfigData")]
    public class PartConfigData : ConfigObject
    {
        public PartConfig data;

        public override ConfigData GetData()
        {
            return data;
        }

        public override void SetData(ConfigData cd)
        {
            data = cd as PartConfig;
        }
    }

    /// <summary>
    /// 部件配置
    /// </summary>
    [Serializable]
    public class PartConfig : ConfigData
    {
        public string partName;
        public string partID;
        public List<PointConfig> pointConfigs;
    }

    [Serializable]
    public class PointConfig
    {
        public string pointName;
        public string pointID;
        public string pointUnit;
        public PointDataType dataType = PointDataType.String;
    }
}
