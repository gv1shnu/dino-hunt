using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DinoHunt.View
{
    /// <summary>
    /// Inspector description of an art model for one unit type (soldier or raptor). Leaving
    /// <see cref="prefab"/> empty keeps the greybox capsule, so the project runs with no art imported.
    /// </summary>
    [Serializable]
    public sealed class UnitModelSlot
    {
        [Tooltip("Rigged model prefab (e.g. a Quaternius FBX). Empty = greybox capsule.")]
        public GameObject prefab;

        [Tooltip("Uniform scale applied to the model instance. Tune until it matches the capsule's footprint.")]
        public float scale = 1f;

        [Tooltip("Extra yaw (degrees) for models whose forward isn't +Z.")]
        public float yawOffset;

        [Tooltip("Looping idle clip.")]
        public AnimationClip idle;

        [Tooltip("Looping run/walk clip.")]
        public AnimationClip move;

        [Tooltip("One-shot action clip (raptor bite / soldier shoot). Optional.")]
        public AnimationClip action;

        [Tooltip("World speed (units/s) at which the move clip plays at 1x. Faster/slower movement speeds the clip up/down.")]
        public float moveClipSpeed = 6f;

        [Tooltip("Only material slots whose name contains this text get the team tint (e.g. the uniform). Empty = tint every slot.")]
        public string tintMaterialFilter = "";

        [Tooltip("Child objects to hide by exact name (e.g. the spare weapons a character pack bundles on the hand bone).")]
        public string[] hideNodes = Array.Empty<string>();

        public bool HasModel => prefab != null;
    }

    /// <summary>
    /// Runtime driver for an instantiated art model: strips physics, applies a team tint, and blends
    /// idle/move/action clips with a tiny PlayableGraph (no AnimatorController asset needed).
    /// Pure presentation — reads positions the view already has and never touches the Simulation.
    /// </summary>
    public sealed class UnitModel : MonoBehaviour
    {
        private const float BlendRate = 8f;
        private static readonly int GltfBaseColor = Shader.PropertyToID("baseColorFactor");

        private UnitModelSlot _slot;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable _idle, _move, _action;
        private float _moveWeight;
        private float _actionTimeLeft;
        private Vector3 _lastPos;
        private float _speed;

        public static UnitModel Spawn(UnitModelSlot slot, Transform parent, Color? tint)
        {
            var go = Instantiate(slot.prefab, parent, false);
            go.name = "Model";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, slot.yawOffset, 0f);
            go.transform.localScale = Vector3.one * slot.scale;

            // Visualization only: imported models often carry colliders, which would block the
            // line-of-sight raycasts and leak non-determinism into the sim.
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);

            if (slot.hideNodes != null && slot.hideNodes.Length > 0)
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (Array.IndexOf(slot.hideNodes, t.name) >= 0) t.gameObject.SetActive(false);

            if (tint.HasValue) ApplyTint(go, tint.Value, slot.tintMaterialFilter);

            var model = go.AddComponent<UnitModel>();
            model.Init(slot);
            return model;
        }

        private static void ApplyTint(GameObject go, Color tint, string filter)
        {
            var block = new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (!string.IsNullOrEmpty(filter) &&
                        m.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // Multiply the slot's own color by the tint so texture detail survives.
                    // glTFast shaders name their albedo "baseColorFactor"; URP/Standard use _BaseColor/_Color.
                    Color baseColor = m.HasProperty(GltfBaseColor) ? m.GetColor(GltfBaseColor)
                                    : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                                    : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                    Color c = string.IsNullOrEmpty(filter) ? baseColor * tint : tint;
                    c.a = baseColor.a;

                    r.GetPropertyBlock(block, i);
                    block.SetColor(GltfBaseColor, c);
                    block.SetColor("_BaseColor", c);
                    block.SetColor("_Color", c);
                    r.SetPropertyBlock(block, i);
                }
            }
        }

        private void Init(UnitModelSlot slot)
        {
            _slot = slot;
            _lastPos = transform.position;
            if (slot.idle == null && slot.move == null) return;

            var animator = GetComponentInChildren<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false; // the sim owns position

            _graph = PlayableGraph.Create($"{name}_Anim");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _mixer = AnimationMixerPlayable.Create(_graph, 3);
            _idle = Connect(slot.idle, 0, 1f);
            _move = Connect(slot.move, 1, 0f);
            _action = Connect(slot.action, 2, 0f);

            var output = AnimationPlayableOutput.Create(_graph, "Anim", animator);
            output.SetSourcePlayable(_mixer);
            // Desync identical units so a squad doesn't run in lockstep.
            if (_move.IsValid() && slot.move.length > 0f)
                _move.SetTime(UnityEngine.Random.value * slot.move.length);
            _graph.Play();
        }

        private AnimationClipPlayable Connect(AnimationClip clip, int input, float weight)
        {
            if (clip == null) return default;
            var p = AnimationClipPlayable.Create(_graph, clip);
            _graph.Connect(p, 0, _mixer, input);
            _mixer.SetInputWeight(input, weight);
            return p;
        }

        /// <summary>Play the one-shot action clip (bite/shoot) over the locomotion blend.</summary>
        public void TriggerAction()
        {
            if (!_action.IsValid()) return;
            _action.SetTime(0);
            _actionTimeLeft = _slot.action.length;
        }

        private void LateUpdate()
        {
            if (!_graph.IsValid()) return;

            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                Vector3 delta = transform.position - _lastPos;
                delta.y = 0f;
                _speed = Mathf.Lerp(_speed, delta.magnitude / dt, 1f - Mathf.Exp(-BlendRate * dt));
            }
            _lastPos = transform.position;

            float targetMove = _move.IsValid() ? Mathf.Clamp01(_speed / (_slot.moveClipSpeed * 0.3f)) : 0f;
            if (!_idle.IsValid()) targetMove = 1f;
            _moveWeight = Mathf.MoveTowards(_moveWeight, targetMove, dt * BlendRate * 0.5f);
            if (_move.IsValid())
                _move.SetSpeed(Mathf.Clamp(_speed / Mathf.Max(0.01f, _slot.moveClipSpeed), 0.5f, 2f));

            float actionWeight = 0f;
            if (_actionTimeLeft > 0f)
            {
                _actionTimeLeft -= dt;
                // Ease in/out over a short window so the action doesn't pop.
                float len = _slot.action.length;
                actionWeight = Mathf.Clamp01(Mathf.Min(len - _actionTimeLeft, _actionTimeLeft) / 0.12f);
            }

            float loco = 1f - actionWeight;
            _mixer.SetInputWeight(0, loco * (1f - _moveWeight));
            _mixer.SetInputWeight(1, loco * _moveWeight);
            _mixer.SetInputWeight(2, actionWeight);
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
