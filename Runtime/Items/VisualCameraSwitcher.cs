using NonsensicalKit.Core;
#if UNITY_6000_0_OR_NEWER
using Unity.Cinemachine;
#else
using Cinemachine;
#endif

namespace NonsensicalKit.DigitalTwin
{
    /// <summary>
    /// 基于Cinemachine的多摄像机管理，负责相机之间的切换
    /// </summary>
    public class VisualCameraSwitcher : MonoSingleton<VisualCameraSwitcher>
    {
        private CinemachineVirtualCamera _laseCamera;

        protected override void Awake()
        {
            base.Awake();

            Subscribe<CinemachineVirtualCamera>("switchVirtualCamera", OnSwitchCamera);
        }

        public void SwitchCamera(CinemachineVirtualCamera newCamera)
        {
            OnSwitchCamera(newCamera);
        }

        private void OnSwitchCamera(CinemachineVirtualCamera newCamera)
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
