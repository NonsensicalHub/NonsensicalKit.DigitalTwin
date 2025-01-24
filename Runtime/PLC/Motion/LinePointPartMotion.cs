using System.Collections.Generic;
using NonsensicalKit.Tools;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.PLC
{
    /// <summary>
    /// 传送带停止点
    /// </summary>
    public class LinePointPartMotion : PartMotionBase
    {
        public Transform m_Trays;
        public Transform m_StopPos;
        public Transform m_NextPos;
        public float m_MoveTime = 5;
        public bool m_UseNext = false; //是否使用下一个点位的信号,在线同步仿真时应当使用，虚拟运行是应当不使用，因为无法正确同步

        private bool _first;
        private bool _check;
        private bool _moving;
        private bool _nextCheck;

        private Tweener _tweener;

        protected override void Init()
        {
            base.Init();
            _first = true;
            m_Trays.gameObject.SetActive(false);
        }

        /// <summary>
        /// 收到检测信号后显示物料，失去信号后物料移动至下一个点位
        /// 下一个点位收到信号后隐藏物料
        /// </summary>
        /// <param name="part"></param>
        protected override void OnReceiveData(List<PLCPoint> part)
        {
            if (m_UseNext)
            {
                if (_first)
                {
                    _first = false;
                    _check = bool.Parse(part[0].value);
                    _nextCheck = bool.Parse(part[1].value);
                    if (_check)
                    {
                        m_Trays.gameObject.SetActive(true);
                        m_Trays.position = m_StopPos.position;
                    }

                    return;
                }

                _nextCheck = bool.Parse(part[1].value);

                if (_moving)
                {
                    if (_nextCheck)
                    {
                        m_Trays.gameObject.SetActive(false);
                        if (_tweener != null)
                        {
                            _tweener.Abort();
                            _tweener = null;
                        }
                    }
                }

                if (_check != bool.Parse(part[0].value))
                {
                    _check = !_check;
                    if (_check)
                    {
                        m_Trays.gameObject.SetActive(true);
                        m_Trays.position = m_StopPos.position;
                    }
                    else
                    {
                        if (_tweener != null)
                        {
                            _tweener.Abort();
                            _tweener = null;
                        }

                        _tweener = m_Trays.DoMove(m_NextPos.position, m_MoveTime);
                        _moving = true;
                    }
                }
            }
            else
            {
                if (_first)
                {
                    _first = false;
                    _check = bool.Parse(part[0].value);
                    if (_check)
                    {
                        m_Trays.gameObject.SetActive(true);
                        m_Trays.position = m_StopPos.position;
                    }

                    return;
                }

                if (_check != bool.Parse(part[0].value))
                {
                    _check = !_check;
                    if (_check)
                    {
                        m_Trays.gameObject.SetActive(true);
                        m_Trays.position = m_StopPos.position;
                    }
                    else
                    {
                        if (_tweener != null)
                        {
                            _tweener.Abort();
                            _tweener = null;
                        }

                        _tweener = m_Trays.DoMove(m_NextPos.position, m_MoveTime).OnComplete(() =>
                        {
                            _tweener = null;
                            m_Trays.gameObject.SetActive(false);
                        });
                        _moving = true;
                    }
                }
            }
        }

        protected override PLCPartInfo GetInfo()
        {
            return new PLCPartInfo("单层传送带停止点", m_partID,
                new List<PLCPointInfo>()
                {
                    new PLCPointInfo("当前传感", PLCDataType.Bool, false),
                    new PLCPointInfo("下一点位传感", PLCDataType.Bool, false)
                });
        }
    }
}
