using System;
using UnityEngine;

namespace MeshEditTools
{
[Serializable]
public class EditableMesh
{
    public SlotList<BmVert> Verts = new();
    public SlotList<BmEdge> Edges = new();
    public SlotList<BmFace> Faces = new();
    public SlotList<BmLoop> Loops = new();

    // Optional: quick edge lookup so we don’t create duplicates
    // Key: (minVert,maxVert) -> edgeId
    [NonSerialized] private System.Collections.Generic.Dictionary<ulong, int> _edgeMap;

    public void RebuildCaches()
    {
        _edgeMap = new System.Collections.Generic.Dictionary<ulong, int>(1024);
        for (int e = 0; e < Edges.Capacity; e++)
        {
            if (!Edges.IsAlive(e)) continue;
            var ed = Edges[e];
            var a = ed.V0.Value;
            var b = ed.V1.Value;
            ulong key = MakeEdgeKey(a, b);
            _edgeMap[key] = e;
        }
    }

    static ulong MakeEdgeKey(int a, int b)
    {
        uint lo = (uint)Mathf.Min(a, b);
        uint hi = (uint)Mathf.Max(a, b);
        return ((ulong)hi << 32) | lo;
    }

    public VertId AddVert(Vector3 pos)
    {
        int id = Verts.Allocate();
        Verts[id] = new BmVert { Position = pos, AnyLoop = -1, Flags = 0 };
        return new VertId(id);
    }

    public EdgeId GetOrCreateEdge(VertId a, VertId b)
    {
        _edgeMap ??= new System.Collections.Generic.Dictionary<ulong, int>(1024);
        ulong key = MakeEdgeKey(a.Value, b.Value);
        if (_edgeMap.TryGetValue(key, out int existing))
            return new EdgeId(existing);

        int id = Edges.Allocate();
        Edges[id] = new BmEdge { V0 = a, V1 = b, AnyLoop = -1, Flags = 0 };
        _edgeMap[key] = id;
        return new EdgeId(id);
    }

    /// <summary>
    /// Creates a face from an ordered list of vertex ids (must be >= 3).
    /// Builds loops, face ring, and radial links for each edge.
    /// </summary>
    public FaceId AddFace(System.Collections.Generic.IReadOnlyList<VertId> faceVerts, int materialIndex = 0)
    {
        if (faceVerts == null || faceVerts.Count < 3)
            throw new ArgumentException("Face needs at least 3 verts.");

        // Allocate face
        int fId = Faces.Allocate();
        Faces[fId] = new BmFace { AnyLoop = -1, LoopCount = faceVerts.Count, MaterialIndex = materialIndex, Flags = 0 };

        // Allocate loops
        int n = faceVerts.Count;
        int firstLoop = -1;

        // Create loops with vert/edge
        int[] loopIds = new int[n];

        for (int i = 0; i < n; i++)
        {
            VertId vThis = faceVerts[i];
            VertId vNext = faceVerts[(i + 1) % n];

            EdgeId e = GetOrCreateEdge(vThis, vNext);

            int lId = Loops.Allocate();
            loopIds[i] = lId;

            Loops[lId] = new BmLoop
            {
                Face = new FaceId(fId),
                Vert = vThis,
                Edge = e,
                Next = -1,
                Prev = -1,
                RadialNext = -1,
                RadialPrev = -1,
                Flags = 0,
                UV0 = Vector2.zero
            };

            // attach anyLoop pointers (useful for traversal)
            ref var vv = ref Verts[vThis.Value];
            if (vv.AnyLoop < 0) vv.AnyLoop = lId;

            ref var ee = ref Edges[e.Value];
            if (ee.AnyLoop < 0) ee.AnyLoop = lId;

            if (firstLoop < 0) firstLoop = lId;
        }

        // Link face ring next/prev
        for (int i = 0; i < n; i++)
        {
            int lId = loopIds[i];
            int lNext = loopIds[(i + 1) % n];
            int lPrev = loopIds[(i - 1 + n) % n];

            ref var l = ref Loops[lId];
            l.Next = lNext;
            l.Prev = lPrev;
        }

        // Set face entry loop
        ref var ff = ref Faces[fId];
        ff.AnyLoop = firstLoop;

        // Link radials around edges (doubly-linked cycle per edge)
        for (int i = 0; i < n; i++)
            InsertLoopIntoEdgeRadial(loopIds[i]);

        return new FaceId(fId);
    }

    private void InsertLoopIntoEdgeRadial(int loopId)
    {
        ref var l = ref Loops[loopId];
        int eId = l.Edge.Value;

        ref var e = ref Edges[eId];

        if (e.AnyLoop < 0)
        {
            // first loop on this edge: self-cycle
            e.AnyLoop = loopId;
            l.RadialNext = loopId;
            l.RadialPrev = loopId;
            return;
        }

        // Insert into existing cycle: (a <-> b) becomes (a <-> new <-> b)
        int a = e.AnyLoop;
        ref var la = ref Loops[a];
        int b = la.RadialNext;

        l.RadialPrev = a;
        l.RadialNext = b;

        ref var lb = ref Loops[b];
        la.RadialNext = loopId;
        lb.RadialPrev = loopId;
    }
}

}
