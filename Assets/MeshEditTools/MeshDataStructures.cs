using System;
using UnityEngine;

namespace MeshEditTools
{
    /// <summary>
    /// Flags for editable mesh elements (selection, tagging, etc).
    /// </summary>
    [Flags]
    public enum MeshElementFlags : byte
    {
        None = 0,
        Selected = 1 << 0
    }

    /// <summary>
    /// Identifier wrapper for vertices stored in a <see cref="SlotList{T}"/>.
    /// </summary>
    [Serializable]
    public struct VertId
    {
        /// <summary>
        /// Raw integer value for the vertex index.
        /// </summary>
        public int Value;
        /// <summary>
        /// Returns true when the id refers to a valid slot.
        /// </summary>
        public bool IsValid => Value >= 0;
        /// <summary>
        /// Creates a vertex id from a slot index.
        /// </summary>
        public VertId(int v) => Value = v;
    }

    /// <summary>
    /// Identifier wrapper for edges stored in a <see cref="SlotList{T}"/>.
    /// </summary>
    [Serializable]
    public struct EdgeId
    {
        /// <summary>
        /// Raw integer value for the edge index.
        /// </summary>
        public int Value;
        /// <summary>
        /// Returns true when the id refers to a valid slot.
        /// </summary>
        public bool IsValid => Value >= 0;
        /// <summary>
        /// Creates an edge id from a slot index.
        /// </summary>
        public EdgeId(int v) => Value = v;
    }

    /// <summary>
    /// Identifier wrapper for faces stored in a <see cref="SlotList{T}"/>.
    /// </summary>
    [Serializable]
    public struct FaceId
    {
        /// <summary>
        /// Raw integer value for the face index.
        /// </summary>
        public int Value;
        /// <summary>
        /// Returns true when the id refers to a valid slot.
        /// </summary>
        public bool IsValid => Value >= 0;
        /// <summary>
        /// Creates a face id from a slot index.
        /// </summary>
        public FaceId(int v) => Value = v;
    }

    /// <summary>
    /// Identifier wrapper for loops stored in a <see cref="SlotList{T}"/>.
    /// </summary>
    [Serializable]
    public struct LoopId
    {
        /// <summary>
        /// Raw integer value for the loop index.
        /// </summary>
        public int Value;
        /// <summary>
        /// Returns true when the id refers to a valid slot.
        /// </summary>
        public bool IsValid => Value >= 0;
        /// <summary>
        /// Creates a loop id from a slot index.
        /// </summary>
        public LoopId(int v) => Value = v;
    }
    
    /// <summary>
    /// Vertex data stored in an editable mesh.
    /// </summary>
    [Serializable]
    public struct BmVert
    {
        /// <summary>
        /// Local-space position for the vertex.
        /// </summary>
        public Vector3 Position;

        // One outgoing loop that uses this vertex (for traversal). -1 if none.
        /// <summary>
        /// Index of one loop using this vertex, or -1 if none.
        /// </summary>
        public int AnyLoop;
        /// <summary>
        /// Flags for selection or tagging.
        /// </summary>
        public byte Flags; // selection, tags, etc later
    }

    /// <summary>
    /// Edge data stored in an editable mesh.
    /// </summary>
    [Serializable]
    public struct BmEdge
    {
        /// <summary>
        /// First vertex id of the edge.
        /// </summary>
        public VertId V0;
        /// <summary>
        /// Second vertex id of the edge.
        /// </summary>
        public VertId V1;

        // One loop that uses this edge (to walk radial cycles around the edge). -1 if none.
        /// <summary>
        /// Index of one loop using this edge, or -1 if none.
        /// </summary>
        public int AnyLoop;

        /// <summary>
        /// Flags for selection or tagging.
        /// </summary>
        public byte Flags; // sharp, seam, etc later
    }

    /// <summary>
    /// Face data stored in an editable mesh.
    /// </summary>
    [Serializable]
    public struct BmFace
    {
        /// <summary>
        /// Index of one loop on this face, or -1 if none.
        /// </summary>
        public int AnyLoop; // a loop on this face (ring entry), -1 if none
        /// <summary>
        /// Number of loops in this face.
        /// </summary>
        public int LoopCount;

        /// <summary>
        /// Material index used for submesh assignment.
        /// </summary>
        public int MaterialIndex; // future splitting/submeshes
        /// <summary>
        /// Flags for selection or tagging.
        /// </summary>
        public byte Flags;
    }

    /// <summary>
    /// Loop/corner data connecting faces, edges, and vertices.
    /// </summary>
    [Serializable]
    public struct BmLoop
    {
        /// <summary>
        /// Face id this loop belongs to.
        /// </summary>
        public FaceId Face;
        /// <summary>
        /// Vertex id referenced by this loop.
        /// </summary>
        public VertId Vert;
        /// <summary>
        /// Edge id referenced by this loop.
        /// </summary>
        public EdgeId Edge;

        // Face ring
        /// <summary>
        /// Next loop in the face ring.
        /// </summary>
        public int Next;
        /// <summary>
        /// Previous loop in the face ring.
        /// </summary>
        public int Prev;

        // Radial cycle around an edge: all face-corners that share that edge
        /// <summary>
        /// Next loop in the radial cycle around the edge.
        /// </summary>
        public int RadialNext;
        /// <summary>
        /// Previous loop in the radial cycle around the edge.
        /// </summary>
        public int RadialPrev;

        /// <summary>
        /// Flags for selection or tagging.
        /// </summary>
        public byte Flags;

        // NOTE: per-corner attributes (UV/normal override/color) should live in an attribute system later.
        // For prototype UV0 you *can* temporarily put it here:
        /// <summary>
        /// UV0 coordinate stored per corner.
        /// </summary>
        public Vector2 UV0;
    }
    
    /// <summary>
    /// Sparse slot list that keeps stable indices and supports reuse.
    /// </summary>
    [Serializable]
    public class SlotList<T>
    {
        [SerializeField] private T[] Items = Array.Empty<T>();
        [SerializeField] private bool[] Alive = Array.Empty<bool>();
        [SerializeField] private int[] Free = Array.Empty<int>();
        [SerializeField] private int FreeCount;

        /// <summary>
        /// Number of allocated slots (including free slots).
        /// </summary>
        public int Capacity => Items.Length;

        /// <summary>
        /// Gets a ref to an element by slot id.
        /// </summary>
        public ref T this[int id] => ref Items[id];
        /// <summary>
        /// Returns true if the slot id is within bounds and marked alive.
        /// </summary>
        public bool IsAlive(int id) => (uint)id < (uint)Alive.Length && Alive[id];

        /// <summary>
        /// Allocates a new slot id, reusing a freed slot if available.
        /// </summary>
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

        /// <summary>
        /// Frees a slot id for reuse.
        /// </summary>
        public void FreeItem(int id)
        {
            if (!IsAlive(id)) return;
            Alive[id] = false;
            if (FreeCount == Free.Length) Array.Resize(ref Free, Math.Max(4, Free.Length * 2));
            Free[FreeCount++] = id;
        }
    }

}
