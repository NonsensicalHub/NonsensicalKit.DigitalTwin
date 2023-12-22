using NonsensicalKit.Core.Service.Config;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.PLC
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

    [System.Serializable]
    public class PartConfig : ConfigData
    {
        public string partID;
        public List<string> pointIDs;
    }
}
