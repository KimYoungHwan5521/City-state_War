using UnityEngine;
using UnityEngine.InputSystem;

namespace LittleCiv.Runtime
{
    public sealed class PrototypeMapCamera : MonoBehaviour
    {
        public float PanSpeed = 12f;
        public float ZoomSpeed = 5f;
        public float MinimumSize = 4f;
        public float MaximumSize = 30f;

        private Camera targetCamera;

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null)
            {
                return;
            }

            var horizontal = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            var vertical = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            transform.position += new Vector3(horizontal, 0f, vertical) * (PanSpeed * Time.deltaTime);

            var scroll = mouse == null ? 0f : mouse.scroll.ReadValue().y * 0.01f;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                targetCamera.orthographicSize = Mathf.Clamp(
                    targetCamera.orthographicSize - (scroll * ZoomSpeed),
                    MinimumSize,
                    MaximumSize);
            }
        }
    }
}
