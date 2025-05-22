using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 收集数据并缓存，然后在合适的时机进行分发
    /// </summary>
    public class DataDispenser : NonsensicalMono, IMonoService
    {
        [FormerlySerializedAs("logNonExistentPoint")] [Tooltip("接收到未配置过的点位时输出日志")] [SerializeField]
        private bool m_logNonExistentPoint;

        /// <summary>
        /// 部件监听字典
        /// 部件id为键，监听部件的事件为值，动态修改
        /// </summary>
        private readonly Dictionary<string, Action<List<PointData>>> _partDataReceive = new();

        /// <summary>
        /// 部件点位缓存字典
        /// 部件id为键，所属点位缓存字典为值，初始化时写入，之后不再修改
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, PointDataBuffer>> _partPointsPair = new();

        /// <summary>
        /// 点位部件字典
        /// 点id为键，所属部件id链表为值，初始化时写入，之后不再修改
        /// 存在同一个点由多个部件使用的情况，如传送带上多个停止点，每个停止点都需要获取本停止点和下一个停止点的信息来判断状态
        /// </summary>
        private readonly Dictionary<string, List<string>> _pointPartsPair = new();

        /// <summary>
        /// 每次报警后缓存点位，防止重复报警
        /// </summary>
        private readonly HashSet<string> _loggedPoints = new();

        public bool IsReady { get; set; }

        public Action InitCompleted { get; set; }

        private bool _debugMode;

        private void Awake()
        {
            Subscribe<IEnumerable<PartConfig>>("partConfigInit", Init);
            Subscribe<string, Action<List<PointData>>>("addPartListener", AddPartListener);
            Subscribe<string, Action<List<PointData>>>("removePartListener", RemovePartListener);
            Subscribe<IEnumerable<PointData>>("ReceivePoints", ReceivePoints);
            Subscribe<PointData>("receivePoint", ReceivePoint);
            IsReady = true;
            InitCompleted?.Invoke();
        }

        public void StartDebug()
        {
            _debugMode = true;
        }

        public void StopDebug()
        {
            _debugMode = false;
        }

        private void Init(IEnumerable<PartConfig> configs)
        {
            List<string> errorID = new List<string>();
            foreach (var config in configs)
            {
                string partID = config.ConfigID;
                if (_partPointsPair.ContainsKey(partID))
                {
                    errorID.Add("重复的部件ID" + partID);
                    continue;
                }

                Dictionary<string, PointDataBuffer> pointIDs = new Dictionary<string, PointDataBuffer>();
                foreach (var point in config.pointConfigs)
                {
                    if (pointIDs.ContainsKey(point.pointID))
                    {
                        errorID.Add(partID + "部件中重复的点位ID" + point);
                        continue;
                    }

                    pointIDs.Add(point.pointID, new PointDataBuffer(null, false));
                }

                _partPointsPair.Add(partID, pointIDs);

                foreach (var point in config.pointConfigs)
                {
                    if (_pointPartsPair.TryGetValue(point.pointID, out var value))
                    {
                        value.Add(partID);
                    }
                    else
                    {
                        _pointPartsPair.Add(point.pointID, new List<string>() { partID });
                    }
                }
            }

            if (errorID.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("点位配置有误");
                foreach (var item in errorID)
                {
                    sb.AppendLine(item);
                }

                Debug.Log(sb.ToString());
            }
        }

        /// <summary>
        /// 获取到一组点数据
        /// </summary>
        /// <param name="points"></param>
        private void ReceivePoints(IEnumerable<PointData> points)
        {
            //使用set防止重复调用事件
            HashSet<string> partids = new HashSet<string>();

            foreach (var point in points)
            {
                string pointID = point.pointID;
                if (_pointPartsPair.ContainsKey(pointID))
                {
                    var partIDs = _pointPartsPair[pointID];
                    foreach (var partID in partIDs)
                    {
                        //此处无需检测是否存在部件id，因为初始化时是双向的
                        _partPointsPair[partID][pointID].fresh = true;
                        _partPointsPair[partID][pointID].data = point;
                    }

                    foreach (var partID in partIDs)
                    {
                        partids.Add(partID);
                    }
                }
                else
                {
                    if (m_logNonExistentPoint
                        && _loggedPoints.Contains(pointID) == false)
                    {
                        _loggedPoints.Add(pointID);
                        Debug.Log($"未配置的点位：{pointID}，{point.name}");
                    }
                }
            }

            CheckParts(partids.ToList());
        }

        /// <summary>
        /// 获取到单个点数据
        /// </summary>
        /// <param name="pointData"></param>
        private void ReceivePoint(PointData pointData)
        {
            string pointID = pointData.pointID;
            if (_pointPartsPair.ContainsKey(pointID))
            {
                var partIDs = _pointPartsPair[pointID];
                foreach (var partID in partIDs)
                {
                    _partPointsPair[partID][pointID].fresh = true;
                    _partPointsPair[partID][pointID].data = pointData;
                }

                CheckParts(partIDs);
            }
            else
            {
                if (m_logNonExistentPoint
                    && _loggedPoints.Contains(pointID) == false)
                {
                    _loggedPoints.Add(pointID);
                    Debug.Log($"未配置的点位：{pointID}，{pointData.name}");
                }
            }
        }

        /// <summary>
        /// 检测part中的数据是否是都是新鲜的，新鲜的话就调用事件
        /// </summary>
        /// <param name="partIDs"></param>
        private void CheckParts(List<string> partIDs)
        {
            foreach (var partID in partIDs)
            {
                if (_partDataReceive.ContainsKey(partID))
                {
                    var dic = _partPointsPair[partID];
                    bool flag = true;

                    //遍历部件下的所有点，是否都是新鲜的
                    foreach (var pair in dic)
                    {
                        if (pair.Value.fresh == false)
                        {
                            flag = false;
                            break;
                        }
                    }

                    if (flag == false)
                    {
                        continue;
                    }

                    List<PointData> points = new List<PointData>();
                    foreach (var pair in dic)
                    {
                        points.Add(pair.Value.data);
                        pair.Value.fresh = false;
                    }

                    //foreach (var key in dic.Keys)
                    //{
                    //    points.Add(dic[key].data);
                    //    dic[key].fresh = false;
                    //}
                    _partDataReceive[partID].Invoke(points);
                }
            }

            if (_debugMode)
            {
                Publish("plcDebugData", _partPointsPair);
            }
        }

        /// <summary>
        /// 添加新的部件监听
        /// </summary>
        /// <param name="partID"></param>
        /// <param name="action"></param>
        private void AddPartListener(string partID, Action<List<PointData>> action)
        {
            if (_partDataReceive.ContainsKey(partID))
            {
                _partDataReceive[partID] += action;
            }
            else
            {
                _partDataReceive.Add(partID, action);
            }
        }

        /// <summary>
        /// 移除部件监听
        /// </summary>
        /// <param name="partID"></param>
        /// <param name="action"></param>
        private void RemovePartListener(string partID, Action<List<PointData>> action)
        {
            if (_partDataReceive.ContainsKey(partID))
            {
                _partDataReceive[partID] -= action;

                if (_partDataReceive[partID] == null)
                {
                    _partDataReceive.Remove(partID);
                }
            }
        }
    }

    /// <summary>
    /// 点数据缓存
    /// </summary>
    class PointDataBuffer
    {
        /// <summary>
        /// 点数据
        /// </summary>
        public PointData data;

        /// <summary>
        /// 是否新鲜（从上次调用事件后更新过）
        /// </summary>
        public bool fresh;

        public PointDataBuffer(PointData data, bool fresh)
        {
            this.data = data;
            this.fresh = fresh;
        }
    }
}
