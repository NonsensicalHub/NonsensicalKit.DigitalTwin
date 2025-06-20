using System;
using NaughtyAttributes;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;

[CreateAssetMenu(fileName = "MqttClientConfig", menuName = "ScriptableObjects/MqttClientConfig")]
public class MqttClientConfig : ConfigObject
{
    public MqttClientConfigData data;

    public override ConfigData GetData()
    {
        return data;
    }

    public override void SetData(ConfigData cd)
    {
        data = cd as MqttClientConfigData;
    }
}

[Serializable]
public class MqttClientConfigData : ConfigData
{
    [BoxGroup("MQTT链接")] public string m_MqttPrefix = "ws://";
    [BoxGroup("MQTT链接")] public string m_MqttUri = "broker.emqx.io";
    [BoxGroup("MQTT链接")] public int m_MqttPort = 1883;
    [BoxGroup("MQTT链接")] public string m_MqttSuffix = "/mqtt";
    [BoxGroup("MQTT链接")] public bool m_IsWebSocketConnectionType;
    [BoxGroup("MQTT链接")] public string m_MqttUser = "";
    [BoxGroup("MQTT链接")] public string m_MqttPassword = "";
}
