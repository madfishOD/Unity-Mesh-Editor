using System;
using UnityEngine;

namespace MeshEditTools
{
    [Flags]
    public enum MeshElementFlags : byte
    {
        None = 0,
        Selected = 1 << 0
    }

    [Serializable] public struct VertId { public int Value; public bool IsValid => Value >= 0; public VertId(int v) => Value = v; }
    [Serializable] public struct EdgeId { public int Value; public bool IsValid => Value >= 0; public EdgeId(int v) => Value = v; }
    [Serializable] public struct FaceId { public int Value; public bool IsValid => Value >= 0; public FaceId(int v) => Value = v; }
    [Serializable] public struct LoopId { public int Value; public bool IsValid => Value >= 0; public LoopId(int v) => Value = v; }
    
    [Serializable]
    public struct BmVert
    {
        public Vector3 Position;

        // One outgoing loop that uses this vertex (for traversal). -1 if none.
        public int AnyLoop;
        public byte Flags; // selection, tags, etc later
    }

    [Serializable]
    public struct BmEdge
    {
        public VertId V0;
        public VertId V1;

        // One loop that uses this edge (to walk radial cycles around the edge). -1 if none.
        public int AnyLoop;

        public byte Flags; // sharp, seam, etc later
    }

    [Serializable]
    public struct BmFace
    {
        public int AnyLoop; // a loop on this face (ring entry), -1 if none
        public int LoopCount;

        public int MaterialIndex; // future splitting/submeshes
        public byte Flags;
    }

    [Serializable]
    public struct BmLoop
    {
        public FaceId Face;
        public VertId Vert;
        public EdgeId Edge;

        // Face ring
        public int Next;
        public int Prev;

        // Radial cycle around an edge: all face-corners that share that edge
        public int RadialNext;
        public int RadialPrev;

        public byte Flags;

        // NOTE: per-corner attributes (UV/normal override/color) should live in an attribute system later.
        // For prototype UV0 you *can* temporarily put it here:
        public Vector2 UV0;
    }
    
    [Serializable]
    public class SlotList<T>
    {
        [SerializeField] private T[] Items = Array.Empty<T>();
        [SerializeField] private bool[] Alive = Array.Empty<bool>();
        [SerializeField] private int[] Free = Array.Empty<int>();
        [SerializeField] private int FreeCount;

        public int Capacity => Items.Length;

        public ref T this[int id] => ref Items[id];
        public bool IsAlive(int id) => (uint)id < (uint)Alive.Length && Alive[id];

        public int Allocate()
        {
            if (FreeCount > 0)
            {
                int id = Free[--FreeCount];
                Alive[id] = true;
                return id;
            }
            int newId = Items.Length;
            Array.Resize(ref Items, newId + 1);
            Array.Resize(ref Alive, newId + 1);
            Alive[newId] = true;
            return newId;
        }

        public void FreeItem(int id)
        {
            if (!IsAlive(id)) return;
            Alive[id] = false;
            if (FreeCount == Free.Length) Array.Resize(ref Free, Math.Max(4, Free.Length * 2));
            Free[FreeCount++] = id;
        }
    }

}
