using System.Collections.Generic;
using NonsensicalKit.Tools;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 传送带停止点
    /// </summary>
    public class LinePointPartMotion : PartMotionBase
    {
        [SerializeField] private Transform m_trays;
        [SerializeField] private Transform m_stopPos;
        [SerializeField] private Transform m_nextPos;
        [SerializeField] private float m_moveTime = 5;
        [SerializeField] private bool m_useNext = false; //是否使用下一个点位的信号,在线同步仿真时应当使用，虚拟运行是应当不使用，因为无法正确同步

        private bool _first;
        private bool _check;
        private bool _moving;
        private bool _nextCheck;

        private Tweener _tweener;

        protected override void Init()
        {
            base.Init();
            _first = true;
            m_trays.gameObject.SetActive(false);
        }

        /// <summary>
        /// 收到检测信号后显示物料，失去信号后物料移动至下一个点位
        /// 下一个点位收到信号后隐藏物料
        /// </summary>
        /// <param name="part"></param>
        protected override void OnReceiveData(List<PointData> part)
        {
            if (m_useNext)
            {
                if (_first)
                {
                    _first = false;
                    _check = bool.Parse(part[0].Value);
                    _nextCheck = bool.Parse(part[1].Value);
                    if (_check)
                    {
                        m_trays.gameObject.SetActive(true);
                        m_trays.position = m_stopPos.position;
                    }

                    return;
                }

                _nextCheck = bool.Parse(part[1].Value);

                if (_moving)
                {
                    if (_nextCheck)
                    {
                        m_trays.gameObject.SetActive(false);
                        if (_tweener != null)
                        {
                            _tweener.Abort();
                            _tweener = null;
                        }
                    }
                }

                if (_check != bool.Parse(part[0].Value))
                {
                    _check = !_check;
                    if (_check)
                    {
                        m_trays.gameObject.SetActive(true);
                        m_trays.position = m_stopPos.position;
                    }
                    else
                    {
                        if (_tweener != null)
                        {
                            _tweener.Abort();
                            _tweener = null;
                        }

                        _tweener = m_trays.DoMove(m_nextPos.position, m_moveTime);
                        _moving = true;
                    }
                }
            }
            else
            {
                if (_first)
                {
                    _first = false;
                    _check = bool.Parse(part[0].Value);
                    if (_check)
                    {
                        m_trays.gameObject.SetActive(true);
                        m_trays.position = m_stopPos.position;
                    }

                    return;
                }

                if (_check != bool.Parse(part[0].Value))
                {
                    _check = !_check;
                    if (_check)
                    {
                        m_trays.gameObject.SetActive(true);
                        m_trays.position = m_stopPos.position;
                    }
                    else
                    {
                        if (_tweener != null)
                        {
                            _tweener.Abort();
                            _tweener = null;
                        }

                        _tweener = m_trays.DoMove(m_nextPos.position, m_moveTime).OnComplete(() =>
                        {
                            _tweener = null;
                            m_trays.gameObject.SetActive(false);
                        });
                        _moving = true;
                    }
                }
            }
        }

        protected override PartDataInfo GetInfo()
        {
            return new PartDataInfo("单层传送带停止点", m_partID,
                new List<PointDataInfo>()
                {
                    new PointDataInfo("当前传感", PointDataType.Bool, false),
                    new PointDataInfo("下一点位传感", PointDataType.Bool, false)
                });
        }
    }
}
