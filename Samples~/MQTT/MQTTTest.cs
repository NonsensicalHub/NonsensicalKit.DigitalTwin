using System;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.DigitalTwin.MQTT;
using UnityEngine;

public class MQTTTest : MonoBehaviour
{
    [SerializeField] private bool useGUI;

    [SerializeField, ResizableTextArea, Label("主题")]
    private string m_topic;

    [SerializeField, ResizableTextArea, Label("消息")]
    private string m_message;

    [SerializeField, TextArea(minLines: 5, maxLines: 25), BoxGroup("接收消息")]
    private string m_receivedTopic;

    private MqttService m_manager;
    GUIStyle m_Style;
    private Vector2 scrollPos; // 用于保存滚动位置

    private void Awake()
    {
        ServiceCore.SafeGet<MqttService>(SafeGetManager);
        IOCC.Subscribe<string, string>("receivedMQTTData", MessageReceived);
    }

    private void SafeGetManager(MqttService obj)
    {
        m_manager = obj;
    }


    private bool _addListener;

    [Button("订阅主题")]
    public void SubscribeTopic()
    {
        m_manager?.Manager.SubscribeAsync(m_topic);
        if (_addListener == false)
        {
            if (m_manager != null)
            {
                _addListener = true;
                m_manager.Manager.MessageReceived += MessageReceived;
            }
        }

        m_receivedTopic += $"----------{DateTime.Now:hh:mm:ss}----------\n" +
                           $"订阅主题： {m_topic}\n" +
                           $"+++++++++++++++++++++++++++++++++++++\n";
    }

    [Button("取消订阅主题")]
    public void UnSubscribeTopic()
    {
#if UNITY_EDITOR||!UNITY_WEBGL
        m_manager?.Manager.UnsubscribeAsync(m_topic);
#else
        IOCC.Publish("SendMessageToJS", "MQTT", new string[] { "UnSubscribe", m_topic });
#endif
    }

    [Button("发布消息")]
    public void PublishMessage()
    {
        m_manager?.Manager.PublishAsync(m_topic, m_message);
    }

    [Button("清除消息区")]
    public void Clear()
    {
        m_receivedTopic = string.Empty;
    }


    private void MessageReceived(string arg1, string arg2)
    {
        // Debug.Log($"Received Message: {arg1}, {arg2}");
        m_receivedTopic += $"----------{DateTime.Now:hh:mm:ss}----------\n" +
                           $"接收主题： {arg1}\n" +
                           $"接收消息： {arg2}\n" +
                           "+++++++++++++++++++++++++++++++++++++\n";
    }


    private void OnGUI()
    {
        if (useGUI == false) return;

        if (m_Style == null)
        {
            m_Style = GUI.skin.label;
            m_Style.fontSize = 20;
        }

        GUILayout.BeginVertical("Box");

        GUILayout.Label("MQTT Test", m_Style);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Topic:", GUILayout.Width(60));
        m_topic = GUILayout.TextField(m_topic, GUILayout.Width(200));
        if (GUILayout.Button("Subscribe", GUILayout.Width(80)))
        {
            SubscribeTopic();
        }

        if (GUILayout.Button("UnSubscribe", GUILayout.Width(80)))
        {
            UnSubscribeTopic();
        }

        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Message:", GUILayout.Width(60));
        m_message = GUILayout.TextField(m_message, GUILayout.Width(200));
        if (GUILayout.Button("PublishMessage", GUILayout.Width(80)))
        {
            PublishMessage();
        }

        GUILayout.EndHorizontal();

        if (GUILayout.Button("ClearMessage", GUILayout.Width(150)))
        {
            Clear();
        }

        if (GUILayout.Button("PrintSubscribeTopics", GUILayout.Width(150)))
        {
            var a = (m_manager ?? IOCC.Get<MqttService>("MQTTService"))?.Manager.ShowSubscribedTopics();
            string b = "";
            foreach (var str in a)
            {
                b += str + "\n";
            }

            m_receivedTopic += $"----------{DateTime.Now:hh:mm:ss}----------\n" +
                               $"SubscribedTopic: \n{b}\n" +
                               "+++++++++++++++++++++++++++++++++++++\n";
        }

        GUILayout.Label("ReceivedTopic:");
        /*GUILayout.TextArea(m_receivedTopic, GUILayout.Height(200));

        GUILayout.EndVertical();*/
        scrollPos = GUILayout.BeginScrollView(scrollPos, false, true, GUILayout.Height(200));
        GUILayout.TextArea(m_receivedTopic, GUILayout.ExpandWidth(true));
        GUILayout.EndScrollView();

        GUILayout.EndVertical();
    }
}