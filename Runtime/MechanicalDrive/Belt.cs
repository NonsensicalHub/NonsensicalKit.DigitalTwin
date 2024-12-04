using NonsensicalKit.Core.Log;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    [RequireComponent(typeof(Renderer))]
    public class Belt : Mechanism
    {
        [SerializeField] private float m_ratio = 1;
        [SerializeField] private bool m_isXDir;
        [SerializeField] private BeltLoader m_loader;

        protected Renderer Renderer;

        protected virtual void Awake()
        {
            Renderer = GetComponent<Renderer>();
        }

        public override void Drive(float power, DriveType driveType)
        {
            if (driveType == DriveType.Angular)
            {
                LogCore.Error("此机械结构不支持角运动");
                return;
            }

            m_loader?.Move(power * m_ratio);
            if (m_isXDir)
            {
                Renderer.material.mainTextureOffset += new Vector2(power * m_ratio, 0);
            }
            else
            {
                Renderer.material.mainTextureOffset += new Vector2(0, power * m_ratio);
            }
        }
    }
}
