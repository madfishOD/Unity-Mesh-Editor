using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MeshEditTools.Editor
{
    /// <summary>
    /// Scene view overlay for rendering and selecting editable mesh elements.
    /// </summary>
    [InitializeOnLoad]
    public static class EditableMeshSceneView
    {
        private const float VertexSizeScale = 0.04f;
        private const float EdgePickScale = 0.08f;
        private const float FacePickScale = 0.12f;
        private static readonly Color EdgeColor = new(0.6f, 0.6f, 0.6f, 0.9f);
        private static readonly Color EdgeSelectedColor = new(1f, 0.8f, 0.2f, 1f);
        private static readonly Color VertexColor = new(0.2f, 0.9f, 1f, 0.9f);
        private static readonly Color VertexSelectedColor = new(1f, 0.4f, 0.1f, 1f);
        private static readonly Color FaceColor = new(0.2f, 0.6f, 0.9f, 0.08f);
        private static readonly Color FaceSelectedColor = new(1f, 0.6f, 0.1f, 0.2f);
        private static readonly Color FaceOutlineColor = new(0.2f, 0.6f, 0.9f, 0.4f);
        private static readonly Color FaceOutlineSelectedColor = new(1f, 0.6f, 0.1f, 0.8f);

        /// <summary>
        /// Registers the scene GUI callback when the editor loads.
        /// </summary>
        static EditableMeshSceneView()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        /// <summary>
        /// Handles scene GUI events for selected editable mesh components.
        /// </summary>
        private static void OnSceneGui(SceneView sceneView)
        {
            var targets = Selection.GetFiltered<EditableMeshComponent>(SelectionMode.TopLevel);
            if (targets.Length == 0)
                return;

            foreach (var target in targets)
            {
                DrawEditableMesh(target);
            }
        }

        /// <summary>
        /// Draws the mesh elements for a single component.
        /// </summary>
        private static void DrawEditableMesh(EditableMeshComponent component)
        {
            if (component == null || component.Mesh == null)
                return;

            var mesh = component.Mesh;
            var transform = component.transform;

            Handles.matrix = transform.localToWorldMatrix;
            DrawFaces(component, mesh);
            DrawEdges(component, mesh);
            DrawVertices(component, mesh);
            DrawMoveHandle(component, mesh);
            Handles.matrix = Matrix4x4.identity;
        }

        /// <summary>
        /// Draws vertex handles and selection buttons.
        /// </summary>
        private static void DrawVertices(EditableMeshComponent component, EditableMesh mesh)
        {
            for (int v = 0; v < mesh.Verts.Capacity; v++)
            {
                if (!mesh.Verts.IsAlive(v))
                    continue;

                ref var vert = ref mesh.Verts[v];
                bool selected = IsSelected(vert.Flags);
                Handles.color = selected ? VertexSelectedColor : VertexColor;

                float size = HandleUtility.GetHandleSize(vert.Position) * VertexSizeScale;
                if (Event.current.type == EventType.Repaint)
                {
                    Handles.DotHandleCap(0, vert.Position, Quaternion.identity, size, EventType.Repaint);
                }

                if (component.SelectionMode == MeshSelectionMode.Vertex &&
                    Handles.Button(vert.Position, Quaternion.identity, size, size, Handles.DotHandleCap))
                {
                    ApplySelection(component, mesh, MeshSelectionMode.Vertex, v);
                }
            }
        }

        /// <summary>
        /// Draws edge lines and selection handles.
        /// </summary>
        private static void DrawEdges(EditableMeshComponent component, EditableMesh mesh)
        {
            for (int e = 0; e < mesh.Edges.Capacity; e++)
            {
                if (!mesh.Edges.IsAlive(e))
                    continue;

                ref var edge = ref mesh.Edges[e];
                var v0 = mesh.Verts[edge.V0.Value];
                var v1 = mesh.Verts[edge.V1.Value];
                bool selected = IsSelected(edge.Flags);
                Handles.color = selected ? EdgeSelectedColor : EdgeColor;
                if (Event.current.type == EventType.Repaint)
                {
                    Handles.DrawLine(v0.Position, v1.Position);
                }

                if (component.SelectionMode == MeshSelectionMode.Edge)
                {
                    Vector3 center = (v0.Position + v1.Position) * 0.5f;
                    float size = HandleUtility.GetHandleSize(center) * EdgePickScale;
                    if (Handles.Button(center, Quaternion.identity, size, size, Handles.DotHandleCap))
                    {
                        ApplySelection(component, mesh, MeshSelectionMode.Edge, e);
                    }
                }
            }
        }

        /// <summary>
        /// Draws face polygons and selection handles.
        /// </summary>
        private static void DrawFaces(EditableMeshComponent component, EditableMesh mesh)
        {
            for (int f = 0; f < mesh.Faces.Capacity; f++)
            {
                if (!mesh.Faces.IsAlive(f))
                    continue;

                ref var face = ref mesh.Faces[f];
                if (face.AnyLoop < 0 || face.LoopCount < 3)
                    continue;

                var positions = new Vector3[face.LoopCount];
                int loopId = face.AnyLoop;
                for (int i = 0; i < face.LoopCount; i++)
                {
                    var loop = mesh.Loops[loopId];
                    positions[i] = mesh.Verts[loop.Vert.Value].Position;
                    loopId = loop.Next;
                    if (loopId < 0)
                        break;
                }

                bool selected = IsSelected(face.Flags);
                Handles.color = selected ? FaceSelectedColor : FaceColor;
                if (positions.Length >= 3 && Event.current.type == EventType.Repaint)
                {
                    Handles.DrawAAConvexPolygon(positions);
                    Handles.color = selected ? FaceOutlineSelectedColor : FaceOutlineColor;
                    Handles.DrawAAPolyLine(2f, positions);
                }

                if (component.SelectionMode == MeshSelectionMode.Face)
                {
                    Vector3 center = Vector3.zero;
                    for (int i = 0; i < positions.Length; i++)
                        center += positions[i];
                    center /= positions.Length;

                    float size = HandleUtility.GetHandleSize(center) * FacePickScale;
                    if (Handles.Button(center, Quaternion.identity, size, size, Handles.RectangleHandleCap))
                    {
                        ApplySelection(component, mesh, MeshSelectionMode.Face, f);
                    }
                }
            }
        }

        /// <summary>
        /// Draws a move handle for the current selection and applies position deltas.
        /// </summary>
        private static void DrawMoveHandle(EditableMeshComponent component, EditableMesh mesh)
        {
            if (Tools.current != Tool.Move)
                return;

            var selectedVerts = CollectSelectedVertices(component, mesh);
            if (selectedVerts.Count == 0)
                return;

            Vector3 centroid = Vector3.zero;
            foreach (int vertId in selectedVerts)
            {
                centroid += mesh.Verts[vertId].Position;
            }
            centroid /= selectedVerts.Count;

            EditorGUI.BeginChangeCheck();
            Vector3 newPosition = Handles.PositionHandle(centroid, Quaternion.identity);
            if (!EditorGUI.EndChangeCheck())
                return;

            Vector3 delta = newPosition - centroid;
            if (delta.sqrMagnitude <= Mathf.Epsilon)
                return;

            Undo.RecordObject(component, "Move Mesh Elements");
            foreach (int vertId in selectedVerts)
            {
                ref var vert = ref mesh.Verts[vertId];
                vert.Position += delta;
            }

            EditorUtility.SetDirty(component);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Collects the vertex ids affected by the current selection mode.
        /// </summary>
        private static HashSet<int> CollectSelectedVertices(EditableMeshComponent component, EditableMesh mesh)
        {
            var selected = new HashSet<int>();
            switch (component.SelectionMode)
            {
                case MeshSelectionMode.Vertex:
                    for (int v = 0; v < mesh.Verts.Capacity; v++)
                    {
                        if (!mesh.Verts.IsAlive(v))
                            continue;

                        if (IsSelected(mesh.Verts[v].Flags))
                        {
                            selected.Add(v);
                        }
                    }
                    break;
                case MeshSelectionMode.Edge:
                    for (int e = 0; e < mesh.Edges.Capacity; e++)
                    {
                        if (!mesh.Edges.IsAlive(e))
                            continue;

                        if (!IsSelected(mesh.Edges[e].Flags))
                            continue;

                        var edge = mesh.Edges[e];
                        selected.Add(edge.V0.Value);
                        selected.Add(edge.V1.Value);
                    }
                    break;
                case MeshSelectionMode.Face:
                    for (int f = 0; f < mesh.Faces.Capacity; f++)
                    {
                        if (!mesh.Faces.IsAlive(f))
                            continue;

                        ref var face = ref mesh.Faces[f];
                        if (!IsSelected(face.Flags) || face.AnyLoop < 0 || face.LoopCount < 1)
                            continue;

                        int loopId = face.AnyLoop;
                        for (int i = 0; i < face.LoopCount; i++)
                        {
                            var loop = mesh.Loops[loopId];
                            selected.Add(loop.Vert.Value);
                            loopId = loop.Next;
                            if (loopId < 0)
                                break;
                        }
                    }
                    break;
            }

            return selected;
        }

        /// <summary>
        /// Applies selection changes for a mesh element.
        /// </summary>
        private static void ApplySelection(EditableMeshComponent component, EditableMesh mesh, MeshSelectionMode mode, int id)
        {
            bool toggle = Event.current.shift;
            Undo.RecordObject(component, "Select Mesh Element");

            if (!toggle)
            {
                ClearSelection(mesh);
            }

            switch (mode)
            {
                case MeshSelectionMode.Vertex:
                    ref var vert = ref mesh.Verts[id];
                    vert.Flags = SetSelectedFlag(vert.Flags, toggle ? !IsSelected(vert.Flags) : true);
                    break;
                case MeshSelectionMode.Edge:
                    ref var edge = ref mesh.Edges[id];
                    edge.Flags = SetSelectedFlag(edge.Flags, toggle ? !IsSelected(edge.Flags) : true);
                    break;
                case MeshSelectionMode.Face:
                    ref var face = ref mesh.Faces[id];
                    face.Flags = SetSelectedFlag(face.Flags, toggle ? !IsSelected(face.Flags) : true);
                    break;
            }

            EditorUtility.SetDirty(component);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Clears selection flags on all mesh elements.
        /// </summary>
        private static void ClearSelection(EditableMesh mesh)
        {
            for (int v = 0; v < mesh.Verts.Capacity; v++)
            {
                if (!mesh.Verts.IsAlive(v))
                    continue;

                ref var vert = ref mesh.Verts[v];
                vert.Flags = SetSelectedFlag(vert.Flags, false);
            }

            for (int e = 0; e < mesh.Edges.Capacity; e++)
            {
                if (!mesh.Edges.IsAlive(e))
                    continue;

                ref var edge = ref mesh.Edges[e];
                edge.Flags = SetSelectedFlag(edge.Flags, false);
            }

            for (int f = 0; f < mesh.Faces.Capacity; f++)
            {
                if (!mesh.Faces.IsAlive(f))
                    continue;

                ref var face = ref mesh.Faces[f];
                face.Flags = SetSelectedFlag(face.Flags, false);
            }
        }

        /// <summary>
        /// Returns true if the selected flag is set.
        /// </summary>
        private static bool IsSelected(byte flags) => (flags & (byte)MeshElementFlags.Selected) != 0;

        /// <summary>
        /// Sets or clears the selected flag on a flag field.
        /// </summary>
        private static byte SetSelectedFlag(byte flags, bool selected)
        {
            if (selected)
            {
                return (byte)(flags | (byte)MeshElementFlags.Selected);
            }

            return (byte)(flags & ~(byte)MeshElementFlags.Selected);
        }
    }
}
