using NonsensicalKit.Tools;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.PLC
{
    /// <summary>
    /// 传送带双层停止点
    /// </summary>
    public class LinePoint2PartMotion : PartMotionBase
    {
        public Transform m_Trays;     //用于运输的料盘
        public Transform m_DownPos;   //下方停止点
        public Transform m_UpPos;     //上方停止点
        public Transform m_NextPos;   //下一个地点的为止
        public float m_MoveTime = 5;      //前往下一个地点用时
        public float m_VerticalTime = 1;      //上下方移动用时
        public bool u_UseNext = false; //是否使用下一个点位的信号,在线同步仿真时应当使用，虚拟运行是应当不使用，因为无法正确同步

        private bool _up;        //是否去过上层
        private bool _first;     //是否是首次接受信号
        private bool _downCheck; //下方是否检测到物料
        private bool _upCheck;   //上方方是否检测到物料
        private bool _moving;    //是否正在前往下一个地点
        private bool _fall;      //是否正在从上方前往下方
        private bool _nextCheck; //下个地点是否检测到物料

        private Tweenner _moveTweenner;
        private Tweenner _fallTweenner;
        private Tweenner _floatTweenner;

        protected override void Init()
        {
            base.Init();
            _first = true;
            m_Trays.gameObject.SetActive(false);
        }

        /// <summary>
        /// 三个数据应当分别是下方检测，上方检测，下一点检测
        /// </summary>
        /// <param name="part"></param>
        protected override void OnReceiveData(List<PLCPoint> part)
        {
            if (u_UseNext)
            {
                if (_first)
                {
                    _first = false;
                    _downCheck = bool.Parse(part[0].value);
                    _upCheck = bool.Parse(part[1].value);
                    _nextCheck = bool.Parse(part[2].value);
                    if (_downCheck)
                    {
                        m_Trays.gameObject.SetActive(true);
                        m_Trays.position = m_DownPos.position;
                    }
                    else if (_upCheck)
                    {
                        _up = true;
                        m_Trays.gameObject.SetActive(true);
                        m_Trays.position = m_UpPos.position;
                    }
                    return;
                }

                _nextCheck = bool.Parse(part[2].value);

                if (_moving)
                {
                    if (_nextCheck)
                    {
                        m_Trays.gameObject.SetActive(false);
                        if (_moveTweenner != null)
                        {
                            _moveTweenner.Abort();
                            _moveTweenner = null;
                        }
                        _moving = true;
                    }
                }

                if (_upCheck != bool.Parse(part[1].value))
                {
                    _upCheck = !_upCheck;

                    if (_upCheck)
                    {
                        _up = true;
                        if (_floatTweenner != null)
                        {
                            _floatTweenner.Abort();
                            _floatTweenner = null;
                        }
                        m_Trays.position = m_UpPos.position;
                    }
                    else
                    {
                        RunTweenner(_fallTweenner, m_Trays, m_DownPos.position, m_VerticalTime);
                        _fall = true;
                    }
                }

                if (_downCheck != bool.Parse(part[0].value))
                {
                    _downCheck = !_downCheck;
                    if (_downCheck)
                    {
                        if (_fall)
                        {
                            _fall = false;
                            if (_moveTweenner != null)
                            {
                                _moveTweenner.Abort();
                                _moveTweenner = null;
                            }
                            m_Trays.position = m_DownPos.position;
                        }
                        else
                        {
                            m_Trays.gameObject.SetActive(true);
                            m_Trays.position = m_DownPos.position;
                        }
                    }
                    else
                    {
                        if (_up)
                        {
                            _up = false;
                            RunTweenner(_moveTweenner, m_Trays, m_NextPos.position, m_MoveTime);
                            _moving = true;
                        }
                        else
                        {
                            RunTweenner(_floatTweenner, m_Trays, m_UpPos.position, m_VerticalTime);
                        }
                    }
                }
            }
            else
            {
                if (_first)
                {
                    _first = false;
                    _downCheck = bool.Parse(part[0].value);
                    _upCheck = bool.Parse(part[1].value);
                    if (_downCheck)
                    {
                        m_Trays.gameObject.SetActive(true);
                        m_Trays.position = m_DownPos.position;
                    }
                    else if (_upCheck)
                    {
                        _up = true;
                        m_Trays.gameObject.SetActive(true);
                        m_Trays.position = m_UpPos.position;
                    }
                    return;
                }

                if (_upCheck != bool.Parse(part[1].value))
                {
                    _upCheck = !_upCheck;

                    if (_upCheck)
                    {
                        _up = true;
                        if (_floatTweenner != null)
                        {
                            _floatTweenner.Abort();
                            _floatTweenner = null;
                        }
                        m_Trays.position = m_UpPos.position;
                    }
                    else
                    {
                        RunTweenner(_fallTweenner, m_Trays, m_DownPos.position, m_VerticalTime);
                        _fall = true;
                    }
                }

                if (_downCheck != bool.Parse(part[0].value))
                {
                    _downCheck = !_downCheck;
                    if (_downCheck)
                    {
                        if (_fall)
                        {
                            _fall = false;
                            if (_moveTweenner != null)
                            {
                                _moveTweenner.Abort();
                                _moveTweenner = null;
                            }
                            m_Trays.position = m_DownPos.position;
                        }
                        else
                        {
                            m_Trays.gameObject.SetActive(true);
                            m_Trays.position = m_DownPos.position;
                        }
                    }
                    else
                    {
                        if (_up)
                        {
                            _up = false;
                            RunTweenner(_moveTweenner, m_Trays, m_NextPos.position, m_MoveTime, true);
                            _moving = true;
                        }
                        else
                        {
                            RunTweenner(_floatTweenner, m_Trays, m_UpPos.position, m_VerticalTime);
                        }
                    }
                }
            }
        }

        private void RunTweenner(Tweenner tweenner, Transform control, Vector3 target, float time, bool needClear = false)
        {
            if (tweenner != null)
            {
                tweenner.Abort();
                tweenner = null;
            }
            if (needClear)
            {
                tweenner = control.DoMove(target, time).OnComplete(() => { tweenner = null; control.gameObject.SetActive(false); });
            }
            else
            {
                tweenner = control.DoMove(target, time);
            }
        }
        protected override PLCPartInfo GetInfo()
        {
            return new PLCPartInfo("传送带双层停止点", m_partID,
                new List<PLCPointInfo>() {
                new PLCPointInfo("下方传感",PLCDataType.Int,false),
                new PLCPointInfo("上方传感",PLCDataType.Int,false),
                new PLCPointInfo("后一点位传感",PLCDataType.Int,false),
                });
        }
    }
}
