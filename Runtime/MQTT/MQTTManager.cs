using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using UnityEngine;
#if UNITY_EDITOR||!UNITY_WEBGL
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
#endif

#if UNITY_WEBGL
using NonsensicalKit.Core.Service.Config;
using NonsensicalKit.WebGL;
#endif

/// <summary>
/// MQTT管理器
/// </summary>
[ServicePrefab("Services/MQTTManager")]
public class MQTTManager : NonsensicalMono, IMonoService
{
    [SerializeField, BoxGroup("MQTT链接")] private string MQTTPrefix = "ws://";
    [SerializeField, BoxGroup("MQTT链接")] private string MQTTURI = "broker.emqx.io";
    [SerializeField, BoxGroup("MQTT链接")] private int MQTTPort = 1883;
    [SerializeField, BoxGroup("MQTT链接")] private string MQTTPath;
    [SerializeField, BoxGroup("MQTT链接")] private string MQTTUser = "";
    [SerializeField, BoxGroup("MQTT链接")] private string MQTTPassword = "";

    [SerializeField] private bool m_differenceOnWebGL;
    [SerializeField, BoxGroup("WebGLMQTT链接"), ShowIf("m_differenceOnWebGL")] private string m_WebMQTTPrefix = "ws://";
    [SerializeField, BoxGroup("WebGLMQTT链接"), ShowIf("m_differenceOnWebGL")] private string m_WebMQTTURI = "broker.emqx.io";
    [SerializeField, BoxGroup("WebGLMQTT链接"), ShowIf("m_differenceOnWebGL")] private int m_WebMQTTPort = 1883;
    [SerializeField, BoxGroup("WebGLMQTT链接"), ShowIf("m_differenceOnWebGL")] private string m_WebMQTTPath;
    [SerializeField, BoxGroup("WebGLMQTT链接"), ShowIf("m_differenceOnWebGL")] private string m_WebMQTTUser = "";
    [SerializeField, BoxGroup("WebGLMQTT链接"), ShowIf("m_differenceOnWebGL")] private string m_WebMQTTPassword = "";


    [SerializeField] private bool m_log;
    [SerializeField] private bool m_recordTopic;

    [SerializeField, ShowIf("m_recordTopic")]
    private bool m_showTopics;

    [SerializeField, Label("重连间隔时间(s)")] private float ReconnectGapTime = 10;
    [SerializeField] private bool m_useTLS;

    [SerializeField, ShowIf("m_showTopics"), ReadOnly, Label("已订阅的主题")]
    private List<string> _subscribeTopics = new List<string>();

    [SerializeField, Label("MQTT链接状态"), ReadOnly]
    private MQTTStatus _status;

    private int _failCount;
    private float _waitTime;
    private string _clientID;
#if UNITY_EDITOR||!UNITY_WEBGL
    private IMqttClient _client;
#endif
    private Task _connectTask;

    public Action<string, string> MessageReceived;

    public bool IsReady { get; private set; }
    public Action InitCompleted { get; set; }

    public MQTTStatus Status => _status;

    private void Awake()
    {
        IsReady = false;
        Init();
    }

    private void Init()
    {
        _clientID = Guid.NewGuid().ToString();
        Debug.Log(Application.platform);
        if (PlatformInfo.IsWindow || PlatformInfo.IsEditor)
        {
            _connectTask = Task.Run(InitPCMQTT);
        }
        else if (PlatformInfo.IsWebGL)
        {
            Debug.Log("EnterWebGl");
            Subscribe<string[]>("WebBridge", "WebMQTTInit", InitWebMQTT);
            Subscribe<string[]>("WebBridge", "MQTTMessageConnectSuccess", OnWebMQTTConnectSuccess);
        }
    }

    void Update()
    {
#if UNITY_EDITOR||!UNITY_WEBGL
        if (IsReady && _status == MQTTStatus.ConnectFailed)
        {
            _waitTime += Time.deltaTime;
            if (_waitTime > ReconnectGapTime)
            {
                _waitTime = 0;
                Reconnect();
            }
        }
#endif
    }

    private void OnApplicationQuit()
    {
#if UNITY_EDITOR||!UNITY_WEBGL
        _connectTask?.Dispose();
        _client?.Dispose();
#else
        if (PlatformInfo.IsWebGL)
        {
            WebMQTT.Instance.End();
        }
#endif
    }

