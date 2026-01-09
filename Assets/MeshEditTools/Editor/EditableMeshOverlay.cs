using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace MeshEditTools.Editor
{
    /// <summary>
    /// Scene view overlay for toggling mesh edit mode and tools.
    /// </summary>
    [Overlay(typeof(SceneView), "Mesh Edit")]
    public class EditableMeshOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.Add(new IMGUIContainer(DrawOverlayGui));
            return root;
        }

        private static void DrawOverlayGui()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                bool editMode = EditableMeshSession.EditModeEnabled;
                EditorGUI.BeginChangeCheck();
                editMode = EditorGUILayout.ToggleLeft("Edit Mode", editMode);
                if (EditorGUI.EndChangeCheck())
                {
                    EditableMeshSession.SetEditMode(editMode);
                }

                if (!EditableMeshSession.EditModeEnabled)
                    return;

                var meshFilter = EditableMeshSession.ActiveMeshFilter;
                var sessionData = EditableMeshSession.ActiveData;

                if (meshFilter == null)
                {
                    EditorGUILayout.HelpBox("Select a GameObject with a MeshFilter to edit.", MessageType.Info);
                    return;
                }

                if (meshFilter.sharedMesh == null)
                {
                    EditorGUILayout.HelpBox("The selected MeshFilter has no mesh assigned.", MessageType.Warning);
                    return;
                }

                EditorGUILayout.LabelField("Target", meshFilter.name);

                using (new EditorGUI.DisabledScope(sessionData == null || sessionData.EditableMesh == null))
                {
                    var modes = new[] { "Vertex", "Edge", "Face" };
                    EditorGUI.BeginChangeCheck();
                    int modeIndex = GUILayout.Toolbar((int)EditableMeshSession.SelectionMode, modes);
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditableMeshSession.SetSelectionMode((MeshSelectionMode)modeIndex);
                    }

                    EditorGUILayout.Space(2f);
                    var toolLabels = new[] { "Select Tool", "Move Tool", "Rotate Tool", "Scale Tool" };
                    int toolIndex = Tools.current switch
                    {
                        Tool.Move => 1,
                        Tool.Rotate => 2,
                        Tool.Scale => 3,
                        _ => 0
                    };
                    EditorGUI.BeginChangeCheck();
                    toolIndex = GUILayout.Toolbar(toolIndex, toolLabels);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Tools.current = toolIndex switch
                        {
                            1 => Tool.Move,
                            2 => Tool.Rotate,
                            3 => Tool.Scale,
                            _ => Tool.None
                        };
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Convert From Mesh"))
                    {
                        EditableMeshSession.ConvertFromMeshFilter();
                    }

                    if (GUILayout.Button("Bake To Mesh"))
                    {
                        EditableMeshSession.BakeToMeshFilter();
                    }
                }
            }
        }
    }
}
