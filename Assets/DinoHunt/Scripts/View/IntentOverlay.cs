using System.Collections.Generic;
using DinoHunt.Sim;
using UnityEngine;

namespace DinoHunt.View
{
    /// <summary>
    /// Draws each agent's current Intent string floating above its capsule. This is the first
    /// appearance of the project's core feature — legible minds — even though M2's decisions
    /// are stubs. Pure presentation: it only reads agent state.
    ///
    /// IMGUI labels are used deliberately: cheap, no TextMeshPro/canvas setup, and easy to
    /// read from directly overhead. It can be upgraded to world-space text later if needed.
    /// </summary>
    public sealed class IntentOverlay : MonoBehaviour
    {
        private Camera _camera;
        private IReadOnlyList<Agent> _agents;
        private IReadOnlyList<Raptor> _raptors;
        private GUIStyle _style;

        [SerializeField] private float labelHeight = 6f;

        [Tooltip("Extra world height per agent id, so labels of clustered agents don't overlap.")]
        [SerializeField] private float labelStagger = 1.8f;

        public void Init(Camera camera, IReadOnlyList<Agent> agents, IReadOnlyList<Raptor> raptors)
        {
            _camera = camera;
            _agents = agents;
            _raptors = raptors;
        }

        private void OnGUI()
        {
            if (_camera == null || _agents == null) return;

            _style ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };

            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (!agent.IsAlive) continue; // no body, no label

                float height = labelHeight + agent.Id * labelStagger;
                Vector3 screen = _camera.WorldToScreenPoint(agent.Position + Vector3.up * height);
                if (screen.z <= 0f) continue; // behind the camera

                _style.normal.textColor = agent.Team == Team.Blue
                    ? new Color(0.65f, 0.82f, 1f)
                    : new Color(1f, 0.72f, 0.62f);

                var rect = new Rect(screen.x - 120f, Screen.height - screen.y - 22f, 240f, 20f);
                string who = string.IsNullOrEmpty(agent.Name) ? "#" + agent.Id : agent.Name;
                GUI.Label(rect, $"{who} [{Mathf.CeilToInt(agent.Health)}] {AmmoLabel(agent.Weapon)}  {agent.Intent}", _style);
            }

            if (_raptors == null) return;
            for (int i = 0; i < _raptors.Count; i++)
            {
                var raptor = _raptors[i];
                if (!raptor.IsAlive) continue;

                Vector3 screen = _camera.WorldToScreenPoint(raptor.Position + Vector3.up * (labelHeight + 2f));
                if (screen.z <= 0f) continue;

                string state = raptor.State == RaptorState.Hunting && raptor.Target != null
                    ? "hunting " + (raptor.Target.Team == Team.Blue ? "blue" : "red") + "_" + raptor.Target.Id
                    : raptor.State == RaptorState.Returning ? "returning" : "prowling";

                _style.normal.textColor = new Color(0.7f, 0.85f, 0.4f);
                var rect = new Rect(screen.x - 120f, Screen.height - screen.y - 22f, 240f, 20f);
                GUI.Label(rect, $"raptor [{Mathf.CeilToInt(raptor.Health)}]: {state}", _style);
            }
        }

        /// <summary>Ammo state as short text — the gun-tilt reload animation alone is easy to miss.</summary>
        private static string AmmoLabel(WeaponState w)
        {
            if (w.IsReloading) return "RELOADING";
            return w.RifleDepleted ? $"sidearm {w.SidearmMag}" : $"{w.RifleMag}/{w.RifleReserve}";
        }
    }
}
