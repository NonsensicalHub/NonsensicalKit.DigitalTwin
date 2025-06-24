using System;
using System.Collections.Generic;
using MQTTnet.Protocol;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.MQTT
{
    /// <summary>
    /// 暂无安卓支持
    /// </summary>
    public partial class MqttManager : NonsensicalMono
    {
        public string MQTTPrefix = "ws://";
        public string MQTTURI = "broker.emqx.io";
        public int MQTTPort = 1883;
        public string MQTTSuffix;
        public bool IsWebSocketConnectionType;
        public string MQTTUser = "";
        public string MQTTPassword = "";

        [field:SerializeField, ReadOnly]    public bool Log { get; set; }

        [field:SerializeField, ReadOnly,Label("重连间隔时间(s)")] public float ReconnectGapTime  { get; set; }= 10;
        [field:SerializeField, ReadOnly]  public bool UseTLS { get; set; }

        private Dictionary<string, MqttQualityOfServiceLevel>  SubscribeTopics  { get; set; }= new ();

        [field: SerializeField, Label("MQTT链接状态"), ReadOnly]
        public MQTTStatus Status { get;private set; }

        private float _waitTime;
        private string _clientID;

        public Action<string, string> MessageReceived;

        public partial void Run();

        private partial void OnApplicationQuit();

        public List<string> ShowSubscribedTopics()
        {
            if (SubscribeTopics == null || SubscribeTopics.Count == 0) return null;
            var topics = new List<string>();
            foreach (var item in SubscribeTopics)
            {
                topics.Add(item.Key);
                Debug.Log($"Subscribed topic: {item.Key}");
            }

            return topics;
        }
    }
}
