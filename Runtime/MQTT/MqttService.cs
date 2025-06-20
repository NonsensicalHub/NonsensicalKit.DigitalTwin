using System;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MQTT
{
    /// <summary>
    /// MQTT管理器
    /// </summary>
    [ServicePrefab("Services/MqttService")]
    public class MqttService : NonsensicalMono, IMonoService
    {
        [SerializeField, BoxGroup("MQTT链接")] private string m_mqttPrefix = "ws://";
        [SerializeField, BoxGroup("MQTT链接")] private string m_mqtturi = "broker.emqx.io";
        [SerializeField, BoxGroup("MQTT链接")] private int m_mqttPort = 1883;
        [SerializeField, BoxGroup("MQTT链接")] private string m_mqttSuffix;
        [SerializeField, BoxGroup("MQTT链接")] private bool m_isWebSocketConnectionType;
        [SerializeField, BoxGroup("MQTT链接")] private string m_mqttUser = "";
        [SerializeField, BoxGroup("MQTT链接")] private string m_mqttPassword = "";

        [SerializeField] private bool m_log;

        [SerializeField, Label("重连间隔时间(s)")] private float m_reconnectGapTime = 10;
        [SerializeField] private bool m_useTls;

        public bool IsReady { get; private set; }
        public Action InitCompleted { get; set; }

        public MqttManager Manager;

        private void Awake()
        {
            IsReady = true;
            Init();
        }

        private void Init()
        {
            Manager= gameObject.AddComponent<MqttManager>();
            Manager.MQTTPrefix = m_mqttPrefix;
            Manager.MQTTURI = m_mqtturi;
            Manager.MQTTPort = m_mqttPort;
            Manager.MQTTSuffix = m_mqttSuffix;
            Manager.IsWebSocketConnectionType = m_isWebSocketConnectionType;
            Manager.MQTTUser = m_mqttUser;
            Manager.MQTTPassword = m_mqttPassword;
            Manager.m_log = m_log;
            Manager.m_useTLS = m_useTls;
            Manager.ReconnectGapTime = m_reconnectGapTime;
            Manager.Run();
        }
    }

    /// <summary>
    /// 状态
    /// </summary>
    public enum MQTTStatus
    {
        Empty = 0,

        /// <summary>
        /// 连接中
        /// </summary>
        Connecting = 1,

        /// <summary>
        /// 连接成功
        /// </summary>
        Connected = 2,

        /// <summary>
        /// 连接失败
        /// </summary>
        ConnectFailed = 3,
    }
}
