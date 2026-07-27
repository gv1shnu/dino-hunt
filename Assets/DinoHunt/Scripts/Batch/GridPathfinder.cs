using System.Collections.Generic;
using DinoHunt.Arena;
using DinoHunt.Sim;
using UnityEngine;

namespace DinoHunt.Batch
{
    /// <summary>
    /// A* over a coarse occupancy grid built from the arena's boxes. The headless counterpart
    /// to NavMeshPathfinder, so batch matches route around buildings the way played ones do.
    ///
    /// Deliberately grid-based rather than a NavMesh reimplementation: the arena is flat with
    /// axis-aligned obstacles, so a grid captures the routing decisions that matter (which way
    /// around a building, which lane to the nest) without any Unity subsystem. Paths are
    /// string-pulled afterwards so agents walk clean diagonals instead of stair-stepping.
    ///
    /// Fidelity note: this will not produce byte-identical routes to Unity's NavMesh. Batch runs
    /// are internally consistent and reproducible, which is what research requires; they are not
    /// claimed to be step-for-step identical to a rendered match.
    /// </summary>
    public sealed class GridPathfinder : IPathfinder
    {
        private readonly float _cell;
        private readonly float _originX, _originZ;
        private readonly int _w, _h;
        private readonly bool[] _blocked;

        // A* scratch, reused across queries so repathing doesn't allocate.
        private readonly float[] _g;
        private readonly int[] _cameFrom;
        private readonly int[] _stamp;
        private int _currentStamp;
        private readonly MinHeap _open;

        public GridPathfinder(IReadOnlyList<ArenaBox> boxes, ArenaLayout layout, float cellSize = 12f, float agentRadius = 2f)
        {
            _cell = cellSize;
            float halfX = ArenaGeometry.HalfX(layout) + cellSize * 2f;
            float halfZ = ArenaGeometry.HalfZ(layout) + cellSize * 2f;
            _originX = -halfX;
            _originZ = -halfZ;
            _w = Mathf.CeilToInt(halfX * 2f / cellSize) + 1;
            _h = Mathf.CeilToInt(halfZ * 2f / cellSize) + 1;

            _blocked = new bool[_w * _h];
            _g = new float[_w * _h];
            _cameFrom = new int[_w * _h];
            _stamp = new int[_w * _h];
            _open = new MinHeap(1024);

            // Inflate obstacles by the agent radius so paths don't scrape corners.
            for (int i = 0; i < boxes.Count; i++)
            {
                Vector3 mn = boxes[i].Min, mx = boxes[i].Max;
                int x0 = ClampX(Mathf.FloorToInt((mn.x - agentRadius - _originX) / _cell));
                int x1 = ClampX(Mathf.CeilToInt((mx.x + agentRadius - _originX) / _cell));
                int z0 = ClampZ(Mathf.FloorToInt((mn.z - agentRadius - _originZ) / _cell));
                int z1 = ClampZ(Mathf.CeilToInt((mx.z + agentRadius - _originZ) / _cell));

                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        _blocked[z * _w + x] = true;
            }
        }

        private int ClampX(int x) => x < 0 ? 0 : (x >= _w ? _w - 1 : x);
        private int ClampZ(int z) => z < 0 ? 0 : (z >= _h ? _h - 1 : z);

        private int CellX(float worldX) => ClampX(Mathf.RoundToInt((worldX - _originX) / _cell));
        private int CellZ(float worldZ) => ClampZ(Mathf.RoundToInt((worldZ - _originZ) / _cell));
        private Vector3 CellCenter(int idx)
        {
            int x = idx % _w, z = idx / _w;
            return new Vector3(_originX + x * _cell, 0f, _originZ + z * _cell);
        }

        public bool TryFindPath(Vector3 start, Vector3 end, List<Vector3> cornersOut)
        {
            cornersOut.Clear();

            // Open ground between the two points is the common case — skip the search entirely.
            if (Walkable(start, end))
            {
                cornersOut.Add(start);
                cornersOut.Add(end);
                return true;
            }

            int startIdx = NearestFree(CellZ(start.z) * _w + CellX(start.x));
            int goalIdx = NearestFree(CellZ(end.z) * _w + CellX(end.x));
            if (startIdx < 0 || goalIdx < 0)
            {
                cornersOut.Add(start);
                cornersOut.Add(end); // no route: fall back to a straight line rather than freezing
                return true;
            }

            if (!Search(startIdx, goalIdx))
            {
                cornersOut.Add(start);
                cornersOut.Add(end);
                return true;
            }

            // Walk the parent chain back, then reverse into world space.
            var raw = new List<Vector3>(32) { end };
            int cur = goalIdx;
            while (cur != startIdx)
            {
                raw.Add(CellCenter(cur));
                cur = _cameFrom[cur];
            }
            raw.Add(start);
            raw.Reverse();

            StringPull(raw, cornersOut);
            return true;
        }

