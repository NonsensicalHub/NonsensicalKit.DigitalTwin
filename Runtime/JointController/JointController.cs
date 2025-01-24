using System;
using System.Collections;
using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin
{
    /// <summary>
    /// 关节运动方式
    /// </summary>
    public enum JointAxisType
    {
        Rotation,
        Position,
    }

    /// <summary>
    /// 关节运动方向，IX代表反向X，IY和IZ同理
    /// </summary>
    public enum JointDirType
    {
        X,
        Y,
        Z,
        IX,
        IY,
        IZ,
    }

    [Serializable]
    public class JointSetting
    {
        /// <summary>
        /// 关节节点
        /// </summary>
        public Transform JointsNode;

        /// <summary>
        /// 需要改变的轴
        /// </summary>
        public JointAxisType AxisType;

        /// <summary>
        /// 需要改变的方向
        /// </summary>
        public JointDirType DirType;

        /// <summary>
        /// 正常姿态时的欧拉角/轴坐标
        /// </summary>
        public Vector3 ZeroState;

        /// <summary>
        /// 正常姿态的初始值
        /// </summary>
        public float InitialValue;

        /// <summary>
        /// 转换率（一单位改变需要改变多少角度/位移）
        /// </summary>
        public float ConversionRate = 1;
    }

    public class ActionData
    {
        public ActionData()
        {
        }

        public ActionData(long[] values, double time = 0)
        {
            Values = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                Values[i] = values[i];
            }

            Time = (float)time;
        }

        public ActionData(double[] values, double time = 0)
        {
            Values = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                Values[i] = (float)values[i];
            }

            Time = (float)time;
        }

        public ActionData(float[] values, float time = 0)
        {
            Values = values;
            Time = time;
        }

        /// <summary>
        /// 每个节点的数值
        /// </summary>
        public float[] Values { get; set; }

        /// <summary>
        /// 到达目标关节需要多久
        /// </summary>
        public float Time { get; set; }

        public int Length => Values.Length;

        public override string ToString()
        {
            return $"{StringTool.GetSetString(Values)},time:{Time}";
        }
    }

    /// <summary>
    /// 使用数值控制模型节点位移或者旋转
    /// </summary>
    public class JointController : NonsensicalMono
    {
        public bool Pausing { get; set; }

        public JointSetting[] Joints;

        private float _listTimer; //贯穿链表数据运动的计时器，用于校准时间，避免因为每个数据执行时协程里一帧的等待时间积累起来导致的时间误差
        private float _listTime;

        private bool _isList;

        protected virtual void Update()
        {
            if (!Pausing)
            {
                _listTimer += Time.deltaTime;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 零点重置
        /// </summary>
        [ContextMenu("ResetZeroState")]
        public void ResetZeroState()
        {
            foreach (var item in Joints)
            {
                if (item.JointsNode != null)
                {
                    if (item.AxisType == JointAxisType.Position)
                    {
                        item.ZeroState = item.JointsNode.localPosition;
                    }
                    else if (item.AxisType == JointAxisType.Rotation)
                    {
                        item.ZeroState = item.JointsNode.localEulerAngles;
                    }
                }
            }

            EditorUtility.SetDirty(gameObject);
        }

        /// <summary>
        /// 姿态重置
        /// </summary>
        [ContextMenu("ResetRobotState")]
        public void ResetRobotState()
        {
            foreach (var item in Joints)
            {
                if (item.JointsNode != null)
                {
                    if (item.AxisType == JointAxisType.Position)
                    {
                        item.JointsNode.localPosition = item.ZeroState;
                    }
                    else if (item.AxisType == JointAxisType.Rotation)
                    {
                        item.JointsNode.localEulerAngles = item.ZeroState;
                    }
                }
            }

            EditorUtility.SetDirty(gameObject);
        }
#endif

        public float[] GetJointsValue()
        {
            float[] values = new float[Joints.Length];

            for (int i = 0; i < Joints.Length; i++)
            {
                float crtValue = 0;
                Vector3 gap = Vector3.zero;

                if (Joints[i].AxisType == JointAxisType.Position)
                {
                    gap = (Joints[i].JointsNode.localPosition - Joints[i].ZeroState) / Joints[i].ConversionRate;
                }
                else if (Joints[i].AxisType == JointAxisType.Rotation)
                {
                    //gap = Vector3.one* Quaternion.Angle(Quaternion.Euler(joints[i].jointsNode.localEulerAngles), Quaternion.Euler(joints[i].zeroState));

                    gap = (Joints[i].JointsNode.localEulerAngles - Joints[i].ZeroState) / Joints[i].ConversionRate;
                }

                switch (Joints[i].DirType)
                {
                    case JointDirType.X:
                        crtValue = gap.x;
                        break;
                    case JointDirType.Y:
                        crtValue = gap.y;
                        break;
                    case JointDirType.Z:
                        crtValue = gap.z;
                        break;
                    case JointDirType.IX:
                        crtValue = -gap.x;
                        break;
                    case JointDirType.IY:
                        crtValue = -gap.y;
                        break;
                    case JointDirType.IZ:
                        crtValue = -gap.z;
                        break;
                    default:
                        crtValue = 0;
                        break;
                }


                if (Joints[i].AxisType == JointAxisType.Rotation && crtValue < -180)
                {
                    crtValue += 360;
                }

                values[i] = crtValue;
            }

            return values;
        }

        public void ChangeStates(IEnumerable<ActionData> jds)
        {
            _isList = true;
            _listTimer = 0;
            _listTime = 0;
            StopAllCoroutines();
            StartCoroutine(ChangeStatesCor(jds));
        }

        public void ChangeState(ActionData jd)
        {
            StopAllCoroutines();
            StartCoroutine(ChangeStateCor(jd));
        }

        private IEnumerator ChangeStatesCor(IEnumerable<ActionData> jds)
        {
            foreach (var item in jds)
            {
                yield return ChangeStateCor(item);
            }
        }

        private IEnumerator ChangeStateCor(ActionData jd)
        {
            float time = jd.Time;
            if (_isList)
            {
                _listTime += jd.Time;

                time = _listTime - _listTimer;
            }

            int min = Joints.Length < jd.Length ? Joints.Length : jd.Length;

            for (int i = 0; i < min - 1; i++)
            {
                StartCoroutine(ChangeJoint(i, jd.Values[i], time));
            }

            yield return ChangeJoint(min - 1, jd.Values[min - 1], time);
        }

        private IEnumerator ChangeJoint(int index, float targetValue, float time)
        {
            JointSetting crtJoint = Joints[index];

            float offset = (targetValue - crtJoint.InitialValue) * crtJoint.ConversionRate;

            Vector3 v3Offset = Vector3.zero;
            switch (crtJoint.DirType)
            {
                case JointDirType.X:
                    v3Offset = new Vector3(offset, 0, 0);
                    break;
                case JointDirType.Y:
                    v3Offset = new Vector3(0, offset, 0);
                    break;
                case JointDirType.Z:
                    v3Offset = new Vector3(0, 0, offset);
                    break;
                case JointDirType.IX:
                    v3Offset = new Vector3(-offset, 0, 0);
                    break;
                case JointDirType.IY:
                    v3Offset = new Vector3(0, -offset, 0);
                    break;
                case JointDirType.IZ:
                    v3Offset = new Vector3(0, 0, -offset);
                    break;
            }

            Vector3 targetV3 = crtJoint.ZeroState + v3Offset;
            switch (crtJoint.AxisType)
            {
                case JointAxisType.Rotation:
                    yield return DoRotate(crtJoint.JointsNode, targetV3, time);
                    break;
                case JointAxisType.Position:
                    yield return DoMove(crtJoint.JointsNode, targetV3, time);
                    break;
            }
        }

        private IEnumerator DoRotate(Transform targetTsf, Vector3 targetLocalEuler, float time)
        {
            if (time <= 0)
            {
                targetTsf.localEulerAngles = targetLocalEuler;
                yield break;
            }

            float timer = 0;

            Quaternion startQuaternion = targetTsf.localRotation;
            Quaternion targetQuaternion = Quaternion.Euler(targetLocalEuler);
            while (true)
            {
                while (Pausing)
                {
                    yield return null;
                }

                timer += Time.deltaTime;

                if (timer > time)
                {
                    targetTsf.localEulerAngles = targetLocalEuler;
                    yield break;
                }
                else
                {
                    targetTsf.localRotation = Quaternion.Lerp(startQuaternion, targetQuaternion, timer / time);
                }

                yield return null;
            }
        }

        private IEnumerator DoMove(Transform targetTsf, Vector3 targetLocalPosition, float time)
        {
            if (time <= 0)
            {
                targetTsf.localPosition = targetLocalPosition;
                yield break;
            }

            float timer = 0;

            Vector3 startLocalPosition = targetTsf.localPosition;

            while (true)
            {
                while (Pausing)
                {
                    yield return null;
                }

                timer += Time.deltaTime;

                if (timer > time)
                {
                    targetTsf.localPosition = targetLocalPosition;
                    yield break;
                }
                else
                {
                    targetTsf.localPosition = Vector3.Lerp(startLocalPosition, targetLocalPosition, timer / time);
                }

                yield return null;
            }
        }
    }
}
