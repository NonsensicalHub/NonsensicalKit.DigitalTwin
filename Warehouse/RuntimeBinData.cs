using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// 运行时货位数据。
    /// </summary>
    public class RuntimeBinData
    {
        public Vector3 Pos;
        public bool ShowCargo;

        /// <summary>
        /// 结构是否启用。由禁用规则在加载时写入；为 false 时不可再显示货物。
        /// </summary>
        public bool SlotEnabled = true;

        public Matrix4x4 CachedMatrix;
        public bool HasCachedMatrix;

        public RuntimeBinData(float posX, float posY, float posZ, bool defaultShowCargo = true, bool slotEnabled = true)
        {
            Pos = new Vector3(posX, posY, posZ);
            SlotEnabled = slotEnabled;
            // 默认显示货物，避免初始化后全部不可见；禁用槽位强制不显示。
            ShowCargo = slotEnabled && defaultShowCargo;
            CachedMatrix = Matrix4x4.identity;
            HasCachedMatrix = false;
        }
    }
}
