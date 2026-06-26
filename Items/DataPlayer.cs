using System;
using System.Collections;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin
{
    public class DataPlayer : NonsensicalMono
    {
        [SerializeField] private TextAsset m_dataFile;
        [SerializeField] private float m_timeScale = 1f;

        [ShowNonSerializedField] private string _playTime = string.Empty;
        [ShowNonSerializedField] private int _playIndex;

        [Button]
        public void Play()
        {
            StartCoroutine(PlayData());
        }

        private IEnumerator PlayData()
        {
            var lines = m_dataFile.text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            _playTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            _playIndex = 0;

            long crtTicks = -1;
            while (_playIndex < lines.Length)
            {
                var line = lines[_playIndex];
                if (line.Length < 52)
                {
                    _playIndex++;
                    continue;
                }

                _playTime = line[..23];
                var ticksStr = line[24..42];
                long ticks = long.Parse(ticksStr); // 转成 long
                var keyStr = line[43..48];
                var eventStr = line[49..51];
                var msgStr = line[52..];

                if (crtTicks < 0)
                {
                    crtTicks = ticks;
                }

                if (crtTicks >= ticks)
                {
                    switch (eventStr)
                    {
                        case "MQ":
                        {
                            var args = msgStr.Split('|');
                            PublishWithID("MQTTMessage", keyStr, args[0].Trim(), args[1].Trim());
                            break;
                        }
                        case "SR": PublishWithID("SignalRMessage", keyStr, msgStr); break;
                    }

                    _playIndex++;
                }

                while (crtTicks < ticks)
                {
                    yield return null;
                    crtTicks += (long)(Time.deltaTime * 10000000 * m_timeScale);
                }
            }
        }
    }
}
