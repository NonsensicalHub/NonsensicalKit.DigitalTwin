using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Motion;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NonsensicalKit
{
    public class SocketIOToPointData : NonsensicalMono
    {
        [SerializeField] private TextAsset m_autoData;
        [SerializeField] private string key;

        private bool _recording;
        private bool _playing;

        private List<List<PointData>> _records = new List<List<PointData>>();

        private void Awake()
        {
            Subscribe<string>("socketIOMsg", key, OnSocketIOMessage);
            Subscribe< TextAsset>("playRecordPLCData", OnPlayRecordData);
            Subscribe< string>("playRecordPLCData", OnPlayRecordData);

            if (m_autoData != null)
            {
                OnPlayRecordData(m_autoData);
            }
        }

        public void Record()
        {
            if (_playing)
            {
                return;
            }
            _records.Clear();
            _recording = true;
        }

        public void Load()
        {
            var v = File.ReadAllText("D://record.txt");
            if (string.IsNullOrEmpty(v))
            {
                return;
            }
            OnPlayRecordData(v,false);
        }

        private IEnumerator Play(bool loop = false)
        {
            do
            {
                Debug.Log("playStart");
                _playing = true;
                if (_records.Count == 0)
                {
                    yield break;
                }
                int crtIndex = 0;
                long baseTicks = _records[0][0].Ticks;
                float timer = 0;
                while (crtIndex < _records.Count)
                {
                    while (crtIndex < _records.Count)
                    {
                        var v = _records[crtIndex];
                        if (((v[0].Ticks - baseTicks) / 1000f) > timer)
                        {
                            break;
                        }
                        Publish<IEnumerable<PointData>>("receivePoints", v);
                        crtIndex++;
                    }
                    yield return null;
                    if (loop)
                    {
                        timer += Time.deltaTime * 11;
                    }
                    else
                    {
                        timer += Time.deltaTime;
                    }
                }
                _playing = false;
                Debug.Log("playEnd");
            } while (loop);
        }

        public void Save()
        {
            File.WriteAllText("D://record.txt", NonsensicalKit.Tools.JsonTool.SerializeObject(_records));
        }

        private void OnPlayRecordData(TextAsset text)
        {
            OnPlayRecordData(text.text);

        }
        private void OnPlayRecordData(string str)
        {
            OnPlayRecordData(str,true);
        }
        private void OnPlayRecordData(string str,bool loop)
        {
            _records = NonsensicalKit.Tools.JsonTool.DeserializeObject<List<List<PointData>>>(str);
            StartCoroutine(Play(loop));
        }

        private void OnSocketIOMessage(string msg)
        {
            if (string.IsNullOrEmpty(msg))
            {
                return;
            }
            Debug.Log(msg);
            List<PointData> points;
            try
            {
                points = NonsensicalKit.Tools.JsonTool.DeserializeObject<List<PointData>>(msg);
            }
            catch (System.Exception)
            {
                Debug.Log("数据转换失败");
                return;
            }
            for (int i = 0; i < points.Count; i++)
            {
                PointData p = points[i];
                if (p == null || p.Value == null)
                {

                    points.RemoveAt(i);
                    i--;
                    continue;
                }
                if (p.Value.ToString().Length > 5 && p.Value.ToString().Substring(0, 5) == "Redis")
                {
                    points.RemoveAt(i);
                    i--;
                    continue;
                }
            }
            if (_recording)
            {
                _records.Add(points);
            }
            if (!_playing)
            {
                Publish<IEnumerable<PointData>>("receivePoints", points);
            }
        }
    }
}
