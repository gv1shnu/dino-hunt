using UnityEngine;

namespace DinoHunt.View
{
    /// <summary>
    /// A visual tracer for a fired shot. Combat is hitscan in the sim (resolved instantly); this
    /// is purely cosmetic — a small bright bullet that streaks from shooter to target, then
    /// destroys itself. Nothing in the simulation depends on it.
    /// </summary>
    public sealed class BulletFx : MonoBehaviour
    {
        private Vector3 _start;
        private Vector3 _end;
        private float _duration;
        private float _elapsed;

        public static void Spawn(Vector3 start, Vector3 end, float speed, float thickness)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Bullet";
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = GreyboxMaterials.Get(GreyboxMaterials.Bullet);

            float length = Mathf.Max(1.5f, thickness * 6f);
            go.transform.localScale = new Vector3(thickness, thickness, length);
            if ((end - start).sqrMagnitude > 1e-4f)
                go.transform.rotation = Quaternion.LookRotation(end - start);

            var fx = go.AddComponent<BulletFx>();
            fx._start = start;
            fx._end = end;
            fx._duration = Mathf.Max(0.02f, Vector3.Distance(start, end) / Mathf.Max(1f, speed));
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _elapsed / _duration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            transform.position = Vector3.Lerp(_start, _end, t);
        }
    }
}
