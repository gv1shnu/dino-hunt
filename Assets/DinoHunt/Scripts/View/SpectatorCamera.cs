using UnityEngine;

namespace DinoHunt.View
{
    /// <summary>
    /// Elevated, tilted perspective camera giving a 3D oversight of the nest. The tilt restores
    /// the height information a flat top-down view throws away (cover, capsules read as solids).
    /// Pure spectator — the simulation never reads from it. Auto-director / follow is a later
    /// concern (GDD §12.4). Set pitch to 90 for the old straight-down orthographic look.
    ///
    /// Framed at a fixed distance from the given center rather than fit to the arena's footprint —
    /// the arena is now far larger than any single view, so "fit the whole thing in frame" would
    /// put the camera absurdly far away. The framing is re-applied every frame, so pitch / FOV /
    /// distance can be dialed in live in the Inspector during Play.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class SpectatorCamera : MonoBehaviour
    {
        [Tooltip("Downward tilt from horizontal, in degrees. 90 = straight down (top-down); ~50 = angled oversight.")]
        [SerializeField] private float pitchDegrees = 52f;

        [Tooltip("Vertical field of view for the perspective oversight.")]
        [SerializeField] private float fieldOfView = 38f;

        [Tooltip("Fixed distance from the framed center to the camera, in world units. ~110 frames the nest (radius 16), the raptors around it and arriving soldiers.")]
        [SerializeField] private float distance = 110f;

        private Camera _camera;
        private Vector3 _center;
        private float _farExtent;
        private bool _framed;

        /// <summary>Frame from a fixed distance, angled down at the given center (typically the nest).</summary>
        public void Frame(Vector3 center, float halfX, float halfZ)
        {
            _camera = GetComponent<Camera>();
            _center = center;
            _farExtent = Mathf.Max(halfX, halfZ); // keep the far clip generous enough to still render distant structures
            _framed = true;
            Apply();
        }

        private void LateUpdate()
        {
            if (_framed) Apply();
        }

        /// <summary>Stop auto-framing so a manual controller (free-fly cam) can own the transform.</summary>
        public void ReleaseControl() => _framed = false;

        private void Apply()
        {
            _camera.orthographic = false;
            _camera.fieldOfView = fieldOfView;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.07f, 0.08f, 0.11f);

            float pitch = pitchDegrees * Mathf.Deg2Rad;
            // Look direction: down by 'pitch' from horizontal, aimed toward +Z so the camera sits
            // to the south of the center and looks north across it.
            Vector3 forward = new Vector3(0f, -Mathf.Sin(pitch), Mathf.Cos(pitch)).normalized;

            transform.position = _center - forward * distance;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            _camera.nearClipPlane = 0.3f;
            _camera.farClipPlane = distance * 2f + _farExtent + 100f;
        }
    }
}
