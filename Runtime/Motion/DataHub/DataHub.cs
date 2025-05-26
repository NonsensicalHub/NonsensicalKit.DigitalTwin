using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 收集数据并缓存，然后在合适的时机进行分发
    /// </summary>
    public class DataHub : NonsensicalMono, IMonoService
    {
        [Tooltip("单点位修改模式")]  [SerializeField]
        private bool m_singlePointMode;     //一个部件只要有一个点位修改就更新状态
        [Tooltip("首次接收到未配置过的点位时输出日志")] [SerializeField]
        private bool m_logNonExistentPoint;
        public bool IsReady { get; private set; }

        public Action InitCompleted { get; set; }

        /// <summary>
        /// 部件点位缓存字典
        /// 部件id为键，所属点位缓存字典为值
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, PointDataBuffer>> _partPointsPair = new();

        /// <summary>
        /// 点位部件字典
        /// 点id为键，所属部件id链表为值
        /// 存在同一个点由多个部件使用的情况，如传送带上多个停止点，每个停止点都需要获取本停止点和下一个停止点的信息来判断状态
        /// </summary>
        private readonly Dictionary<string, List<string>> _pointPartsPair = new();

        /// <summary>
        /// 每次报警后缓存点位，防止重复报警
        /// </summary>
        private readonly HashSet<string> _loggedPoints = new();

        private void Awake()
        {
            Subscribe<IEnumerable<PartConfig>>("InitPartConfig", InitPartConfig); //初始化配置
            Subscribe<IEnumerable<PointData>>("ReceivePoints", ReceivePoints); //接受到一组数据
            Subscribe<PointData>("ReceivePoint", ReceivePoint); //接收到单个数据
            IsReady = true;
            InitCompleted?.Invoke();
        }

        public void InitPartConfig(IEnumerable<PartConfig> configs)
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
        public void ReceivePoints(IEnumerable<PointData> points)
        {
            //使用set防止重复调用事件
            HashSet<string> IDs = new HashSet<string>();

            foreach (var point in points)
            {
                string pointID = point.PointID;
                if (_pointPartsPair.TryGetValue(pointID, out var partIDs))
                {
                    foreach (var partID in partIDs)
                    {
                        //此处无需检测是否存在部件id，因为初始化时是双向的
                        _partPointsPair[partID][pointID].Fresh = true;
                        _partPointsPair[partID][pointID].Data = point;
                    }

                    foreach (var partID in partIDs)
                    {
                        IDs.Add(partID);
                    }
                }
                else
                {
                    if (m_logNonExistentPoint && _loggedPoints.Add(pointID))
                    {
                        Debug.Log($"未配置的点位：{pointID}，{point.Name}");
                    }
                }
            }

            CheckParts(IDs.ToList());
        }

        /// <summary>
        /// 获取到单个点数据
        /// </summary>
        /// <param name="pointData"></param>
        public void ReceivePoint(PointData pointData)
        {
            string pointID = pointData.PointID;
            if (_pointPartsPair.TryGetValue(pointID, out var partIDs))
            {
                foreach (var partID in partIDs)
                {
                    _partPointsPair[partID][pointID].Fresh = true;
                    _partPointsPair[partID][pointID].Data = pointData;
                }

                CheckParts(partIDs);
            }
            else
            {
                if (m_logNonExistentPoint && _loggedPoints.Add(pointID))
                {
                    Debug.Log($"未配置的点位：{pointID}，{pointData.Name}");
                }
            }
        }

        /// <summary>
        /// 检测part中的数据是否是都是新鲜的，新鲜的话就调用事件
        /// 单点位修改模式下改为检测是否至少有一个点位更新
        /// </summary>
        /// <param name="partIDs"></param>
        private void CheckParts(List<string> partIDs)
        {
            foreach (var partID in partIDs)
            {
                var dic = _partPointsPair[partID];  //此处使用的id是通过_pointPartsPair查出来的，无需判断是否存在
                bool flag ; //是否需要更新此部件
                if (m_singlePointMode)
                {
                    flag = false;
                    //遍历部件下的所有点，是否存在新鲜的
                    foreach (var pair in dic)
                    {
                        if (pair.Value.Fresh )
                        {
                            flag = true;
                            break;
                        }
                    }
                }
                else
                {
                    flag = true;
                    //遍历部件下的所有点，是否都是新鲜的
                    foreach (var pair in dic)
                    {
                        if (pair.Value.Fresh == false)
                        {
                            flag = false;
                            break;
                        }
                    }
                }

                if (flag == false)
                {
                    continue;
                }

                List<PointData> points = new List<PointData>();
                foreach (var pair in dic)
                {
                    points.Add(pair.Value.Data);
                    pair.Value.Fresh = false;
                }
    
                PublishWithID("MotionPartUpdate",partID, points);
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
        public PointData Data;

        /// <summary>
        /// 是否新鲜（从上次调用事件后更新过）
        /// </summary>
        public bool Fresh;

        public PointDataBuffer(PointData data, bool fresh)
        {
            this.Data = data;
            this.Fresh = fresh;
        }
    }
}
