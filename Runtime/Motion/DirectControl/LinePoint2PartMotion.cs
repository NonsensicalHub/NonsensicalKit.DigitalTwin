using System.Collections.Generic;
using NonsensicalKit.Tools;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 传送带双层停止点
    /// </summary>
    public class LinePoint2PartMotion : PartMotionBase
    {
        [SerializeField] private Transform m_trays; //用于运输的料盘
        [SerializeField] private Transform m_downPos; //下方停止点
        [SerializeField] private Transform m_upPos; //上方停止点
        [SerializeField] private Transform m_nextPos; //下一个地点的为止
        [SerializeField] private float m_moveTime = 5; //前往下一个地点用时
        [SerializeField] private float m_verticalTime = 1; //上下方移动用时
        [SerializeField] private bool m_useNext; //是否使用下一个点位的信号,在线同步仿真时应当使用，虚拟运行是应当不使用，因为无法正确同步

        private bool _up; //是否去过上层
        private bool _first; //是否是首次接受信号
        private bool _downCheck; //下方是否检测到物料
        private bool _upCheck; //上方方是否检测到物料
        private bool _moving; //是否正在前往下一个地点
        private bool _fall; //是否正在从上方前往下方
        private bool _nextCheck; //下个地点是否检测到物料

        private Tweener _moveTweener;
        private Tweener _fallTweener;
        private Tweener _floatTweener;

        protected override void Init()
        {
            base.Init();
            _first = true;
            m_trays.gameObject.SetActive(false);
        }

        /// <summary>
        /// 三个数据应当分别是下方检测，上方检测，下一点检测
        /// </summary>
        /// <param name="part"></param>
        protected override void OnReceiveData(List<PointData> part)
        {
            if (m_useNext)
            {
                if (_first)
                {
                    _first = false;
                    _downCheck = bool.Parse(part[0].Value);
                    _upCheck = bool.Parse(part[1].Value);
                    _nextCheck = bool.Parse(part[2].Value);
                    if (_downCheck)
                    {
                        m_trays.gameObject.SetActive(true);
                        m_trays.position = m_downPos.position;
                    }
                    else if (_upCheck)
                    {
                        _up = true;
                        m_trays.gameObject.SetActive(true);
                        m_trays.position = m_upPos.position;
                    }

                    return;
                }

                _nextCheck = bool.Parse(part[2].Value);

                if (_moving)
                {
                    if (_nextCheck)
                    {
                        m_trays.gameObject.SetActive(false);
                        if (_moveTweener != null)
                        {
                            _moveTweener.Abort();
                            _moveTweener = null;
                        }

                        _moving = true;
                    }
                }

                if (_upCheck != bool.Parse(part[1].Value))
                {
                    _upCheck = !_upCheck;

                    if (_upCheck)
                    {
                        _up = true;
                        if (_floatTweener != null)
                        {
                            _floatTweener.Abort();
                            _floatTweener = null;
                        }

                        m_trays.position = m_upPos.position;
                    }
                    else
                    {
                        RunTweener(ref _fallTweener, m_trays, m_downPos.position, m_verticalTime);
                        _fall = true;
                    }
                }

                if (_downCheck != bool.Parse(part[0].Value))
                {
                    _downCheck = !_downCheck;
                    if (_downCheck)
                    {
                        if (_fall)
                        {
                            _fall = false;
                            if (_moveTweener != null)
                            {
                                _moveTweener.Abort();
                                _moveTweener = null;
                            }

                            m_trays.position = m_downPos.position;
                        }
                        else
                        {
                            m_trays.gameObject.SetActive(true);
                            m_trays.position = m_downPos.position;
                        }
                    }
                    else
                    {
                        if (_up)
                        {
                            _up = false;
                            RunTweener(ref _moveTweener, m_trays, m_nextPos.position, m_moveTime);
                            _moving = true;
                        }
                        else
                        {
                            RunTweener(ref _floatTweener, m_trays, m_upPos.position, m_verticalTime);
                        }
                    }
                }
            }
            else
            {
                if (_first)
                {
                    _first = false;
                    _downCheck = bool.Parse(part[0].Value);
                    _upCheck = bool.Parse(part[1].Value);
                    if (_downCheck)
                    {
                        m_trays.gameObject.SetActive(true);
                        m_trays.position = m_downPos.position;
                    }
                    else if (_upCheck)
                    {
                        _up = true;
                        m_trays.gameObject.SetActive(true);
                        m_trays.position = m_upPos.position;
                    }

                    return;
                }

                if (_upCheck != bool.Parse(part[1].Value))
                {
                    _upCheck = !_upCheck;

                    if (_upCheck)
                    {
                        _up = true;
                        if (_floatTweener != null)
                        {
                            _floatTweener.Abort();
                            _floatTweener = null;
                        }

                        m_trays.position = m_upPos.position;
                    }
                    else
                    {
                        RunTweener(ref _fallTweener, m_trays, m_downPos.position, m_verticalTime);
                        _fall = true;
                    }
                }

                if (_downCheck != bool.Parse(part[0].Value))
                {
                    _downCheck = !_downCheck;
                    if (_downCheck)
                    {
                        if (_fall)
                        {
                            _fall = false;
                            if (_moveTweener != null)
                            {
                                _moveTweener.Abort();
                                _moveTweener = null;
                            }

                            m_trays.position = m_downPos.position;
                        }
                        else
                        {
                            m_trays.gameObject.SetActive(true);
                            m_trays.position = m_downPos.position;
                        }
                    }
                    else
                    {
                        if (_up)
                        {
                            _up = false;
                            RunTweener(ref _moveTweener, m_trays, m_nextPos.position, m_moveTime, true);
                            _moving = true;
                        }
                        else
                        {
                            RunTweener(ref _floatTweener, m_trays, m_upPos.position, m_verticalTime);
                        }
                    }
                }
            }
        }

        private void RunTweener(ref Tweener tweener, Transform control, Vector3 target, float time, bool needClear = false)
        {
            if (tweener != null)
            {
                tweener.Abort();
            }

            if (needClear)
            {
                tweener = control.DoMove(target, time).OnComplete(() =>
                {
                    control.gameObject.SetActive(false);
                });
            }
            else
            {
                tweener = control.DoMove(target, time);
            }
        }

        protected override PartDataInfo GetInfo()
        {
            return new PartDataInfo("传送带双层停止点", m_partID,
                new List<PointDataInfo>()
                {
                    new PointDataInfo("下方传感", PointDataType.Int, false),
                    new PointDataInfo("上方传感", PointDataType.Int, false),
                    new PointDataInfo("后一点位传感", PointDataType.Int, false),
                });
        }
    }
}
