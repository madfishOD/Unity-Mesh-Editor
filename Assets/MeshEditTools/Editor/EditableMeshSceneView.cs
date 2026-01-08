using UnityEditor;
using UnityEngine;

namespace MeshEditTools.Editor
{
    [InitializeOnLoad]
    public static class EditableMeshSceneView
    {
        private const float VertexSizeScale = 0.04f;
        private static readonly Color EdgeColor = new(0.6f, 0.6f, 0.6f, 0.9f);
        private static readonly Color EdgeSelectedColor = new(1f, 0.8f, 0.2f, 1f);
        private static readonly Color VertexColor = new(0.2f, 0.9f, 1f, 0.9f);
        private static readonly Color VertexSelectedColor = new(1f, 0.4f, 0.1f, 1f);

        static EditableMeshSceneView()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

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

        private static void DrawEditableMesh(EditableMeshComponent component)
        {
            if (component == null || component.Mesh == null)
                return;

            var mesh = component.Mesh;
            var transform = component.transform;

            Handles.matrix = transform.localToWorldMatrix;
            DrawEdges(mesh);
            DrawVertices(mesh);
            Handles.matrix = Matrix4x4.identity;
        }

        private static void DrawVertices(EditableMesh mesh)
        {
            for (int v = 0; v < mesh.Verts.Capacity; v++)
            {
                if (!mesh.Verts.IsAlive(v))
                    continue;

                var vert = mesh.Verts[v];
                bool selected = (vert.Flags & (byte)MeshElementFlags.Selected) != 0;
                Handles.color = selected ? VertexSelectedColor : VertexColor;

                float size = HandleUtility.GetHandleSize(vert.Position) * VertexSizeScale;
                Handles.DotHandleCap(0, vert.Position, Quaternion.identity, size, EventType.Repaint);
            }
        }

        private static void DrawEdges(EditableMesh mesh)
        {
            for (int e = 0; e < mesh.Edges.Capacity; e++)
            {
                if (!mesh.Edges.IsAlive(e))
                    continue;

                var edge = mesh.Edges[e];
                var v0 = mesh.Verts[edge.V0.Value];
                var v1 = mesh.Verts[edge.V1.Value];
                bool selected = (edge.Flags & (byte)MeshElementFlags.Selected) != 0;
                Handles.color = selected ? EdgeSelectedColor : EdgeColor;
                Handles.DrawLine(v0.Position, v1.Position);
            }
        }
    }
}
