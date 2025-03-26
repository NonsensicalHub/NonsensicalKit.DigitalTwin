using NonsensicalKit.Core;

#if Cinemachine3
using Unity.Cinemachine;
using CinemachineCamera= Unity.Cinemachine.CinemachineCamera;
#else
using Cinemachine;
using CinemachineCamera= Cinemachine.CinemachineVirtualCamera;
#endif

namespace NonsensicalKit.DigitalTwin
{
    /// <summary>
    /// 基于Cinemachine的多摄像机管理，负责相机之间的切换
    /// </summary>
    public class VisualCameraSwitcher : MonoSingleton<VisualCameraSwitcher>
    {
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
                _laseCamera.Priority = 10;
            }

            newCamera.Priority = 11;

            _laseCamera = newCamera;
        }
    }
}
