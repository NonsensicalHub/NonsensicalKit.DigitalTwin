using NonsensicalKit.Core;
#if Cinemachine3
using CinemachineCamera = Unity.Cinemachine.CinemachineCamera;  //cinemachine3.0版本后命名空间改变

#else
using Cinemachine;
using CinemachineCamera = Cinemachine.CinemachineVirtualCamera;
#endif

namespace NonsensicalKit.DigitalTwin
{
    /// <summary>
    /// 基于Cinemachine的多摄像机管理，负责相机之间的切换
    /// </summary>
    public class VisualCameraSwitcher : MonoSingleton<VisualCameraSwitcher>
    {
        public int LastPriority { get; set; } = 10;
        public int CurrentPriority { get; set; } = 20;

        private CinemachineCamera _laseCamera;

        protected override void Awake()
        {
            base.Awake();

            Subscribe<CinemachineCamera>("switchVirtualCamera", OnSwitchCamera);
        }

        public void SwitchCamera(CinemachineCamera newCamera)
        {
            OnSwitchCamera(newCamera);
        }

        private void OnSwitchCamera(CinemachineCamera newCamera)
        {
            if (_laseCamera)
            {
                _laseCamera.Priority = LastPriority;
            }

            newCamera.Priority = CurrentPriority;

            _laseCamera = newCamera;
        }
    }
}
