using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.PLC
{
    /// <summary>
    /// 收集数据并缓存，然后在合适的时机进行分发
    /// </summary>
    public class DataDispenser : NonsensicalMono, IMonoService
    {
        [Tooltip("接收到未配置过的点位时输出日志")][SerializeField] private bool logNonExistentPoint = false;

        /// <summary>
        /// 部件监听字典
        /// 部件id为键，监听部件的事件为值，动态修改
        /// </summary>
        private Dictionary<string, Action<List<PLCPoint>>> partDataReceive = new Dictionary<string, Action<List<PLCPoint>>>();

        /// <summary>
        /// 部件点位缓存字典
        /// 部件id为键，所属点位缓存字典为值，初始化时写入，之后不再修改
        /// </summary>
        private Dictionary<string, Dictionary<string, PointDataBuffer>> partPointsPair = new Dictionary<string, Dictionary<string, PointDataBuffer>>();

        /// <summary>
        /// 点位部件字典
        /// 点id为键，所属部件id链表为值，初始化时写入，之后不再修改
        /// 存在同一个点由多个部件使用的情况，如传送带上多个停止点，每个停止点都需要获取本停止点和下一个停止点的信息来判断状态
        /// </summary>
        private Dictionary<string, List<string>> pointPartsPair = new Dictionary<string, List<string>>();

        /// <summary>
        /// 每次报警后缓存点位，防止重复报警
        /// </summary>
        private HashSet<string> loggedPoints = new HashSet<string>();

        public bool IsReady { get; set; }

        public Action InitCompleted { get; set; }

        private bool _debugMode;

        private void Awake()
        {
            Subscribe<IEnumerable<PartConfig>>("partConfigInit", Init);
            Subscribe<string, Action<List<PLCPoint>>>("addPartListener", AddPartListener);
            Subscribe<string, Action<List<PLCPoint>>>("removePartListener", RemovePartListener);
            Subscribe<IEnumerable<PLCPoint>>("receivePoints", ReceivePoints);
            Subscribe<PLCPoint>("receivePoint", ReceivePoint);
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
                if (partPointsPair.ContainsKey(partID))
                {
                    errorID.Add("重复的部件ID" + partID);
                    continue;
                }
                Dictionary<string, PointDataBuffer> pointIDs = new Dictionary<string, PointDataBuffer>();
                foreach (var item in config.pointIDs)
                {
                    if (pointIDs.ContainsKey(item))
                    {
                        errorID.Add(partID + "部件中重复的点位ID" + item);
                        continue;
                    }
                    pointIDs.Add(item, new PointDataBuffer(null, false));
                }
                partPointsPair.Add(partID, pointIDs);

                foreach (var pointID in config.pointIDs)
                {
                    if (pointPartsPair.ContainsKey(pointID))
                    {
                        pointPartsPair[pointID].Add(partID);
                    }
                    else
                    {
                        pointPartsPair.Add(pointID, new List<string>() { partID });
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
        private void ReceivePoints(IEnumerable<PLCPoint> points)
        {
            //使用set防止重复调用事件
            HashSet<string> partids = new HashSet<string>();

            foreach (var point in points)
            {
                string pointID = point.pointID;
                if (pointPartsPair.ContainsKey(pointID))
                {
                    var partIDs = pointPartsPair[pointID];
                    foreach (var partID in partIDs)
                    {
                        //此处无需检测是否存在部件id，因为初始化时是双向的
                        partPointsPair[partID][pointID].fresh = true;
                        partPointsPair[partID][pointID].data = point;
                    }
                    foreach (var partID in partIDs)
                    {
                        partids.Add(partID);
                    }
                }
                else
                {
                    if (logNonExistentPoint
                        && loggedPoints.Contains(pointID) == false)
                    {
                        loggedPoints.Add(pointID);
                        Debug.Log($"未配置的点位：{pointID}，{point.name}");
                    }
                }
            }

            CheckParts(partids.ToList());
        }

        /// <summary>
        /// 获取到单个点数据
        /// </summary>
        /// <param name="point"></param>
        private void ReceivePoint(PLCPoint point)
        {
            string pointID = point.pointID;
            if (pointPartsPair.ContainsKey(pointID))
            {
                var partIDs = pointPartsPair[pointID];
                foreach (var partID in partIDs)
                {
                    partPointsPair[partID][pointID].fresh = true;
                    partPointsPair[partID][pointID].data = point;
                }

                CheckParts(partIDs);
            }
            else
            {
                if (logNonExistentPoint
                    && loggedPoints.Contains(pointID) == false)
                {
                    loggedPoints.Add(pointID);
                    Debug.Log($"未配置的点位：{pointID}，{point.name}");
                }
            }
        }

        /// <summary>
        /// 检测part中的数据是否是都是新鲜的，新鲜的话就调用事件
        /// </summary>
        /// <param name="partID"></param>
        private void CheckParts(List<string> partIDs)
        {
            foreach (var partID in partIDs)
            {
                if (partDataReceive.ContainsKey(partID))
                {
                    var dic = partPointsPair[partID];
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
                    List<PLCPoint> points = new List<PLCPoint>();
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
                    partDataReceive[partID].Invoke(points);
                }
            }
            if (_debugMode)
            {
                Publish<Dictionary<string, Dictionary<string, PointDataBuffer>>>("plcDebugData", partPointsPair);
            }
        }

        /// <summary>
        /// 添加新的部件监听
        /// </summary>
        /// <param name="partID"></param>
        /// <param name="action"></param>
        private void AddPartListener(string partID, Action<List<PLCPoint>> action)
        {
            if (partDataReceive.ContainsKey(partID))
            {
                partDataReceive[partID] += action;
            }
            else
            {
                partDataReceive.Add(partID, action);
            }
        }

        /// <summary>
        /// 移除部件监听
        /// </summary>
        /// <param name="partID"></param>
        /// <param name="action"></param>
        private void RemovePartListener(string partID, Action<List<PLCPoint>> action)
        {
            if (partDataReceive.ContainsKey(partID))
            {
                partDataReceive[partID] -= action;

                if (partDataReceive[partID] == null)
                {
                    partDataReceive.Remove(partID);
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
        public PLCPoint data;
        /// <summary>
        /// 是否新鲜（从上次调用事件后更新过）
        /// </summary>
        public bool fresh;

        public PointDataBuffer(PLCPoint data, bool fresh)
        {
            this.data = data;
            this.fresh = fresh;
        }
    }
}
