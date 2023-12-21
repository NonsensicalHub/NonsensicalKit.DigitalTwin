using Cinemachine;
using NonsensicalKit.Editor;
using UnityEngine;

namespace NonsensicalKit.Editor
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
        [SerializeField] private float m_LookSpeedMouse = 4.0f;
        /// <summary>
        /// Movement speed.
        /// </summary>
        [SerializeField] private float m_MoveSpeed = 10.0f;
        /// <summary>
        /// Scale factor of the turbo mode.
        /// </summary>
        [SerializeField] private float m_Turbo = 10.0f;
        [SerializeField] private string m_cameraID;

        private const string KEY_MOUSE_X = "Mouse X";
        private const string KEY_MOUSE_Y = "Mouse Y";
        private const string KEY_VERTICAL = "Vertical";
        private const string KEY_HORIZONTAL = "Horizontal";

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

                float moveSpeed = Time.deltaTime * m_MoveSpeed;
                if (_fire1 || _leftShiftBoost && _leftShift)
                    moveSpeed *= m_Turbo;
                _controller.Move(transform.forward * moveSpeed * _inputVertical + transform.right * moveSpeed * _inputHorizontal);
            }
        }

        private void OnSwitchCamera(string id)
        {
            if (id == m_cameraID)
            {
                Publish<CinemachineVirtualCamera>("switchCamera", _selfCamera);

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
                _inputRotateAxisX = Input.GetAxis(KEY_MOUSE_X) * m_LookSpeedMouse;
                _inputRotateAxisY = Input.GetAxis(KEY_MOUSE_Y) * m_LookSpeedMouse;
            }

            _leftShift = Input.GetKey(KeyCode.LeftShift);

            _inputVertical = Input.GetAxis(KEY_VERTICAL);
            _inputHorizontal = Input.GetAxis(KEY_HORIZONTAL);

        }
    }
}
