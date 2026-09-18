using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>世界空间六向轴（±X / ±Y / ±Z），用于配置单轴运动方向。</summary>
    public enum SignedAxis
    {
        [InspectorName("+X")]
        PositiveX = 0,

        [InspectorName("-X")]
        NegativeX = 1,

        [InspectorName("+Y")]
        PositiveY = 2,

        [InspectorName("-Y")]
        NegativeY = 3,

        [InspectorName("+Z")]
        PositiveZ = 4,

        [InspectorName("-Z")]
        NegativeZ = 5
    }

    public static class SignedAxisUtil
    {
        public static Vector3 ToVector(SignedAxis axis)
        {
            switch (axis)
            {
                case SignedAxis.PositiveX: return Vector3.right;
                case SignedAxis.NegativeX: return Vector3.left;
                case SignedAxis.PositiveY: return Vector3.up;
                case SignedAxis.NegativeY: return Vector3.down;
                case SignedAxis.PositiveZ: return Vector3.forward;
                case SignedAxis.NegativeZ: return Vector3.back;
                default: return Vector3.forward;
            }
        }

        /// <summary>对应世界坐标分量下标：X=0, Y=1, Z=2。</summary>
        public static int ToComponentIndex(SignedAxis axis) => ((int)axis) / 2;

        public static float GetComponent(Vector3 v, SignedAxis axis) =>
            v[ToComponentIndex(axis)];

        public static Vector3 WithComponent(Vector3 v, SignedAxis axis, float value)
        {
            v[ToComponentIndex(axis)] = value;
            return v;
        }
    }
}
