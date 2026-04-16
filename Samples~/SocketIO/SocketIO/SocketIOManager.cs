using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Log;
using SocketIOClient;
using SocketIOClient.Newtonsoft.Json;
using SocketIOClient.Transport;
using UnityEngine;
#if NonsensicalkitWebgl
using NonsensicalKit.WebGL;
#endif

namespace NonsensicalKit
{
    public partial class SocketIOManager : NonsensicalMono
    {
        private SocketIOUnity _socketIO;
        private AutoSendMessage[] _autoSendMessage;

        private string _ID;

        private void Awake()
        {
#if UNITY_EDITOR&&! NonsensicalkitWebgl
            if (PlatformInfo.IsWebGL)   
            {
                Debug.LogError("在WebGL平台使用SocketIOManager需要导入NonsensicalKit.Webgl包");
            }
#endif
            _ID = Guid.NewGuid().ToString();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_socketIO != null)
            {
                _socketIO.Disconnect();
            }
        }

        public void Init(SocketIOConfigData data)
        {
            if (data == null)
            {
                LogCore.Error("不能使用空数据进行初始化");
                return;
            }

            _autoSendMessage = data.AutoSend;
            Subscribe<string, string>("socketIOEmit", Emit);
            if (PlatformInfo.IsEditor)
            {
                _socketIO = new SocketIOUnity(new Uri(data.SocketIOUrl), new SocketIOOptions
                {
                    Query = new Dictionary<string, string>
                    {
                        { "token", "UNITY" }
                    },
                    EIO = 4,
                    Transport = TransportProtocol.WebSocket
                });
                _socketIO.JsonSerializer = new NewtonsoftJsonSerializer();

                Debug.Log("Connecting...");
                AddListener("relay_u3d_data"); //四楼产线固定事件
                _socketIO.OnConnected += OnConnected;
                _socketIO.Connect();
                //socket.OnAnyInUnityThread((name, response) =>
                //{
                //    Debug.Log("Received On " + name + " : " + response.GetValue<string>() + "\n");
                //});
            }
            else
            {
                Debug.Log("开始连接SocketIO");

#if NonsensicalkitWebgl
                WebSocketIO.Instance.ConnectSocketIO(data.SocketIOUrl);
                WebSocketIO.Instance.SocketIOAddListener("relay_u3d_data"); //四楼产线固定事件
                WebSocketIO.Instance.SocketIOAddListener("connect");
                Subscribe<string, string>("socketIOMessage", OnReceiveMsg);
#endif
            }

            foreach (var item in data.EventNames)
            {
                AddListener(item);
            }
        }

        private void OnConnected(object n, EventArgs r)
        {
            Debug.Log("Connected");
            AutoSend();
        }

        private void AutoSend()
        {
            foreach (var item in _autoSendMessage)
            {
                Emit(item.Key, item.Value);
            }
        }

        private void AddListener(string eventName)
        {
            Debug.Log("添加监听:" + eventName);
            if (PlatformInfo.IsEditor)
            {
                _socketIO.OnUnityThread(eventName, (response) =>
                {
                    //Debug.Log(response.GetValue<string>());
                    PublishData(eventName, response.GetValue<string>());
                });
            }
            else
            {
#if NonsensicalkitWebgl
                WebSocketIO.Instance.SocketIOAddListener(eventName);
#endif
            }
        }

        private void Emit(string eventName, string msg)
        {
            Debug.Log("发送消息:" + eventName + " — " + msg);
            if (PlatformInfo.IsEditor)
            {
                if (!IsJSON(msg))
                {
                    _socketIO.Emit(eventName, msg);
                }
                else
                {
                    _socketIO.EmitStringAsJSON(eventName, msg);
                }
            }
            else
            {
#if NonsensicalkitWebgl
                WebSocketIO.Instance.SocketIOSendMessageWithCallback(eventName, msg);
#endif
            }
        }

        private bool IsJSON(string str)
        {
            if (string.IsNullOrWhiteSpace(str)) { return false; }

            str = str.Trim();
            if ((str.StartsWith("{") && str.EndsWith("}")) || //For object
                (str.StartsWith("[") && str.EndsWith("]"))) //For array
            {
                try
                {
                    var obj = JToken.Parse(str);
                    return true;
                }
                catch (Exception ex) //some other exception
                {
                    Console.WriteLine(ex.ToString());
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        private void OnReceiveMsg(string key, string value)
        {
            if (string.Equals(key, "connect"))
            {
                AutoSend();
            }

            PublishData(key, value);
        }

        private void PublishData(string key, string value)
        {
            //Debug.Log("socektIOMessage:" + key + "_" + value);
            PublishWithID("socketIOMsg", key, value);
        }
    }
}
