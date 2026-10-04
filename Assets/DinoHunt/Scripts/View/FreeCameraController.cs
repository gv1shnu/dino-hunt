using UnityEngine;
using UnityEngine.InputSystem;

namespace DinoHunt.View
{
    /// <summary>
    /// Free-fly spectator camera (GDD §12.4 "free-fly"). Lets the observer roam the battlefield:
    /// WASD pans on the ground plane, Q/E drop/raise, hold left OR right mouse to look around,
    /// scroll to dolly in/out, Shift to move faster. Left-drag is accepted alongside right-drag
    /// because this is a pure spectator view with no click interactions, and a laptop trackpad
    /// (notably on macOS) has no sustained right-button drag. The first input hands control off
    /// from the auto-framing
    /// SpectatorCamera. Pure view code — it uses Time.deltaTime freely and never touches the sim.
    ///
    /// Uses the Input System low-level API (Keyboard/Mouse.current) since the project is configured
    /// for the new Input System only.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class FreeCameraController : MonoBehaviour
    {
        [Tooltip("Pan/strafe speed in units/second.")]
        [SerializeField] private float moveSpeed = 80f;

        [Tooltip("Vertical (Q/E) speed in units/second.")]
        [SerializeField] private float verticalSpeed = 50f;

        [Tooltip("Screen-edge panning speed in units/second (move the cursor to a screen edge to slide the view).")]
        [SerializeField] private float edgePanSpeed = 60f;

        [Tooltip("How close to the screen edge (in pixels) triggers edge panning. 0 disables it.")]
        [SerializeField] private float edgePanBorder = 14f;

        [Tooltip("Speed multiplier while holding Shift.")]
        [SerializeField] private float boostMultiplier = 3f;

        [Tooltip("Mouse look sensitivity (degrees per pixel of mouse delta).")]
        [SerializeField] private float lookSensitivity = 0.12f;

        [Tooltip("Dolly units per scroll notch.")]
        [SerializeField] private float scrollSpeed = 0.08f;

        private SpectatorCamera _framer;
        private float _yaw;
        private float _pitch;
        private bool _controlling;

        // Edge panning only runs once a real pointer has moved over the view and while it is still
        // inside it. In a WebGL build the Input System reports (0,0) — the bottom-left corner —
        // until the first mouse move, and keeps the last position after the cursor leaves the
        // canvas, so without this the camera drifts off the arena on its own.
        private bool _pointerSeen;
        private bool _pointerInside = true;

        /// <summary>Called from the WebGL page (canvas mouseenter/mouseleave) via SendMessage.</summary>
        public void SetPointerInside(int inside) => _pointerInside = inside != 0;

        private void Awake()
        {
            _framer = GetComponent<SpectatorCamera>();
        }

        private void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            Vector3 planarMove = Vector3.zero;
            if (kb.wKey.isPressed) planarMove += PlanarForward();
            if (kb.sKey.isPressed) planarMove -= PlanarForward();
            if (kb.dKey.isPressed) planarMove += PlanarRight();
            if (kb.aKey.isPressed) planarMove -= PlanarRight();

            float vertical = 0f;
            if (kb.eKey.isPressed) vertical += 1f;
            if (kb.qKey.isPressed) vertical -= 1f;

            // Left OR right button drags the view. Trackpads (macOS especially) can't hold a
            // right-button drag, and there is nothing to click in a spectator view, so left works too.
            bool looking = mouse != null && (mouse.rightButton.isPressed || mouse.leftButton.isPressed);
            Vector2 lookDelta = looking ? mouse.delta.ReadValue() : Vector2.zero;
            float scroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;
            if (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0f) _pointerSeen = true;

            // Screen-edge panning: push the cursor to a screen edge to slide the view (disabled
            // while looking so a right-drag to the edge doesn't also pan).
            Vector3 edgePan = Vector3.zero;
            if (edgePanBorder > 0f && mouse != null && !looking && Application.isFocused && _pointerSeen && _pointerInside)
            {
                Vector2 mp = mouse.position.ReadValue();
                if (mp.x >= 0f && mp.x <= Screen.width && mp.y >= 0f && mp.y <= Screen.height)
                {
                    if (mp.x < edgePanBorder) edgePan -= PlanarRight();
                    else if (mp.x > Screen.width - edgePanBorder) edgePan += PlanarRight();
                    if (mp.y < edgePanBorder) edgePan -= PlanarForward();
                    else if (mp.y > Screen.height - edgePanBorder) edgePan += PlanarForward();
                }
            }

            bool anyInput = planarMove.sqrMagnitude > 0f || Mathf.Abs(vertical) > 0f
                            || lookDelta.sqrMagnitude > 0f || Mathf.Abs(scroll) > 0.01f
                            || edgePan.sqrMagnitude > 0f;

            if (anyInput && !_controlling) BeginControl();
            if (!_controlling) return;

            float dt = Time.deltaTime;
            float boost = kb.leftShiftKey.isPressed ? boostMultiplier : 1f;

            transform.position += planarMove.normalized * (moveSpeed * boost * dt);
            transform.position += edgePan.normalized * (edgePanSpeed * boost * dt);
            transform.position += Vector3.up * (vertical * verticalSpeed * dt);
            if (Mathf.Abs(scroll) > 0.01f)
                transform.position += transform.forward * (scroll * scrollSpeed);

            if (looking && lookDelta.sqrMagnitude > 0f)
            {
                _yaw += lookDelta.x * lookSensitivity;
                _pitch -= lookDelta.y * lookSensitivity;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
        }

        private void BeginControl()
        {
            _controlling = true;
            if (_framer != null) _framer.ReleaseControl();

            Vector3 e = transform.eulerAngles;
            _pitch = e.x > 180f ? e.x - 360f : e.x;
            _yaw = e.y;
        }

        private Vector3 PlanarForward()
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }

        private Vector3 PlanarRight()
        {
            Vector3 r = transform.right;
            r.y = 0f;
            return r.sqrMagnitude > 1e-4f ? r.normalized : Vector3.right;
        }
    }
}
