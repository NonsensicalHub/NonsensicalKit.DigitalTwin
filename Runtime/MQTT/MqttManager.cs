using System;
using System.Collections.Generic;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MQTT
{
    public partial class MqttManager : NonsensicalMono
    {
        public string MQTTPrefix = "ws://";
        public string MQTTURI = "broker.emqx.io";
        public int MQTTPort = 1883;
        public string MQTTSuffix;
        public bool IsWebSocketConnectionType;
        public string MQTTUser = "";
        public string MQTTPassword = "";


        public bool m_log;
        [SerializeField] private bool m_recordTopic;

        [SerializeField, ShowIf("m_recordTopic")]
        private bool m_showTopics;

        [SerializeField, Label("重连间隔时间(s)")] public float ReconnectGapTime = 10;
        [SerializeField] public bool m_useTLS;

        [SerializeField, ShowIf("m_showTopics"), ReadOnly, Label("已订阅的主题")]
        private List<string> _subscribeTopics = new List<string>();

        [SerializeField, Label("MQTT链接状态"), ReadOnly]
        private MQTTStatus _status;

        private int _failCount;
        private float _waitTime;
        private string _clientID;

        public Action<string, string> MessageReceived;

        public MQTTStatus Status => _status;


        public partial void Run();

        private partial void OnApplicationQuit();

        public List<string> ShowSubscribedTopics()
        {
            if (m_recordTopic)
            {
                if (_subscribeTopics == null || _subscribeTopics.Count == 0) return null;
                foreach (var item in _subscribeTopics)
                {
                    Debug.Log($"Subscribed topic: {item}");
                }

                return _subscribeTopics;
            }

            return null;
        }
    }
}