        private bool Search(int startIdx, int goalIdx)
        {
            _currentStamp++;
            _open.Clear();

            _g[startIdx] = 0f;
            _stamp[startIdx] = _currentStamp;
            _cameFrom[startIdx] = startIdx;
            _open.Push(startIdx, Heuristic(startIdx, goalIdx));

            int guard = _w * _h;
            while (_open.Count > 0 && guard-- > 0)
            {
                int cur = _open.Pop();
                if (cur == goalIdx) return true;

                int cx = cur % _w, cz = cur / _w;
                float gCur = _g[cur];

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nx >= _w || nz < 0 || nz >= _h) continue;

                        int n = nz * _w + nx;
                        if (_blocked[n]) continue;

                        // Don't cut corners diagonally between two blocked cells.
                        if (dx != 0 && dz != 0 && (_blocked[cz * _w + nx] || _blocked[nz * _w + cx])) continue;

                        float step = (dx != 0 && dz != 0) ? 1.41421356f : 1f;
                        float ng = gCur + step;

                        if (_stamp[n] == _currentStamp && ng >= _g[n]) continue;
                        _stamp[n] = _currentStamp;
                        _g[n] = ng;
                        _cameFrom[n] = cur;
                        _open.Push(n, ng + Heuristic(n, goalIdx));
                    }
                }
            }
            return false;
        }

        private float Heuristic(int a, int b)
        {
            int ax = a % _w, az = a / _w, bx = b % _w, bz = b / _w;
            int dx = ax > bx ? ax - bx : bx - ax;
            int dz = az > bz ? az - bz : bz - az;
            int min = dx < dz ? dx : dz, max = dx < dz ? dz : dx;
            return max - min + 1.41421356f * min; // octile
        }

        /// <summary>Spiral out to the closest open cell — spawn points can sit inside inflated geometry.</summary>
        private int NearestFree(int idx)
        {
            if (!_blocked[idx]) return idx;
            int cx = idx % _w, cz = idx / _w;
            for (int r = 1; r < 24; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue; // ring only
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nx >= _w || nz < 0 || nz >= _h) continue;
                        int n = nz * _w + nx;
                        if (!_blocked[n]) return n;
                    }
                }
            }
            return -1;
        }

        /// <summary>Drop intermediate corners the agent can simply walk past — turns stair-steps into clean legs.</summary>
        private void StringPull(List<Vector3> raw, List<Vector3> outCorners)
        {
            outCorners.Add(raw[0]);
            int anchor = 0;
            for (int i = 2; i < raw.Count; i++)
            {
                if (Walkable(raw[anchor], raw[i])) continue;
                outCorners.Add(raw[i - 1]);
                anchor = i - 1;
            }
            outCorners.Add(raw[raw.Count - 1]);
        }

        /// <summary>Sample the straight line between two points against the occupancy grid.</summary>
        private bool Walkable(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a; d.y = 0f;
            float dist = d.magnitude;
            if (dist < 1e-4f) return true;

            int steps = Mathf.CeilToInt(dist / (_cell * 0.5f));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float x = a.x + d.x * t, z = a.z + d.z * t;
                if (_blocked[CellZ(z) * _w + CellX(x)]) return false;
            }
            return true;
        }

        /// <summary>Tiny binary heap keyed by f-score. Ties break by insertion index, so searches stay deterministic.</summary>
        private sealed class MinHeap
        {
            private int[] _items;
            private float[] _keys;
            public int Count;

            public MinHeap(int capacity)
            {
                _items = new int[capacity];
                _keys = new float[capacity];
            }

            public void Clear() => Count = 0;

            public void Push(int item, float key)
            {
                if (Count == _items.Length)
                {
                    System.Array.Resize(ref _items, Count * 2);
                    System.Array.Resize(ref _keys, Count * 2);
                }
                int i = Count++;
                _items[i] = item;
                _keys[i] = key;
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (_keys[parent] <= _keys[i]) break;
                    Swap(parent, i);
                    i = parent;
                }
            }

            public int Pop()
            {
                int top = _items[0];
                Count--;
                if (Count > 0)
                {
                    _items[0] = _items[Count];
                    _keys[0] = _keys[Count];
                    int i = 0;
                    while (true)
                    {
                        int l = i * 2 + 1, r = l + 1, best = i;
                        if (l < Count && _keys[l] < _keys[best]) best = l;
                        if (r < Count && _keys[r] < _keys[best]) best = r;
                        if (best == i) break;
                        Swap(best, i);
                        i = best;
                    }
                }
                return top;
            }

            private void Swap(int a, int b)
            {
                (_items[a], _items[b]) = (_items[b], _items[a]);
                (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
            }
        }
    }
}
