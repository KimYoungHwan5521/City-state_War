using UnityEngine;
using UnityEngine.InputSystem;

namespace LittleCiv.Runtime
{
    public sealed class PrototypeMapCamera : MonoBehaviour
    {
        public float PanSpeed = 12f;
        public float ZoomSpeed = 100f;
        public float MinimumSize = 4f;
        public float MaximumSize = 60f;

        private Camera targetCamera;

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null)
            {
                var horizontal = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                var vertical = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                transform.position += new Vector3(horizontal, 0f, vertical) * (PanSpeed * Time.deltaTime);
            }

            var scroll = mouse == null ? 0f : mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.1f)
            {
                var wheelSteps = scroll / 120f;
                targetCamera.orthographicSize = Mathf.Clamp(
                    targetCamera.orthographicSize - (wheelSteps * ZoomSpeed),
                    MinimumSize,
                    MaximumSize);
            }
        }
    }
}
