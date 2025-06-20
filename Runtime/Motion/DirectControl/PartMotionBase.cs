using System.Collections.Generic;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 部件运动基类
    /// </summary>
    public abstract class PartMotionBase : NonsensicalMono
    {
        /// <summary>
        /// 对应的部件id
        /// </summary>
        [SerializeField] protected string m_partID;

        /// <summary>
        /// 在Enable时初始化
        /// </summary>
        [SerializeField] private bool m_autoInit;

        /// <summary>
        /// 从毫秒转换成秒的倍率
        /// </summary>
        protected const float Magnification = 0.001f;

        protected virtual void Awake()
        {
            Register(GetInfo);
        }

        private void OnEnable()
        {
            if (m_autoInit)
            {
                Init();
            }
        }

        private void OnDisable()
        {
            Dispose();
        }

        /// <summary>
        /// 接收到数据后进行处理的方法
        /// </summary>
        /// <param name="part"></param>
        protected abstract void OnReceiveData(List<PointData> part);
        
        /// <summary>
        /// 获取部件的描述信息，用于在自定义配置界面时展示
        /// </summary>
        /// <returns></returns>
        protected abstract PartDataInfo GetInfo();

        /// <summary>
        /// 在开始时调用的初始化方法
        /// </summary>
        protected virtual void Init()
        {
            Subscribe<List<PointData>>("MotionPartUpdate", m_partID, OnReceiveData);
        }

        /// <summary>
        /// 在销毁时调用的释放方法
        /// </summary>
        protected virtual void Dispose()
        {
            Unsubscribe<List<PointData>>("MotionPartUpdate", m_partID, OnReceiveData);
        }
    }

    public class PartDataInfo
    {
        public string PartName;
        public string PartID;
        public List<PointDataInfo> Points;

        public PartDataInfo(string partName, string partID, List<PointDataInfo> points)
        {
            this.PartName = partName;
            this.PartID = partID;
            this.Points = points;
        }
    }

    public class PointDataInfo
    {
        public string PointName;
        public PointDataType Type;
        public bool IsInput;

        public PointDataInfo(string pointName, PointDataType type, bool isInput)
        {
            this.PointName = pointName;
            this.Type = type;
            this.IsInput = isInput;
        }
    }
}
