using System;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin
{
    public class SocketClient : MonoBehaviour
    {
        [Header("WebSocket 设置")] [SerializeField]
        private int m_port = 9099;

        [SerializeField] private string m_msgID;

        [Header("限流：每秒最大消息数")] [SerializeField]
        private int m_maxMsgPerSecond = 60;

        public Action<string> OnGetSocketMsg;

        private WebSocketServer _server;


        public void StartServer()
        {
            _server = new WebSocketServer
            {
                Port = m_port,
                MaxMessagesPerSecond = m_maxMsgPerSecond
            };

            if (string.IsNullOrEmpty(m_msgID))
            {
                m_msgID = "default";
            }

            _server.StartServer();
        }

        private void Update()
        {
            if (_server == null) return;

            lock (_server.MainThreadMsgQueue)
            {
                while (_server.MainThreadMsgQueue.Count > 0)
                {
                    string msg = _server.MainThreadMsgQueue.Dequeue();
                    HandleMessage(msg);
                }
            }
        }

        private void HandleMessage(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            IOCC.Publish("GetSocketMessage", msg);
            IOCC.PublishWithID("GetSocketMessage", m_msgID, msg);
            OnGetSocketMsg?.Invoke(msg);
            //Debug.Log(msg);
        }

        private void OnApplicationQuit() => _server?.StopServer();
        private void OnDestroy() => _server?.StopServer();
    }
}
