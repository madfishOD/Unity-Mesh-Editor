using UnityEditor;
using UnityEngine;

namespace MeshEditTools.Editor
{
    [CustomEditor(typeof(EditableMeshComponent))]
    public class EditableMeshComponentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var component = (EditableMeshComponent)target;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Convert From Mesh", GUILayout.Width(160)))
                {
                    Undo.RecordObject(component, "Convert From Mesh");
                    component.ConvertFromUnityMesh();
                    EditorUtility.SetDirty(component);
                }
                if (GUILayout.Button("Bake To Mesh", GUILayout.Width(140)))
                {
                    Undo.RecordObject(component, "Bake To Mesh");
                    component.BakeToUnityMesh();
                    EditorUtility.SetDirty(component);
                }
            }
        }
    }
}
