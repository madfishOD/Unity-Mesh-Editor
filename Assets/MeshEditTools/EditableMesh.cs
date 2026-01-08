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

    public FaceId AddFace(System.Collections.Generic.IReadOnlyList<VertId> faceVerts, System.Collections.Generic.IReadOnlyList<Vector2> uv0, int materialIndex = 0)
    {
        var faceId = AddFace(faceVerts, materialIndex);
        if (uv0 != null)
        {
            SetFaceUVs(faceId, uv0);
        }
        return faceId;
    }

    public void SetFaceUVs(FaceId faceId, System.Collections.Generic.IReadOnlyList<Vector2> uv0)
    {
        if (!faceId.IsValid)
            return;

        ref var face = ref Faces[faceId.Value];
        if (face.AnyLoop < 0 || face.LoopCount <= 0)
            return;

        int loopId = face.AnyLoop;
        int max = Mathf.Min(face.LoopCount, uv0.Count);
        for (int i = 0; i < max; i++)
        {
            ref var loop = ref Loops[loopId];
            loop.UV0 = uv0[i];
            loopId = loop.Next;
            if (loopId < 0)
                break;
        }
    }

    public static EditableMesh FromUnityMesh(Mesh source)
    {
        if (source == null)
            return null;

        var editable = new EditableMesh();
        var vertices = source.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            editable.AddVert(vertices[i]);
        }

        var uv0 = source.uv;
        bool hasUv0 = uv0 != null && uv0.Length == vertices.Length;

        int submeshCount = source.subMeshCount;
        for (int submesh = 0; submesh < submeshCount; submesh++)
        {
            var triangles = source.GetTriangles(submesh);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                var faceVerts = new[]
                {
                    new VertId(triangles[i]),
                    new VertId(triangles[i + 1]),
                    new VertId(triangles[i + 2])
                };

                Vector2[] faceUv = null;
                if (hasUv0)
                {
                    faceUv = new[]
                    {
                        uv0[triangles[i]],
                        uv0[triangles[i + 1]],
                        uv0[triangles[i + 2]]
                    };
                }

                editable.AddFace(faceVerts, faceUv, submesh);
            }
        }

        editable.RebuildCaches();
        return editable;
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

    public Mesh BakeToUnityMesh()
    {
        var mesh = new Mesh();
        var vertices = new System.Collections.Generic.List<Vector3>();
        var uv0 = new System.Collections.Generic.List<Vector2>();
        var vertexMap = new System.Collections.Generic.Dictionary<RenderVertexKey, int>();
        var submeshTriangles = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();

        for (int f = 0; f < Faces.Capacity; f++)
        {
            if (!Faces.IsAlive(f)) continue;
            var face = Faces[f];
            if (face.AnyLoop < 0 || face.LoopCount < 3) continue;

            int materialIndex = face.MaterialIndex;
            if (!submeshTriangles.TryGetValue(materialIndex, out var triangles))
            {
                triangles = new System.Collections.Generic.List<int>();
                submeshTriangles.Add(materialIndex, triangles);
            }

            var loopIds = new int[face.LoopCount];
            int loopId = face.AnyLoop;
            for (int i = 0; i < face.LoopCount; i++)
            {
                loopIds[i] = loopId;
                loopId = Loops[loopId].Next;
                if (loopId < 0) break;
            }

            int loopCount = loopIds.Length;
            if (loopCount == 3)
            {
                AddTriangle(loopIds[0], loopIds[1], loopIds[2], materialIndex, triangles, vertexMap, vertices, uv0);
            }
            else if (loopCount == 4)
            {
                AddTriangle(loopIds[0], loopIds[1], loopIds[2], materialIndex, triangles, vertexMap, vertices, uv0);
                AddTriangle(loopIds[0], loopIds[2], loopIds[3], materialIndex, triangles, vertexMap, vertices, uv0);
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv0);

        if (submeshTriangles.Count > 0)
        {
            var materialKeys = new System.Collections.Generic.List<int>(submeshTriangles.Keys);
            materialKeys.Sort();
            mesh.subMeshCount = materialKeys.Count;
            for (int i = 0; i < materialKeys.Count; i++)
            {
                mesh.SetTriangles(submeshTriangles[materialKeys[i]], i);
            }
        }

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void AddTriangle(
        int loopA,
        int loopB,
        int loopC,
        int materialIndex,
        System.Collections.Generic.List<int> triangles,
        System.Collections.Generic.Dictionary<RenderVertexKey, int> vertexMap,
        System.Collections.Generic.List<Vector3> vertices,
        System.Collections.Generic.List<Vector2> uv0)
    {
        triangles.Add(GetRenderVertexIndex(loopA, materialIndex, vertexMap, vertices, uv0));
        triangles.Add(GetRenderVertexIndex(loopB, materialIndex, vertexMap, vertices, uv0));
        triangles.Add(GetRenderVertexIndex(loopC, materialIndex, vertexMap, vertices, uv0));
    }

    private int GetRenderVertexIndex(
        int loopId,
        int materialIndex,
        System.Collections.Generic.Dictionary<RenderVertexKey, int> vertexMap,
        System.Collections.Generic.List<Vector3> vertices,
        System.Collections.Generic.List<Vector2> uv0)
    {
        var loop = Loops[loopId];
        var key = new RenderVertexKey(loop.Vert.Value, loop.UV0, materialIndex);
        if (vertexMap.TryGetValue(key, out int existing))
            return existing;

        var position = Verts[loop.Vert.Value].Position;
        int index = vertices.Count;
        vertices.Add(position);
        uv0.Add(loop.UV0);
        vertexMap.Add(key, index);
        return index;
    }

    private readonly struct RenderVertexKey : IEquatable<RenderVertexKey>
    {
        public RenderVertexKey(int vertId, Vector2 uv0, int materialIndex)
        {
            VertId = vertId;
            Uv0 = uv0;
            MaterialIndex = materialIndex;
        }

        public int VertId { get; }
        public Vector2 Uv0 { get; }
        public int MaterialIndex { get; }

        public bool Equals(RenderVertexKey other)
        {
            return VertId == other.VertId && Uv0.Equals(other.Uv0) && MaterialIndex == other.MaterialIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is RenderVertexKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(VertId, Uv0, MaterialIndex);
        }
    }
}

}
