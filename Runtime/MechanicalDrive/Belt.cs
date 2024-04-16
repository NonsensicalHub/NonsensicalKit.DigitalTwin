using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    [RequireComponent(typeof(Renderer))]
    public class Belt : Mechanism
    {
        [SerializeField] private float m_ratio = 1;

        protected Renderer mRenderer;

        protected virtual void Awake()
        {
            mRenderer = GetComponent<Renderer>();
        }

        public override void Drive(float power, DriveType driveType)
        {
            if (driveType == DriveType.Angular)
            {
                Debug.Log("此机械结构不支持角运动");
                return;
            }
            mRenderer.material.mainTextureOffset += new Vector2(power * m_ratio, 0);
        }
    }
}
