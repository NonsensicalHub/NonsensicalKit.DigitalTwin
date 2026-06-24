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
        /// <summary>货物实例显隐因子 [0,1]，由 Shader Bayer Dither + clip 控制像素保留比例。</summary>
        public float Visibility;
        public Matrix4x4 CachedMatrix;
        public bool HasCachedMatrix;

        public RuntimeBinData(float posX, float posY, float posZ)
        {
            Pos = new Vector3(posX, posY, posZ);
            // 默认显示货物，避免初始化后全部不可见。
            ShowCargo = true;
            Visibility = 1f;
            CachedMatrix = Matrix4x4.identity;
            HasCachedMatrix = false;
        }
    }
}
