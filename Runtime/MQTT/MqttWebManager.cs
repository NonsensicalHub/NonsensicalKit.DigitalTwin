#if !UNITY_EDITOR&&UNITY_WEBGL&&NonsensicalkitWebgl
using MQTTnet.Protocol;
using NonsensicalKit.Core;
using NonsensicalKit.WebGL;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MQTT
{
    public partial class MqttManager
    {
        private bool _connected;

        public partial void Run()
        {
            Subscribe("MQTTInitCompleted", Init);
            Subscribe<string, string>("MQTTConnectSuccess", OnWebMQTTConnectSuccess);
        }

        private partial void OnApplicationQuit()
        {
            if (PlatformInfo.IsWebGL)
            {
                WebMQTT.Instance.CloseAll();
            }
        }

        protected virtual void Init()
        {
            if (Log) Debug.Log("InitWebMQTT: WebMQTT.Instance == null :" + (WebMQTT.Instance == null));


            WebMQTT.Instance.Connect($"{MQTTPrefix}{MQTTURI}:{MQTTPort}/mqtt", MQTTUser, MQTTPassword);

            _connected = true;
            foreach (var topic in SubscribeTopics)
            {
                SubscribeAsync(topic.Key);
            }

            IOCC.Subscribe<string, string>("MQTTMessage", OnWebMQTTMessageReceived);
        }

        private void OnWebMQTTMessageReceived(string topic, string message)
        {
            if (Log) Debug.Log("客户端收到消息：" + topic + "=====" + message);
            MessageReceived?.Invoke(topic, message);
        }

        private void OnWebMQTTConnectSuccess(string url, string clientId)
        {
            if (url != MQTTURI) return;

            if (Log) Debug.Log("MQTTMessageConnectSuccess");

            _clientID = clientId;
            Status = MQTTStatus.Connected;
        }

        #region 发布消息

        public void PublishAsync(string topic, string message)
        {
            if (Log) Debug.Log($"客户端发布：Published message: {message} to topic: {topic}");
            WebMQTT.Instance.SendMessage(MQTTURI, topic, message);
        }

        #endregion

        #region 订阅消息

        public void SubscribeAsync(string topic)
        {
            if (_connected)
            {
                WebMQTT.Instance.Subscribe(MQTTURI, topic);
            }

            SubscribeTopics.Add(topic, MqttQualityOfServiceLevel.AtLeastOnce);
        }

        public void SubscribeAsync(string[] topics)
        {
            if (_connected)
            {
                foreach (var topic in topics)
                {
                    WebMQTT.Instance.Subscribe(MQTTURI, topic);
                }
            }

            foreach (var topic in topics)
            {
                SubscribeTopics.Add(topic, MqttQualityOfServiceLevel.AtLeastOnce);
            }
        }

        #endregion

        #region 取消订阅消息

        public void UnsubscribeAsync(params string[] topics)
        {
            foreach (var topic in topics)
            {
                WebMQTT.Instance.Unsubscribe(MQTTURI, topic);
            }

            foreach (var item in topics)
            {
                if (SubscribeTopics.ContainsKey(item))
                {
                    SubscribeTopics.Remove(item);
                }
            }
        }

        #endregion
    }
}
#endif
