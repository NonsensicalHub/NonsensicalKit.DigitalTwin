#if UNITY_EDITOR||!UNITY_WEBGL
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MQTT
{
    public partial class MqttManager
    {
        private Task _connectTask;
        private IMqttClient _client;
        private List<(string, string)> _messages = new();


        public partial void Run()
        {
            _clientID = Guid.NewGuid().ToString();
            _connectTask = Task.Run(Init);
        }

        private void Update()
        {
            if (_connectTask != null && Status == MQTTStatus.ConnectFailed)
            {
                _waitTime += Time.deltaTime;
                if (_waitTime > ReconnectGapTime)
                {
                    _waitTime = 0;
                    Reconnect();
                }
            }

            if (!ForwardFirstTime)
            {
                foreach (var (topic, message) in _messages)
                {
                    Send(topic, message);
                }

                _messages.Clear();
            }
        }

        private partial void OnApplicationQuit()
        {
            _connectTask?.Dispose();
            _client?.Dispose();
        }

        private void Send(string topic, string message)
        {
            MessageReceived?.Invoke(topic, message);
            Publish("MQTTReceiveData", topic, message);
            Publish("MQTTReceiveData", MQTTURI, topic, message);
        }

        private void Init()
        {
            MqttClientOptionsBuilder builder = new MqttClientOptionsBuilder()
                .WithCredentials(MQTTUser, MQTTPassword) // 要访问的mqtt服务端的用户名和密码
                .WithClientId(_clientID) // 设置客户端id
                .WithCleanSession()
                .WithTlsOptions(new MqttClientTlsOptions()
                {
                    UseTls = UseTLS
                });
            if (IsWebSocketConnectionType)
            {
                builder.WithWebSocketServer(o => o.WithUri($"{MQTTPrefix}{MQTTURI}:{MQTTPort}{MQTTSuffix}"));
            }
            else
            {
                builder.WithTcpServer($"{MQTTPrefix}{MQTTURI}", MQTTPort);
            }

            Debug.Log($"{MQTTPrefix}{MQTTURI}:{MQTTPort}{MQTTSuffix}");

            MqttClientOptions clientOptions = builder.Build();
            _client = new MqttFactory().CreateMqttClient();

            _client.ConnectedAsync += Client_ConnectedAsync; // 客户端连接成功事件
            _client.DisconnectedAsync += Client_DisconnectedAsync; // 客户端连接关闭事件
            _client.ApplicationMessageReceivedAsync += Client_ApplicationMessageReceivedAsync;
            // 收到消息事件

            Status = MQTTStatus.Connecting;
            _client.ConnectAsync(clientOptions);
        }

        /// <summary>
        /// 重新连接
        /// </summary>
        private void Reconnect()
        {
            Debug.LogWarning("重新连接");
            Task.Run(delegate()
            {
                Status = MQTTStatus.Connecting;
                _client.ReconnectAsync();
            });
        }

        /// <summary>
        /// 新消息事件
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        private Task Client_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
        {
            string str = Encoding.UTF8.GetString(arg.ApplicationMessage.PayloadSegment.Array);

            if (Log) Debug.Log("客户端收到消息：" + arg.ApplicationMessage.Topic + "=====" + str);
            if (ForwardFirstTime)
            {
                Send(arg.ApplicationMessage.Topic, str);
            }
            else
            {
                _messages.Add((arg.ApplicationMessage.Topic, str));
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 连接断开事件
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        private Task Client_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
        {
            Debug.Log("MQTT连接断开:" + arg.Reason);
            Status = MQTTStatus.ConnectFailed;
            return Task.CompletedTask;
        }

        /// <summary>
        /// 连接成功事件
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        private Task Client_ConnectedAsync(MqttClientConnectedEventArgs arg)
        {
            Status = MQTTStatus.Connected;
            Debug.Log("MQTT已连接:  " + MQTTURI);
            foreach (var item in SubscribeTopics)
            {
                SubscribeAsync(item.Key, item.Value);
            }


            return Task.CompletedTask;
        }

        #region 发布消息

        public void PublishAsync(MqttApplicationMessage message)
        {
            _client.PublishAsync(message);
        }

        public void PublishAsync(string topic, string message, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtLeastOnce)
        {
            var mqttApplicationMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(message)
                .WithQualityOfServiceLevel(level) // 可选：设置QoS级别
                .WithRetainFlag(false) // 可选：是否保留消息
                .Build();

            _client.PublishAsync(mqttApplicationMessage);
            if (Log) Debug.Log($"客户端发布：Published message: {message} to topic: {topic}");
        }

        public async void PublishAsync(Dictionary<string, string> messages, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtLeastOnce)
        {
            try
            {
                var applicationMessages = messages.Select(kv => new MqttApplicationMessageBuilder()
                        .WithTopic(kv.Key)
                        .WithPayload(kv.Value)
                        .WithQualityOfServiceLevel(level) // 可选：设置QoS级别
                        .WithRetainFlag(false) // 可选：是否保留消息
                        .Build())
                    .ToList();

                foreach (var message in applicationMessages)
                {
                    await _client.PublishAsync(message);
                    if (Log) Debug.Log($"Published message: {Encoding.UTF8.GetString(message.PayloadSegment)} to topic: {message.Topic}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("发布多个消息异常：" + e.Message);
            }
        }

        #endregion

        #region 订阅消息

        public void SubscribeAsync(MqttClientSubscribeOptions options, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtLeastOnce)
        {
            if (_client != null && Status == MQTTStatus.Connected)
            {
                _client.SubscribeAsync(options);
            }

            foreach (var item in options.TopicFilters)
            {
                TryAddTopic(item.Topic, level);
            }
        }

        public void SubscribeAsync(string topic, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtLeastOnce)
        {
            if (_client != null && Status == MQTTStatus.Connected)
            {
                var topicFilter = new MqttTopicFilterBuilder()
                    .WithTopic(topic)
                    .WithQualityOfServiceLevel(level)
                    .Build();
                _client.SubscribeAsync(topicFilter);
            }

            TryAddTopic(topic, level);
        }

        public void SubscribeAsync(string[] topics, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtLeastOnce)
        {
            if (_client != null && Status == MQTTStatus.Connected)
            {
                var topicFilters = new MqttClientSubscribeOptions
                {
                    TopicFilters = topics.Select(topic => new MqttTopicFilterBuilder()
                            .WithTopic(topic)
                            .WithQualityOfServiceLevel(level)
                            .Build())
                        .ToList()
                };
                _client.SubscribeAsync(topicFilters);
            }

            foreach (var item in topics)
            {
                TryAddTopic(item, level);
            }
        }

        #endregion

        #region 取消订阅消息

        public void UnsubscribeAsync(MqttClientUnsubscribeOptions options)
        {
            _client.UnsubscribeAsync(options);
            foreach (var item in options.TopicFilters)
            {
                SubscribeTopics.Remove(item);
            }
        }

        public void UnsubscribeAsync(params string[] topics)
        {
            var topicFilter = new MqttClientUnsubscribeOptions()
            {
                TopicFilters = topics.ToList()
            };

            _client.UnsubscribeAsync(topicFilter);
            foreach (var item in topics)
            {
                SubscribeTopics.Remove(item);
            }
        }

        #endregion

        public IMqttClient CreateMQTTClient(MqttClientOptionsBuilder builderInfo)
        {
            MqttClientOptions options = builderInfo.Build();
            IMqttClient temp = new MqttFactory().CreateMqttClient();
            temp.ConnectAsync(options);
            temp.ConnectedAsync += Client_ConnectedAsync;
            temp.DisconnectedAsync += Client_DisconnectedAsync; // 客户端连接关闭事件
            return temp;
        }

        private void TryAddTopic(string topic, MqttQualityOfServiceLevel level)
        {
            //！！！ 不能改为TryAdd，会触发迭代时修改异常
            if (SubscribeTopics.ContainsKey(topic) == false)
            {
                SubscribeTopics.Add(topic, level);
            }
        }
    }
}
#endif
