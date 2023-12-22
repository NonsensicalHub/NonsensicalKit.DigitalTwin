using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public enum DriveType
    {
        Linear,//线速度运动，单位是米
        Angular//角速度运动，单位是角度
    }

    /// <summary>
    /// 机械装置
    /// “借鉴”：https://github.com/mogoson/MGS.Machinery
    /// </summary>
    public abstract class  Mechanism :MonoBehaviour
    {
        public abstract void Drive(float power, DriveType driveType);
    }
}
