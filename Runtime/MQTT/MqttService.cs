using System;
using System.Collections.Generic;
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
        [SerializeField, Label("重连间隔时间(s)")] private float m_reconnectGapTime = 10;
        [SerializeField] private bool m_log;
        [SerializeField] private bool m_useTls;

        public bool IsReady { get; private set; }
        public Action InitCompleted { get; set; }

        public Dictionary<string, MqttManager> Managers { get; private set; } = new();
        public MqttManager Manager { get; private set; }

        private void Awake()
        {
            IsReady = true;
            ServiceCore.SafeGet<ConfigService>(Init);
        }

        private void Init(ConfigService configService)
        {
            var configs = configService.GetConfigs<MqttClientConfigData>();

            foreach (var config in configs)
            {
                var manager = gameObject.AddComponent<MqttManager>();
                manager.MQTTPrefix = config.m_MqttPrefix;
                manager.MQTTURI = config.m_MqttUri;
                manager.MQTTPort = config.m_MqttPort;
                manager.MQTTSuffix = config.m_MqttSuffix;
                manager.IsWebSocketConnectionType = config.m_IsWebSocketConnectionType;
                manager.MQTTUser = config.m_MqttUser;
                manager.MQTTPassword = config.m_MqttPassword;
                manager.m_log = m_log;
                manager.m_useTLS = m_useTls;
                manager.ReconnectGapTime = m_reconnectGapTime;
                manager.Run();
                Manager ??= manager;
                Managers[config.ConfigID] = manager;
            }
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