    private void InitPCMQTT()
    {
#if UNITY_EDITOR||!UNITY_WEBGL
        MqttClientOptionsBuilder builder = new MqttClientOptionsBuilder()
            .WithTcpServer(MQTTURI, MQTTPort) // 要访问的mqtt服务端的 ip 和 端口号
            .WithCredentials(MQTTUser, MQTTPassword) // 要访问的mqtt服务端的用户名和密码
            .WithClientId(_clientID) // 设置客户端id
            .WithCleanSession()
            .WithTlsOptions(new MqttClientTlsOptions()
            {
                UseTls = m_useTLS
            });

        MqttClientOptions clientOptions = builder.Build();
        _client = new MqttFactory().CreateMqttClient();

        _client.ConnectedAsync += Client_ConnectedAsync; // 客户端连接成功事件
        _client.DisconnectedAsync += Client_DisconnectedAsync; // 客户端连接关闭事件
        _client.ApplicationMessageReceivedAsync += Client_ApplicationMessageReceivedAsync;
        // 收到消息事件

        _status = MQTTStatus.Connecting;
        _client.ConnectAsync(clientOptions);

        IOCC.Set<MQTTManager>("MQTTManager", this);
        IsReady = true;
        InitCompleted?.Invoke();
#endif
    }

    protected virtual void InitWebMQTT(string[] _)
    {
#if UNITY_WEBGL
        Debug.Log("InitWebMQTT: WebMQTT.Instance == null :" + WebMQTT.Instance == null);
        if (m_differenceOnWebGL)
        {
            WebMQTT.Instance.Connect($"{m_WebMQTTPrefix}{m_WebMQTTURI}:{m_WebMQTTPort}{m_WebMQTTPath}", m_WebMQTTUser, m_WebMQTTPassword);
        }
        else
        {
            WebMQTT.Instance.Connect($"{MQTTPrefix}{MQTTURI}:{MQTTPort}{MQTTPath}", MQTTUser, MQTTPassword);
        }

        IOCC.Subscribe<string, string>("MQTTMessage", OnWebMQTTMessageReceived);
#endif
    }


