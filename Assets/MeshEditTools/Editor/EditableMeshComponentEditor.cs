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
                if (GUILayout.Button("Bake To Mesh", GUILayout.Width(140)))
                {
                    component.BakeToUnityMesh();
                }
            }
        }
    }
}
