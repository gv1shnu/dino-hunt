using DinoHunt.View;
using UnityEngine;

namespace DinoHunt.Arena
{
    /// <summary>
    /// Builds the symmetric greybox arena from primitives at startup. Presentation +
    /// collision only — it produces the physical world the simulation reads via raycasts
    /// and NavMesh, but holds no game logic and decides no layout itself.
    ///
    /// The layout comes from ArenaGeometry, so the rendered arena and the headless batch
    /// arena are the same world for a given seed. This class only decides how to draw it.
    /// </summary>
    public sealed class ArenaBuilder
    {
        private ArenaLayout _layout;
        private Transform _root;

        public Transform Root => _root;

        /// <summary>The layout actually used, after any procedural variation. Read this, not the authored asset.</summary>
        public ArenaLayout ResolvedLayout => _layout;

        public ArenaBuilder(ArenaLayout layout)
        {
            _layout = layout;
        }

        public Vector3 NestCenter => ArenaGeometry.NestCenter;
        public Vector3 BlueBaseCenter => ArenaGeometry.BlueBaseCenter(_layout);
        public Vector3 RedBaseCenter => ArenaGeometry.RedBaseCenter(_layout);

        public float HalfX => ArenaGeometry.HalfX(_layout);
        public float HalfZ => ArenaGeometry.HalfZ(_layout);
        public float Extent => ArenaGeometry.Extent(_layout);

        public void Build(ulong seed)
        {
            // Resolve first: with procedural variation on this picks the match's arena, and every
            // consumer (geometry, waypoints, camera framing) must see the same resolved values.
            _layout = ArenaGeometry.Resolve(_layout, seed);

            var root = new GameObject("Arena");
            _root = root.transform;

            BuildGround();
            BuildNest();
            BuildBase("BlueBase", BlueBaseCenter, GreyboxMaterials.BlueBase);
            BuildBase("RedBase", RedBaseCenter, GreyboxMaterials.RedBase);

            var boxes = ArenaGeometry.Build(_layout, seed, GreyboxMaterials.Buildings.Length);

            var coverRoot = new GameObject("Cover").transform;
            var structureRoot = new GameObject("Structures").transform;
            var nestScatterRoot = new GameObject("NestScatter").transform;
            coverRoot.SetParent(_root, false);
            structureRoot.SetParent(_root, false);
            nestScatterRoot.SetParent(_root, false);

            for (int i = 0; i < boxes.Count; i++)
            {
                ArenaBox b = boxes[i];
                Transform parent = b.Kind == ArenaBoxKind.Cover ? coverRoot
                                 : b.Kind == ArenaBoxKind.Structure ? structureRoot
                                 : nestScatterRoot;
                Color color = b.ColorIndex < 0
                    ? GreyboxMaterials.Cover
                    : GreyboxMaterials.Buildings[b.ColorIndex % GreyboxMaterials.Buildings.Length];

                var go = CreatePrimitive(PrimitiveType.Cube, $"{b.Kind}_{i}", color, parent);
                go.transform.localScale = b.Size;
                go.transform.position = b.Center;
            }
        }

        private GameObject CreatePrimitive(PrimitiveType type, string name, Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = GreyboxMaterials.Get(color);
            return go;
        }

        private void BuildGround()
        {
            var ground = CreatePrimitive(PrimitiveType.Plane, "Ground", GreyboxMaterials.Ground, _root);
            // Unity's plane is 10x10 units at scale 1; scale to cover the arena with margin.
            float span = (Extent + 6f) * 2f;
            ground.transform.localScale = new Vector3(span / 10f, 1f, span / 10f);
            ground.transform.position = Vector3.zero;
        }

        private void BuildNest()
        {
            var nest = CreatePrimitive(PrimitiveType.Cylinder, "Nest", GreyboxMaterials.Nest, _root);
            // Unity cylinder is 2 units tall at scale 1; flatten it into a low disc marker.
            nest.transform.localScale = new Vector3(_layout.nestRadius * 2f, 0.05f, _layout.nestRadius * 2f);
            nest.transform.position = NestCenter + new Vector3(0f, 0.05f, 0f);
        }

        private void BuildBase(string name, Vector3 center, Color color)
        {
            var baseGo = CreatePrimitive(PrimitiveType.Cube, name, color, _root);
            baseGo.transform.localScale = new Vector3(_layout.baseSize.x, 0.1f, _layout.baseSize.y);
            baseGo.transform.position = center + new Vector3(0f, 0.05f, 0f);
        }
    }
}
