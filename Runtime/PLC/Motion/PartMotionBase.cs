using NonsensicalKit.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.PLC
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
        /// 在Awake时初始化
        /// </summary>
        [SerializeField] private bool m_autoInit=false;

        /// <summary>
        /// 从毫秒转换成秒的倍率
        /// </summary>
        protected static float _magnification = 0.001f;

        /// <summary>
        /// 接收到数据后进行处理的方法
        /// </summary>
        /// <param name="part"></param>
        protected abstract void OnReceiveData(List<PLCPoint> part);

        protected virtual void Awake()
        {
            IOCC.Register<PLCPartInfo>(GetInfo);
            
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

        protected override void OnDestroy()
        {
            base.OnDestroy();
            IOCC.Unregister<PLCPartInfo>(GetInfo);
        }

        protected abstract PLCPartInfo GetInfo();

        /// <summary>
        /// 在开始时调用的初始化方法
        /// </summary>
        protected virtual void Init()
        {
            Publish<string,Action<List<PLCPoint>>>("addPartListener", m_partID, OnReceiveData);
        }

        /// <summary>
        /// 在销毁时调用的释放方法
        /// </summary>
        protected virtual void Dispose()
        {
            Publish<string, Action<List<PLCPoint>>>("removePartListener", m_partID, OnReceiveData);
        }
    }

    public class PLCPointInfo
    {
        public string pointName;
        public PLCDataType type;
        public bool isInput;

        public PLCPointInfo(string pointName, PLCDataType type, bool isInput)
        {
            this.pointName = pointName;
            this.type = type;
            this.isInput = isInput;
        }
    }

    public class PLCPartInfo
    {
        public string partName;
        public string partID;
        public List<PLCPointInfo> points;

        public PLCPartInfo(string partName, string partID, List<PLCPointInfo> points)
        {
            this.partName = partName;
            this.partID = partID;
            this.points = points;
        }
    }
}
