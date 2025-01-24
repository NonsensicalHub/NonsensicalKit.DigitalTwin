using Cinemachine;
using NonsensicalKit.Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin
{
    /// <summary>
    /// 受刚体控制的飞行摄像机
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(CinemachineVirtualCamera))]
    public class FlyCamera : NonsensicalMono
    {
        /// <summary>
        /// Rotation speed when using the mouse.
        /// </summary>
        [FormerlySerializedAs("m_LookSpeedMouse")] [SerializeField]
        private float m_lookSpeedMouse = 4.0f;

        /// <summary>
        /// Movement speed.
        /// </summary>
        [FormerlySerializedAs("m_MoveSpeed")] [SerializeField]
        private float m_moveSpeed = 10.0f;

        /// <summary>
        /// Scale factor of the turbo mode.
        /// </summary>
        [FormerlySerializedAs("m_Turbo")] [SerializeField]
        private float m_turbo = 10.0f;

        [SerializeField] private string m_cameraID;

        private const string KeyMouseX = "Mouse X";
        private const string KeyMouseY = "Mouse Y";
        private const string KeyVertical = "Vertical";
        private const string KeyHorizontal = "Horizontal";

        private float _inputRotateAxisX, _inputRotateAxisY;
        private float _inputVertical, _inputHorizontal;
        private bool _leftShiftBoost, _leftShift, _fire1;

        private CharacterController _controller;

        private CinemachineVirtualCamera _selfCamera;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _selfCamera = GetComponent<CinemachineVirtualCamera>();

            Subscribe<string>("switchCamera", OnSwitchCamera);
        }

        private void Update()
        {
            UpdateInputs();

            bool moved = _inputRotateAxisX != 0.0f || _inputRotateAxisY != 0.0f || _inputVertical != 0.0f || _inputHorizontal != 0.0f;
            if (moved)
            {
                float rotationX = transform.localEulerAngles.x;
                float newRotationY = transform.localEulerAngles.y + _inputRotateAxisX;

                // Weird clamping code due to weird Euler angle mapping...
                float newRotationX = (rotationX - _inputRotateAxisY);
                if (rotationX <= 90.0f && newRotationX >= 0.0f)
                    newRotationX = Mathf.Clamp(newRotationX, 0.0f, 90.0f);
                if (rotationX >= 270.0f)
                    newRotationX = Mathf.Clamp(newRotationX, 270.0f, 360.0f);

                transform.localRotation = Quaternion.Euler(newRotationX, newRotationY, transform.localEulerAngles.z);

                float moveSpeed = Time.deltaTime * m_moveSpeed;
                if (_fire1 || _leftShiftBoost && _leftShift)
                    moveSpeed *= m_turbo;
                _controller.Move(transform.forward * (moveSpeed * _inputVertical) + transform.right * (moveSpeed * _inputHorizontal));
            }
        }

        private void OnSwitchCamera(string id)
        {
            if (id == m_cameraID)
            {
                Publish("switchCamera", _selfCamera);
            }
        }

        private void UpdateInputs()
        {
            _inputRotateAxisX = 0.0f;
            _inputRotateAxisY = 0.0f;
            _leftShiftBoost = false;
            _fire1 = false;

            if (Input.GetMouseButton(1))
            {
                _leftShiftBoost = true;
                _inputRotateAxisX = Input.GetAxis(KeyMouseX) * m_lookSpeedMouse;
                _inputRotateAxisY = Input.GetAxis(KeyMouseY) * m_lookSpeedMouse;
            }

            _leftShift = Input.GetKey(KeyCode.LeftShift);

            _inputVertical = Input.GetAxis(KeyVertical);
            _inputHorizontal = Input.GetAxis(KeyHorizontal);
        }
    }
}
