#if !UNITY_EDITOR&&UNITY_WEBGL
using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.WebGL;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MQTT
{
    public partial class MqttManager
    {
        private readonly HashSet<string> _buffer = new();
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
            if (m_log) Debug.Log("InitWebMQTT: WebMQTT.Instance == null :" + (WebMQTT.Instance == null));


            WebMQTT.Instance.Connect($"{MQTTPrefix}{MQTTURI}:{MQTTPort}/mqtt", MQTTUser, MQTTPassword);

            _connected = true;
            foreach (var topic in _buffer)
            {
                SubscribeAsync(topic);
            }

            IOCC.Subscribe<string, string>("MQTTMessage", OnWebMQTTMessageReceived);
        }

        private void OnWebMQTTMessageReceived(string topic, string message)
        {
            if (m_log) Debug.Log("客户端收到消息：" + topic + "=====" + message);
            MessageReceived?.Invoke(topic, message);
        }

        private void OnWebMQTTConnectSuccess(string url, string clientId)
        {
            if (url != MQTTURI) return;

            if (m_log) Debug.Log("MQTTMessageConnectSuccess");

            _clientID = clientId;
            _status = MQTTStatus.Connected;
        }

        #region 发布消息

        public void PublishAsync(string topic, string message)
        {
            if (m_log) Debug.Log($"客户端发布：Published message: {message} to topic: {topic}");
            WebMQTT.Instance.SendMessage(MQTTURI, topic, message);
        }

        #endregion

        #region 订阅消息

        public void SubscribeAsync(string topic)
        {
            if (!_connected)
            {
                _buffer.Add(topic);
            }
            else
            {
                WebMQTT.Instance.Subscribe(MQTTURI, topic);
                if (m_recordTopic == false || _subscribeTopics.Contains(topic)) return;
                _subscribeTopics.Add(topic);
            }
        }

        public void SubscribeAsync(string[] topics)
        {
            if (!_connected)
            {
                foreach (var topic in topics)
                {
                    _buffer.Add(topic);
                }
            }
            else
            {
                foreach (var topic in topics)
                {
                    WebMQTT.Instance.Subscribe(MQTTURI, topic);
                }

                if (m_recordTopic)
                {
                    foreach (var item in topics)
                    {
                        if (_subscribeTopics.Contains(item) == false)
                        {
                            _subscribeTopics.Add(item);
                        }
                    }
                }
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

            if (m_recordTopic)
            {
                foreach (var item in topics)
                {
                    if (_subscribeTopics.Contains(item))
                    {
                        _subscribeTopics.Remove(item);
                    }
                }
            }
        }

        #endregion
    }
}
#endif
