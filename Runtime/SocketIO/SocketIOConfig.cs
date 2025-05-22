using NonsensicalKit.Core.Service.Config;
using UnityEngine;

namespace NonsensicalKit
{
    [CreateAssetMenu(fileName = "SocketIOConfig", menuName = "ScriptableObjects/SocketIOConfig")]
    public class SocketIOConfig : ConfigObject
    {
        public SocketIOConfigData data;
        public override ConfigData GetData()
        {
            return data;
        }

        public override void SetData(ConfigData cd)
        {
            data = cd as SocketIOConfigData;
        }
    }

    [System.Serializable]
    public class SocketIOConfigData : ConfigData
    {
        public string SocketIOUrl;
        public AutoSendMessage[] AutoSend;
        public string[] EventNames;
    }

    public class AutoSendMessage
    {
        public string Key;
        public string Value;
    }
}
