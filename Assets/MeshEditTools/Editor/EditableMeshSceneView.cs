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
        private const float DragSelectThreshold = 6f;
        private static readonly Color DragSelectionFillColor = new(0.2f, 0.6f, 0.9f, 0.12f);
        private static readonly Color DragSelectionOutlineColor = new(0.2f, 0.6f, 0.9f, 0.9f);
        private static bool isDragSelecting;
        private static Vector2 dragStart;
        private static Rect dragRect;

        /// <summary>
        /// Registers the scene GUI callback when the editor loads.
        /// </summary>
        static EditableMeshSceneView()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        /// <summary>
        /// Handles scene GUI events for the active editable mesh session.
        /// </summary>
        private static void OnSceneGui(SceneView sceneView)
        {
            if (!EditableMeshSession.EditModeEnabled)
                return;

            var data = EditableMeshSession.ActiveData;
            var filter = EditableMeshSession.ActiveMeshFilter;
            if (data == null || data.EditableMesh == null || filter == null)
                return;

            HandleDragSelection(data, filter.transform, EditableMeshSession.SelectionMode);
            DrawEditableMesh(data, filter.transform, EditableMeshSession.SelectionMode);
        }

        /// <summary>
        /// Draws the mesh elements for a single component.
        /// </summary>
        private static void DrawEditableMesh(EditableMeshSessionData data, Transform targetTransform, MeshSelectionMode selectionMode)
        {
            if (data == null || data.EditableMesh == null || targetTransform == null)
                return;

            var mesh = data.EditableMesh;

            Handles.matrix = targetTransform.localToWorldMatrix;
            DrawFaces(data, mesh, selectionMode);
            DrawEdges(data, mesh, selectionMode);
            DrawVertices(data, mesh, selectionMode);
            DrawMoveHandle(data, mesh, selectionMode);
            Handles.matrix = Matrix4x4.identity;
        }

        /// <summary>
        /// Handles drag rectangle selection in the scene view.
        /// </summary>
        private static void HandleDragSelection(EditableMeshSessionData data, Transform targetTransform, MeshSelectionMode selectionMode)
        {
            if (data == null || data.EditableMesh == null || targetTransform == null)
                return;

            Event currentEvent = Event.current;
            if (currentEvent.alt || Tools.current != Tool.None)
                return;

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                dragStart = currentEvent.mousePosition;
            }

            if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
            {
                if (!isDragSelecting)
                {
                    if ((currentEvent.mousePosition - dragStart).sqrMagnitude >= DragSelectThreshold * DragSelectThreshold)
                    {
                        isDragSelecting = true;
                        GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                    }
                }

                if (isDragSelecting)
                {
                    dragRect = GetDragRect(dragStart, currentEvent.mousePosition);
                    currentEvent.Use();
                    SceneView.RepaintAll();
                }
            }

            if (isDragSelecting && currentEvent.type == EventType.MouseUp && currentEvent.button == 0)
            {
                ApplyDragSelection(data, data.EditableMesh, selectionMode, targetTransform, dragRect, currentEvent.shift);
                isDragSelecting = false;
                GUIUtility.hotControl = 0;
                currentEvent.Use();
                SceneView.RepaintAll();
            }

            if (isDragSelecting && currentEvent.type == EventType.Repaint)
            {
                DrawDragSelectionRect(dragRect);
            }
        }

        private static Rect GetDragRect(Vector2 start, Vector2 end)
        {
            float xMin = Mathf.Min(start.x, end.x);
            float yMin = Mathf.Min(start.y, end.y);
            float xMax = Mathf.Max(start.x, end.x);
            float yMax = Mathf.Max(start.y, end.y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static void DrawDragSelectionRect(Rect rect)
        {
            Handles.BeginGUI();
            EditorGUI.DrawRect(rect, DragSelectionFillColor);
            Handles.color = DragSelectionOutlineColor;
            Handles.DrawAAPolyLine(2f, new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin),
                new Vector3(rect.xMax, rect.yMax), new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMin, rect.yMin));
            Handles.EndGUI();
        }

        private static void ApplyDragSelection(EditableMeshSessionData data, EditableMesh mesh, MeshSelectionMode selectionMode,
            Transform targetTransform, Rect rect, bool toggle)
        {
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            Undo.RecordObject(data, "Drag Select Mesh Elements");
            if (!toggle)
            {
                ClearSelection(mesh);
            }

            switch (selectionMode)
            {
                case MeshSelectionMode.Vertex:
                    for (int v = 0; v < mesh.Verts.Capacity; v++)
                    {
                        if (!mesh.Verts.IsAlive(v))
                            continue;

                        Vector3 world = targetTransform.TransformPoint(mesh.Verts[v].Position);
                        Vector2 guiPoint = HandleUtility.WorldToGUIPoint(world);
                        if (!rect.Contains(guiPoint))
                            continue;

                        ref var vert = ref mesh.Verts[v];
                        vert.Flags = SetSelectedFlag(vert.Flags, toggle ? !IsSelected(vert.Flags) : true);
                    }
                    break;
                case MeshSelectionMode.Edge:
                    for (int e = 0; e < mesh.Edges.Capacity; e++)
                    {
                        if (!mesh.Edges.IsAlive(e))
                            continue;

                        ref var edge = ref mesh.Edges[e];
                        Vector3 v0 = mesh.Verts[edge.V0.Value].Position;
                        Vector3 v1 = mesh.Verts[edge.V1.Value].Position;
                        Vector3 center = (v0 + v1) * 0.5f;
                        Vector3 world = targetTransform.TransformPoint(center);
                        Vector2 guiPoint = HandleUtility.WorldToGUIPoint(world);
                        if (!rect.Contains(guiPoint))
                            continue;

                        edge.Flags = SetSelectedFlag(edge.Flags, toggle ? !IsSelected(edge.Flags) : true);
                    }
                    break;
                case MeshSelectionMode.Face:
                    for (int f = 0; f < mesh.Faces.Capacity; f++)
                    {
                        if (!mesh.Faces.IsAlive(f))
                            continue;

                        ref var face = ref mesh.Faces[f];
                        if (face.AnyLoop < 0 || face.LoopCount < 3)
                            continue;

                        Vector3 center = Vector3.zero;
                        int loopId = face.AnyLoop;
                        for (int i = 0; i < face.LoopCount; i++)
                        {
                            var loop = mesh.Loops[loopId];
                            center += mesh.Verts[loop.Vert.Value].Position;
                            loopId = loop.Next;
                            if (loopId < 0)
                                break;
                        }

                        center /= face.LoopCount;
                        Vector3 world = targetTransform.TransformPoint(center);
                        Vector2 guiPoint = HandleUtility.WorldToGUIPoint(world);
                        if (!rect.Contains(guiPoint))
                            continue;

                        face.Flags = SetSelectedFlag(face.Flags, toggle ? !IsSelected(face.Flags) : true);
                    }
                    break;
            }

            EditorUtility.SetDirty(data);
        }

        /// <summary>
        /// Draws vertex handles and selection buttons.
        /// </summary>
        private static void DrawVertices(EditableMeshSessionData data, EditableMesh mesh, MeshSelectionMode selectionMode)
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

                if (selectionMode == MeshSelectionMode.Vertex &&
                    Handles.Button(vert.Position, Quaternion.identity, size, size, Handles.DotHandleCap))
                {
                    ApplySelection(data, mesh, MeshSelectionMode.Vertex, v);
                }
            }
        }

        /// <summary>
        /// Draws edge lines and selection handles.
        /// </summary>
        private static void DrawEdges(EditableMeshSessionData data, EditableMesh mesh, MeshSelectionMode selectionMode)
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

                if (selectionMode == MeshSelectionMode.Edge)
                {
                    Vector3 center = (v0.Position + v1.Position) * 0.5f;
                    float size = HandleUtility.GetHandleSize(center) * EdgePickScale;
                    if (Handles.Button(center, Quaternion.identity, size, size, Handles.DotHandleCap))
                    {
                        ApplySelection(data, mesh, MeshSelectionMode.Edge, e);
                    }
                }
            }
        }

        /// <summary>
        /// Draws face polygons and selection handles.
        /// </summary>
        private static void DrawFaces(EditableMeshSessionData data, EditableMesh mesh, MeshSelectionMode selectionMode)
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

                if (selectionMode == MeshSelectionMode.Face)
                {
                    Vector3 center = Vector3.zero;
                    for (int i = 0; i < positions.Length; i++)
                        center += positions[i];
                    center /= positions.Length;

                    float size = HandleUtility.GetHandleSize(center) * FacePickScale;
                    if (Handles.Button(center, Quaternion.identity, size, size, Handles.RectangleHandleCap))
                    {
                        ApplySelection(data, mesh, MeshSelectionMode.Face, f);
                    }
                }
            }
        }

        /// <summary>
        /// Draws a move handle for the current selection and applies position deltas.
        /// </summary>
        private static void DrawMoveHandle(EditableMeshSessionData data, EditableMesh mesh, MeshSelectionMode selectionMode)
        {
            if (Tools.current != Tool.Move)
                return;

            var selectedVerts = CollectSelectedVertices(selectionMode, mesh);
            if (selectedVerts.Count == 0)
                return;

            Vector3 centroid = Vector3.zero;
            foreach (int vertId in selectedVerts)
            {
                centroid += mesh.Verts[vertId].Position;
            }
            centroid /= selectedVerts.Count;

            var movedVerts = new HashSet<int>(selectedVerts);
            if (selectionMode == MeshSelectionMode.Vertex || selectionMode == MeshSelectionMode.Face)
            {
                foreach (int coincident in CollectCoincidentVertices(mesh, selectedVerts))
                {
                    movedVerts.Add(coincident);
                }
            }

            EditorGUI.BeginChangeCheck();
            Vector3 newPosition = Handles.PositionHandle(centroid, Quaternion.identity);
            if (!EditorGUI.EndChangeCheck())
                return;

            Vector3 delta = newPosition - centroid;
            if (delta.sqrMagnitude <= Mathf.Epsilon)
                return;

            Undo.RecordObject(data, "Move Mesh Elements");
            foreach (int vertId in movedVerts)
            {
                ref var vert = ref mesh.Verts[vertId];
                vert.Position += delta;
            }

            EditorUtility.SetDirty(data);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Collects the vertex ids affected by the current selection mode.
        /// </summary>
        private static HashSet<int> CollectSelectedVertices(MeshSelectionMode selectionMode, EditableMesh mesh)
        {
            var selected = new HashSet<int>();
            switch (selectionMode)
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

        private static HashSet<int> CollectCoincidentVertices(EditableMesh mesh, HashSet<int> selectedVerts)
        {
            const float epsilon = 1e-6f;
            float epsilonSqr = epsilon * epsilon;
            var coincident = new HashSet<int>();

            foreach (int selectedVertId in selectedVerts)
            {
                Vector3 selectedPosition = mesh.Verts[selectedVertId].Position;
                for (int v = 0; v < mesh.Verts.Capacity; v++)
                {
                    if (!mesh.Verts.IsAlive(v) || selectedVerts.Contains(v))
                        continue;

                    Vector3 delta = mesh.Verts[v].Position - selectedPosition;
                    if (delta.sqrMagnitude <= epsilonSqr)
                    {
                        coincident.Add(v);
                    }
                }
            }

            return coincident;
        }

        /// <summary>
        /// Applies selection changes for a mesh element.
        /// </summary>
        private static void ApplySelection(EditableMeshSessionData data, EditableMesh mesh, MeshSelectionMode mode, int id)
        {
            bool toggle = Event.current.shift;
            Undo.RecordObject(data, "Select Mesh Element");

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

            EditorUtility.SetDirty(data);
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
