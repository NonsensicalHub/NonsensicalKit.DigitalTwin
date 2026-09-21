using System;
using System.Collections.Generic;
using System.Globalization;
using NonsensicalKit.Tools;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 内外门：按 TCC_* 测点驱动内门/外门开合。
    /// 点位（与 MQTT 白名单一致）：
    /// InnerDoorOpenPos / InnerDoorClosePos / OuterDoorOpenPos / OuterDoorClosePos /
    /// InnerDoorOpen / InnerDoorClose / OuterDoorOpen / OuterDoorClose。
    /// 开到位变 false → 向关闭位移动；关到位变 false → 向打开位移动；
    /// 也可由打开/关闭命令驱动；到位信号上升沿会落到对应端点。速度可配置（单位/秒）。
    /// </summary>
    public class InnerOuterDoorPartMotion : PartMotionBase
    {
        private const string PointInnerOpenPos = "InnerDoorOpenPos";
        private const string PointInnerClosePos = "InnerDoorClosePos";
        private const string PointOuterOpenPos = "OuterDoorOpenPos";
        private const string PointOuterClosePos = "OuterDoorClosePos";
        private const string PointInnerOpen = "InnerDoorOpen";
        private const string PointInnerClose = "InnerDoorClose";
        private const string PointOuterOpen = "OuterDoorOpen";
        private const string PointOuterClose = "OuterDoorClose";

        [Serializable]
        public class DoorLeaf
        {
            [Tooltip("门体 Transform；为空则不驱动该扇门")]
            public Transform target;

            [Tooltip("打开时的本地坐标")]
            public Vector3 openLocalPosition;

            [Tooltip("关闭时的本地坐标")]
            public Vector3 closeLocalPosition;

            [Tooltip("移动速度（本地单位/秒）；≤0 则瞬间到位")]
            public float speed = 0.5f;
        }

        [Header("门体")]
        [SerializeField] private DoorLeaf m_innerDoor = new DoorLeaf();
        [SerializeField] private DoorLeaf m_outerDoor = new DoorLeaf();

        private bool _first = true;
        private bool _innerOpenPos;
        private bool _innerClosePos;
        private bool _outerOpenPos;
        private bool _outerClosePos;
        private bool _innerOpen;
        private bool _innerClose;
        private bool _outerOpen;
        private bool _outerClose;

        private Tweener _innerTweener;
        private Tweener _outerTweener;

        protected override void Init()
        {
            base.Init();
            _first = true;
        }

        protected override void Dispose()
        {
            AbortTween(ref _innerTweener);
            AbortTween(ref _outerTweener);
            base.Dispose();
        }

        protected override void OnReceiveData(List<PointData> part)
        {
            if (part == null || part.Count == 0)
                return;

            bool innerOpenPos = ReadBool(part, PointInnerOpenPos, 0, _innerOpenPos);
            bool innerClosePos = ReadBool(part, PointInnerClosePos, 1, _innerClosePos);
            bool outerOpenPos = ReadBool(part, PointOuterOpenPos, 2, _outerOpenPos);
            bool outerClosePos = ReadBool(part, PointOuterClosePos, 3, _outerClosePos);
            bool innerOpen = ReadBool(part, PointInnerOpen, 4, _innerOpen);
            bool innerClose = ReadBool(part, PointInnerClose, 5, _innerClose);
            bool outerOpen = ReadBool(part, PointOuterOpen, 6, _outerOpen);
            bool outerClose = ReadBool(part, PointOuterClose, 7, _outerClose);

            if (_first)
            {
                _first = false;
                SyncDoor(m_innerDoor, ref _innerTweener, innerOpenPos, innerClosePos, snap: true);
                SyncDoor(m_outerDoor, ref _outerTweener, outerOpenPos, outerClosePos, snap: true);
                CacheSignals(
                    innerOpenPos, innerClosePos, outerOpenPos, outerClosePos,
                    innerOpen, innerClose, outerOpen, outerClose);
                return;
            }

            UpdateDoor(
                m_innerDoor,
                ref _innerTweener,
                _innerOpenPos, innerOpenPos,
                _innerClosePos, innerClosePos,
                _innerOpen, innerOpen,
                _innerClose, innerClose);

            UpdateDoor(
                m_outerDoor,
                ref _outerTweener,
                _outerOpenPos, outerOpenPos,
                _outerClosePos, outerClosePos,
                _outerOpen, outerOpen,
                _outerClose, outerClose);

            CacheSignals(
                innerOpenPos, innerClosePos, outerOpenPos, outerClosePos,
                innerOpen, innerClose, outerOpen, outerClose);
        }

        private void CacheSignals(
            bool innerOpenPos, bool innerClosePos, bool outerOpenPos, bool outerClosePos,
            bool innerOpen, bool innerClose, bool outerOpen, bool outerClose)
        {
            _innerOpenPos = innerOpenPos;
            _innerClosePos = innerClosePos;
            _outerOpenPos = outerOpenPos;
            _outerClosePos = outerClosePos;
            _innerOpen = innerOpen;
            _innerClose = innerClose;
            _outerOpen = outerOpen;
            _outerClose = outerClose;
        }

        /// <summary>
        /// 开到位下降沿 → 关；关到位下降沿 → 开；
        /// 打开/关闭命令上升沿同理；到位上升沿落到端点。
        /// </summary>
        private static void UpdateDoor(
            DoorLeaf door,
            ref Tweener tweener,
            bool prevOpenPos, bool openPos,
            bool prevClosePos, bool closePos,
            bool prevOpenCmd, bool openCmd,
            bool prevCloseCmd, bool closeCmd)
        {
            if (door == null || door.target == null)
                return;

            // 到位：落到端点（可中途校正）
            if (!prevOpenPos && openPos)
            {
                MoveDoor(door, ref tweener, toOpen: true, snap: true);
                return;
            }

            if (!prevClosePos && closePos)
            {
                MoveDoor(door, ref tweener, toOpen: false, snap: true);
                return;
            }

            // 离开开到位 / 关闭命令 → 向关
            if ((prevOpenPos && !openPos) || (!prevCloseCmd && closeCmd))
            {
                MoveDoor(door, ref tweener, toOpen: false, snap: false);
                return;
            }

            // 离开关到位 / 打开命令 → 向开
            if ((prevClosePos && !closePos) || (!prevOpenCmd && openCmd))
            {
                MoveDoor(door, ref tweener, toOpen: true, snap: false);
            }
        }

        private static void SyncDoor(
            DoorLeaf door, ref Tweener tweener, bool openPos, bool closePos, bool snap)
        {
            if (door == null || door.target == null)
                return;

            if (openPos)
                MoveDoor(door, ref tweener, toOpen: true, snap);
            else if (closePos)
                MoveDoor(door, ref tweener, toOpen: false, snap);
        }

        private static void MoveDoor(DoorLeaf door, ref Tweener tweener, bool toOpen, bool snap)
        {
            if (door == null || door.target == null)
                return;

            Vector3 target = toOpen ? door.openLocalPosition : door.closeLocalPosition;
            AbortTween(ref tweener);

            if (snap || door.speed <= 0f)
            {
                door.target.localPosition = target;
                return;
            }

            tweener = door.target.DoLocalMove(target, door.speed).SetSpeedBased();
        }

        private static void AbortTween(ref Tweener tweener)
        {
            if (tweener == null)
                return;

            tweener.Abort();
            tweener = null;
        }

        protected override PartDataInfo GetInfo() =>
            new PartDataInfo("内外门", m_partID,
                new List<PointDataInfo>
                {
                    new PointDataInfo("内门开到位", PointDataType.Bool, false),
                    new PointDataInfo("内门关到位", PointDataType.Bool, false),
                    new PointDataInfo("外门开到位", PointDataType.Bool, false),
                    new PointDataInfo("外门关到位", PointDataType.Bool, false),
                    new PointDataInfo("内门打开", PointDataType.Bool, false),
                    new PointDataInfo("内门关闭", PointDataType.Bool, false),
                    new PointDataInfo("外门打开", PointDataType.Bool, false),
                    new PointDataInfo("外门关闭", PointDataType.Bool, false),
                });

        private static bool TryFind(List<PointData> part, string pointCode, out PointData point)
        {
            point = null;
            if (string.IsNullOrEmpty(pointCode) || part == null)
                return false;

            for (int i = 0; i < part.Count; i++)
            {
                var p = part[i];
                if (p == null)
                    continue;

                if (p.Point == pointCode || p.Name == pointCode)
                {
                    point = p;
                    return true;
                }
            }

            return false;
        }

        private static bool ReadBool(List<PointData> part, string pointCode, int fallbackIndex, bool fallbackValue)
        {
            if (TryFind(part, pointCode, out var point))
                return ParseBool(point.Value, fallbackValue);

            if (fallbackIndex >= 0 && fallbackIndex < part.Count && part[fallbackIndex] != null)
                return ParseBool(part[fallbackIndex].Value, fallbackValue);

            return fallbackValue;
        }

        private static bool ParseBool(string raw, bool fallback)
        {
            if (string.IsNullOrEmpty(raw))
                return fallback;

            if (bool.TryParse(raw, out bool b))
                return b;

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return n != 0;

            return fallback;
        }
    }
}