    #region PCMQTT 信息事件

#if UNITY_EDITOR||!UNITY_WEBGL
    /// <summary>
    /// 重新连接
    /// </summary>
    private void Reconnect()
    {
        Debug.LogWarning("重新连接");
        Task.Run(delegate()
        {
            _status = MQTTStatus.Connecting;
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

        if (m_log) Debug.Log("客户端收到消息：" + arg.ApplicationMessage.Topic + "=====" + str);

        MessageReceived?.Invoke(arg.ApplicationMessage.Topic, str);

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
        _status = MQTTStatus.ConnectFailed;
        _failCount++;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 连接成功事件
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task Client_ConnectedAsync(MqttClientConnectedEventArgs arg)
    {
        _status = MQTTStatus.Connected;
        Debug.Log("MQTT连接成功");
        _failCount = 0;

        return Task.CompletedTask;
    }
#endif

    #endregion


    #region WebGLMQTT 消息事件

    private void OnWebMQTTMessageReceived(string toipc, string message)
    {
        if (m_log) Debug.Log("客户端收到消息：" + toipc + "=====" + message);
        MessageReceived?.Invoke(toipc, message);
    }

    private void OnWebMQTTConnectSuccess(string[] _)
    {
        IOCC.Set<MQTTManager>("MQTTManager", this);
        Debug.Log("MQTTMessageConnectSuccess");
        _status = MQTTStatus.Connected;
        IsReady = true;
        InitCompleted?.Invoke();
    }

    #endregion

    #region 发布消息

#if UNITY_EDITOR||!UNITY_WEBGL
    public void PublishAsync(MqttApplicationMessage message)
    {
        if (!IsReady) return;
        if (PlatformInfo.IsWindow || PlatformInfo.IsEditor)
        {
            _client.PublishAsync(message);
        }
        else if (PlatformInfo.IsWebGL)
        {
#if UNITY_WEBGL
            if (message.PayloadSegment.Array != null) WebMQTT.Instance.SendMessage(message.Topic, Encoding.UTF8.GetString(message.PayloadSegment.Array));
#endif
        }
    }

    public void PublishAsync(string topic, string message, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtLeastOnce)
    {
        if (!IsReady) return;
        if (PlatformInfo.IsWindow || PlatformInfo.IsEditor)
        {
            var mqttApplicationMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(message)
                .WithQualityOfServiceLevel(level) // 可选：设置QoS级别
                .WithRetainFlag(false) // 可选：是否保留消息
                .Build();

            _client.PublishAsync(mqttApplicationMessage);
        }
        else if (PlatformInfo.IsWebGL)
        {
#if UNITY_WEBGL
            WebMQTT.Instance.SendMessage(topic, message);
#endif
        }

        if (m_log) Debug.Log($"客户端发布：Published message: {message} to topic: {topic}");
    }

    public async void PublishAsync(Dictionary<string, string> messages, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtLeastOnce)
    {
        if (!IsReady) return;

        try
        {
            if (PlatformInfo.IsWindow || PlatformInfo.IsEditor)
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
                    if (m_log) Debug.Log($"Published message: {Encoding.UTF8.GetString(message.PayloadSegment)} to topic: {message.Topic}");
                }
            }
            else
            {
                foreach (var item in messages.Keys)
                {
#if UNITY_WEBGL
                    WebMQTT.Instance.SendMessage(item, messages[item]);
#endif
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("发布多个消息异常：" + e.Message);
        }
    }
#else
    public void PublishAsync(string topic, string message)
    {
        if (!IsReady) return;

        WebMQTT.Instance.SendMessage(topic, message);
        if (m_log) Debug.Log($"客户端发布：Published message: {message} to topic: {topic}");
    }
#endif

    #endregion

    #region 订阅消息

#if UNITY_EDITOR||!UNITY_WEBGL
    public void SubscribeAsync(MqttClientSubscribeOptions options, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtLeastOnce)
    {
        if (IsReady)
        {
            if (PlatformInfo.IsWindow || PlatformInfo.IsEditor)
            {
                _client.SubscribeAsync(options);
            }
            else if (PlatformInfo.IsWebGL)
            {
                var list = options.TopicFilters.FindAll(x => _subscribeTopics.Contains(x.Topic) == false).Select(y => y.Topic).ToList();
                if (list is { Count: > 0 })
                {
                    foreach (var topic in list)
                    {
#if UNITY_WEBGL
                        WebMQTT.Instance.Subscribe(topic);
#endif
                    }
                }
            }

            if (m_recordTopic) _subscribeTopics.AddRange(options.TopicFilters.FindAll(x => _subscribeTopics.Contains(x.Topic) == false).Select(y => y.Topic).ToList());
        }
    }

    public void SubscribeAsync(string topic, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtLeastOnce)
    {
        if (!IsReady) return;
        if (PlatformInfo.IsWindow || PlatformInfo.IsEditor)
        {
            var topicFilter = new MqttTopicFilterBuilder()
                .WithTopic(topic)
                .WithQualityOfServiceLevel(level)
                .Build();
            _client.SubscribeAsync(topicFilter);
        }
        else if (PlatformInfo.IsWebGL)
        {
#if UNITY_WEBGL
            WebMQTT.Instance.Subscribe(topic);
#endif
        }

        if (m_recordTopic == false || _subscribeTopics.Contains(topic)) return;
        _subscribeTopics.Add(topic);
    }

    public void SubscribeAsync(string[] topics, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtLeastOnce)
    {
        if (!IsReady) return;
        if (PlatformInfo.IsWindow || PlatformInfo.IsEditor)
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
        else if (PlatformInfo.IsWebGL)
        {
            foreach (var topic in topics)
            {
#if UNITY_WEBGL
                WebMQTT.Instance.Subscribe(topic);
#endif
            }
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

#else
    public void SubscribeAsync(string topic)
    {
        if (!IsReady) return;
        WebMQTT.Instance.Subscribe(topic);
        if (m_recordTopic == false || _subscribeTopics.Contains(topic)) return;
        _subscribeTopics.Add(topic);
    }

    public void SubscribeAsync(string[] topics)
    {
        if (!IsReady) return;

        foreach (var topic in topics)
        {
            WebMQTT.Instance.Subscribe(topic);
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
#endif

    #endregion

    #region 取消订阅消息

#if UNITY_EDITOR||!UNITY_WEBGL
    public void UnsubscribeAsync(MqttClientUnsubscribeOptions options)
    {
        if (IsReady)
        {
            _client.UnsubscribeAsync(options);
            foreach (var item in options.TopicFilters)
            {
                _subscribeTopics.Remove(item);
            }
        }
    }

    public void UnsubscribeAsync(params string[] topics)
    {
        if (!IsReady) return;
        var topicFilter = new MqttClientUnsubscribeOptions()
        {
            TopicFilters = topics.ToList()
        };

        _client.UnsubscribeAsync(topicFilter);
        foreach (var item in topics)
        {
            _subscribeTopics.Remove(item);
        }
    }
#else
    public void UnsubscribeAsync(params string[] topics)
    {
        if (!IsReady) return;
    }
#endif

    #endregion

#if UNITY_EDITOR||!UNITY_WEBGL
    public IMqttClient CreateMQTTClient(MqttClientOptionsBuilder builderInfo)
    {
        MqttClientOptions options = builderInfo.Build();
        IMqttClient temp = new MqttFactory().CreateMqttClient();
        temp.ConnectAsync(options);
        temp.ConnectedAsync += Client_ConnectedAsync;
        temp.DisconnectedAsync += Client_DisconnectedAsync; // 客户端连接关闭事件
        return temp;
    }
#endif
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