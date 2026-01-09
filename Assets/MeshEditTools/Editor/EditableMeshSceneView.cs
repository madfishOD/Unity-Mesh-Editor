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
        private const float VertexPickGuiRadius = 8f;
        private const float EdgePickGuiRadius = 10f;
        private const float FacePickGuiRadius = 12f;
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
        private static bool isRotating;
        private static Quaternion lastRotation = Quaternion.identity;
        private static bool isScaling;
        private static Vector3 lastScale = Vector3.one;
        private static readonly HashSet<int> CachedSelectedVerts = new();
        private static readonly HashSet<int> CachedMovedVerts = new();
        private static bool selectionCacheDirty = true;
        private static Vector3[] facePositionsBuffer = System.Array.Empty<Vector3>();
        private static MeshSelectionMode cachedSelectionMode = MeshSelectionMode.Vertex;
        private static Material vertexBillboardMaterial;

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
            HandleClickSelection(data, filter.transform, EditableMeshSession.SelectionMode);
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
            DrawTransformHandle(data, mesh, selectionMode);
            Handles.matrix = Matrix4x4.identity;
        }

        private static void HandleClickSelection(EditableMeshSessionData data, Transform targetTransform, MeshSelectionMode selectionMode)
        {
            if (data == null || data.EditableMesh == null || targetTransform == null)
                return;

            Event currentEvent = Event.current;
            if (currentEvent.alt || Tools.current != Tool.None)
                return;

            if (currentEvent.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                return;
            }

            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0 || isDragSelecting)
                return;

            int pickedId = FindPickedElementId(data.EditableMesh, selectionMode, targetTransform, currentEvent.mousePosition);
            if (pickedId < 0)
                return;

            ApplySelection(data, data.EditableMesh, selectionMode, pickedId);
            currentEvent.Use();
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
            MarkSelectionCacheDirty();
        }

        /// <summary>
        /// Draws vertex handles and selection buttons.
        /// </summary>
        private static void DrawVertices(EditableMeshSessionData data, EditableMesh mesh, MeshSelectionMode selectionMode)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var sceneView = SceneView.currentDrawingSceneView;
            var camera = sceneView != null ? sceneView.camera : null;
            if (camera == null)
                return;

            EnsureVertexBillboardMaterial();
            if (vertexBillboardMaterial == null)
                return;
            vertexBillboardMaterial.SetPass(0);

            Matrix4x4 localToWorld = Handles.matrix;
            Vector3 cameraRight = camera.transform.right;
            Vector3 cameraUp = camera.transform.up;

            GL.PushMatrix();
            GL.Begin(GL.QUADS);
            for (int v = 0; v < mesh.Verts.Capacity; v++)
            {
                if (!mesh.Verts.IsAlive(v))
                    continue;

                ref var vert = ref mesh.Verts[v];
                bool selected = IsSelected(vert.Flags);
                GL.Color(selected ? VertexSelectedColor : VertexColor);

                Vector3 worldPosition = localToWorld.MultiplyPoint3x4(vert.Position);
                float size = HandleUtility.GetHandleSize(worldPosition) * VertexSizeScale;
                Vector3 right = cameraRight * size;
                Vector3 up = cameraUp * size;

                GL.Vertex(worldPosition - right - up);
                GL.Vertex(worldPosition - right + up);
                GL.Vertex(worldPosition + right + up);
                GL.Vertex(worldPosition + right - up);
            }
            GL.End();
            GL.PopMatrix();
        }

        private static void EnsureVertexBillboardMaterial()
        {
            if (vertexBillboardMaterial != null)
                return;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                return;
            vertexBillboardMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            vertexBillboardMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            vertexBillboardMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            vertexBillboardMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            vertexBillboardMaterial.SetInt("_ZWrite", 0);
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

                Vector3[] positions = GetFacePositionsBuffer(face.LoopCount);
                int loopId = face.AnyLoop;
                for (int i = 0; i < face.LoopCount; i++)
                {
                    var loop = mesh.Loops[loopId];
                    positions[i] = mesh.Verts[loop.Vert.Value].Position;
                    loopId = loop.Next;
                    if (loopId < 0)
                        break;
                }

                Vector3 lastPosition = positions[face.LoopCount - 1];
                for (int i = face.LoopCount; i < positions.Length; i++)
                {
                    positions[i] = lastPosition;
                }

                bool selected = IsSelected(face.Flags);
                Handles.color = selected ? FaceSelectedColor : FaceColor;
                if (face.LoopCount >= 3 && Event.current.type == EventType.Repaint)
                {
                    Handles.DrawAAConvexPolygon(positions);
                    Handles.color = selected ? FaceOutlineSelectedColor : FaceOutlineColor;
                    Handles.DrawAAPolyLine(2f, positions);
                }
            }
        }

        /// <summary>
        /// Draws a move handle for the current selection and applies position deltas.
        /// </summary>
        private static void DrawTransformHandle(EditableMeshSessionData data, EditableMesh mesh, MeshSelectionMode selectionMode)
        {
            if (Tools.current != Tool.Rotate && isRotating)
            {
                isRotating = false;
                lastRotation = Quaternion.identity;
            }

            if (Tools.current != Tool.Scale && isScaling)
            {
                isScaling = false;
                lastScale = Vector3.one;
            }

            switch (Tools.current)
            {
                case Tool.Move:
                    DrawMoveHandle(data, mesh, selectionMode);
                    break;
                case Tool.Rotate:
                    DrawRotateHandle(data, mesh, selectionMode);
                    break;
                case Tool.Scale:
                    DrawScaleHandle(data, mesh, selectionMode);
                    break;
            }
        }

        /// <summary>
        /// Collects the vertex ids affected by the current selection mode.
        /// </summary>
        private static void CollectSelectedVertices(MeshSelectionMode selectionMode, EditableMesh mesh, HashSet<int> selected)
        {
            selected.Clear();
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

        }

        private static void CollectCoincidentVertices(EditableMesh mesh, HashSet<int> selectedVerts, HashSet<int> coincident)
        {
            const float epsilon = 1e-6f;
            float epsilonSqr = epsilon * epsilon;

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

        }

        private static bool TryGetSelectionData(EditableMesh mesh, MeshSelectionMode selectionMode, out Vector3 centroid,
            out HashSet<int> movedVerts)
        {
            if (selectionMode != cachedSelectionMode)
            {
                cachedSelectionMode = selectionMode;
                selectionCacheDirty = true;
            }

            if (selectionCacheDirty)
            {
                CollectSelectedVertices(selectionMode, mesh, CachedSelectedVerts);
                selectionCacheDirty = false;
            }

            CachedMovedVerts.Clear();
            CachedMovedVerts.UnionWith(CachedSelectedVerts);
            CollectCoincidentVertices(mesh, CachedSelectedVerts, CachedMovedVerts);

            if (CachedSelectedVerts.Count == 0)
            {
                centroid = Vector3.zero;
                movedVerts = CachedMovedVerts;
                return false;
            }

            centroid = Vector3.zero;
            foreach (int vertId in CachedSelectedVerts)
            {
                centroid += mesh.Verts[vertId].Position;
            }
            centroid /= CachedSelectedVerts.Count;
            movedVerts = CachedMovedVerts;

            return true;
        }

        /// <summary>
        /// Draws a move handle for the current selection and applies position deltas.
        /// </summary>
        private static void DrawMoveHandle(EditableMeshSessionData data, EditableMesh mesh, MeshSelectionMode selectionMode)
        {
            if (!TryGetSelectionData(mesh, selectionMode, out Vector3 centroid, out HashSet<int> movedVerts))
                return;

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
        /// Draws a rotation handle for the current selection and applies rotation deltas.
        /// </summary>
        private static void DrawRotateHandle(EditableMeshSessionData data, EditableMesh mesh, MeshSelectionMode selectionMode)
        {
            if (!TryGetSelectionData(mesh, selectionMode, out Vector3 centroid, out HashSet<int> movedVerts))
                return;

            EditorGUI.BeginChangeCheck();
            Quaternion newRotation = Handles.RotationHandle(Quaternion.identity, centroid);
            if (!EditorGUI.EndChangeCheck())
            {
                if (isRotating && GUIUtility.hotControl == 0)
                {
                    isRotating = false;
                    lastRotation = Quaternion.identity;
                }

                return;
            }

            if (Quaternion.Angle(Quaternion.identity, newRotation) <= Mathf.Epsilon)
                return;

            if (!isRotating)
            {
                isRotating = true;
                lastRotation = Quaternion.identity;
            }

            Quaternion deltaRotation = newRotation * Quaternion.Inverse(lastRotation);
            lastRotation = newRotation;

            Undo.RecordObject(data, "Rotate Mesh Elements");
            foreach (int vertId in movedVerts)
            {
                ref var vert = ref mesh.Verts[vertId];
                Vector3 offset = vert.Position - centroid;
                vert.Position = centroid + deltaRotation * offset;
            }

            EditorUtility.SetDirty(data);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Draws a scale handle for the current selection and applies scale deltas.
        /// </summary>
        private static void DrawScaleHandle(EditableMeshSessionData data, EditableMesh mesh, MeshSelectionMode selectionMode)
        {
            if (!TryGetSelectionData(mesh, selectionMode, out Vector3 centroid, out HashSet<int> movedVerts))
                return;

            float handleSize = HandleUtility.GetHandleSize(centroid);
            EditorGUI.BeginChangeCheck();
            Vector3 newScale = Handles.ScaleHandle(Vector3.one, centroid, Quaternion.identity, handleSize);
            if (!EditorGUI.EndChangeCheck())
            {
                if (isScaling && GUIUtility.hotControl == 0)
                {
                    isScaling = false;
                    lastScale = Vector3.one;
                }

                return;
            }

            if ((newScale - Vector3.one).sqrMagnitude <= Mathf.Epsilon)
                return;

            if (!isScaling)
            {
                isScaling = true;
                lastScale = Vector3.one;
            }

            Vector3 deltaScale = new Vector3(
                lastScale.x == 0f ? 1f : newScale.x / lastScale.x,
                lastScale.y == 0f ? 1f : newScale.y / lastScale.y,
                lastScale.z == 0f ? 1f : newScale.z / lastScale.z);
            lastScale = newScale;

            Undo.RecordObject(data, "Scale Mesh Elements");
            foreach (int vertId in movedVerts)
            {
                ref var vert = ref mesh.Verts[vertId];
                Vector3 offset = vert.Position - centroid;
                vert.Position = centroid + Vector3.Scale(offset, deltaScale);
            }

            EditorUtility.SetDirty(data);
            SceneView.RepaintAll();
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
            MarkSelectionCacheDirty();
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
            MarkSelectionCacheDirty();
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

        private static int FindPickedElementId(EditableMesh mesh, MeshSelectionMode selectionMode, Transform targetTransform, Vector2 mousePosition)
        {
            int bestId = -1;
            float bestSqrDistance = float.MaxValue;

            switch (selectionMode)
            {
                case MeshSelectionMode.Vertex:
                    for (int v = 0; v < mesh.Verts.Capacity; v++)
                    {
                        if (!mesh.Verts.IsAlive(v))
                            continue;

                        Vector3 world = targetTransform.TransformPoint(mesh.Verts[v].Position);
                        Vector2 guiPoint = HandleUtility.WorldToGUIPoint(world);
                        float sqrDistance = (guiPoint - mousePosition).sqrMagnitude;
                        if (sqrDistance <= VertexPickGuiRadius * VertexPickGuiRadius && sqrDistance < bestSqrDistance)
                        {
                            bestSqrDistance = sqrDistance;
                            bestId = v;
                        }
                    }
                    break;
                case MeshSelectionMode.Edge:
                    for (int e = 0; e < mesh.Edges.Capacity; e++)
                    {
                        if (!mesh.Edges.IsAlive(e))
                            continue;

                        ref var edge = ref mesh.Edges[e];
                        Vector3 v0World = targetTransform.TransformPoint(mesh.Verts[edge.V0.Value].Position);
                        Vector3 v1World = targetTransform.TransformPoint(mesh.Verts[edge.V1.Value].Position);
                        Vector2 v0Gui = HandleUtility.WorldToGUIPoint(v0World);
                        Vector2 v1Gui = HandleUtility.WorldToGUIPoint(v1World);
                        float sqrDistance = DistancePointToSegmentSqr(mousePosition, v0Gui, v1Gui);
                        if (sqrDistance <= EdgePickGuiRadius * EdgePickGuiRadius && sqrDistance < bestSqrDistance)
                        {
                            bestSqrDistance = sqrDistance;
                            bestId = e;
                        }
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
                        float sqrDistance = (guiPoint - mousePosition).sqrMagnitude;
                        if (sqrDistance <= FacePickGuiRadius * FacePickGuiRadius && sqrDistance < bestSqrDistance)
                        {
                            bestSqrDistance = sqrDistance;
                            bestId = f;
                        }
                    }
                    break;
            }

            return bestId;
        }

        private static float DistancePointToSegmentSqr(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float abSqr = ab.sqrMagnitude;
            if (abSqr <= Mathf.Epsilon)
                return (point - a).sqrMagnitude;

            float t = Vector2.Dot(point - a, ab) / abSqr;
            t = Mathf.Clamp01(t);
            Vector2 projection = a + t * ab;
            return (point - projection).sqrMagnitude;
        }

        private static void MarkSelectionCacheDirty()
        {
            selectionCacheDirty = true;
        }

        private static Vector3[] GetFacePositionsBuffer(int requiredCount)
        {
            if (facePositionsBuffer.Length < requiredCount)
            {
                int newSize = facePositionsBuffer.Length == 0 ? 4 : facePositionsBuffer.Length;
                while (newSize < requiredCount)
                {
                    newSize *= 2;
                }
                facePositionsBuffer = new Vector3[newSize];
            }

            return facePositionsBuffer;
        }
    }
}
