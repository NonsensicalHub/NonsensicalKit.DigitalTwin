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
        public Matrix4x4 CachedMatrix;
        public bool HasCachedMatrix;

        public RuntimeBinData(float posX, float posY, float posZ)
        {
            Pos = new Vector3(posX, posY, posZ);
            // 默认显示货物，避免初始化后全部不可见。
            ShowCargo = true;
            CachedMatrix = Matrix4x4.identity;
            HasCachedMatrix = false;
        }
    }
}
