using UnityEngine;

namespace MeshEditTools
{
    public class EditableMeshComponent : MonoBehaviour
    {
        [SerializeField] private EditableMesh editableMesh = new();
        [SerializeField] private MeshFilter targetMeshFilter;
        [SerializeField] private MeshSelectionMode selectionMode = MeshSelectionMode.Vertex;

        public EditableMesh Mesh => editableMesh;
        public MeshSelectionMode SelectionMode => selectionMode;

        public void BakeToUnityMesh()
        {
            if (targetMeshFilter == null)
            {
                targetMeshFilter = GetComponent<MeshFilter>();
            }

            if (targetMeshFilter == null)
            {
                Debug.LogWarning("EditableMeshComponent requires a MeshFilter to bake into.", this);
                return;
            }

            Mesh unityMesh = editableMesh.BakeToUnityMesh();
            unityMesh.name = name + "_Baked";
            targetMeshFilter.sharedMesh = unityMesh;
        }

        public void ConvertFromUnityMesh()
        {
            if (targetMeshFilter == null)
            {
                targetMeshFilter = GetComponent<MeshFilter>();
            }

            if (targetMeshFilter == null || targetMeshFilter.sharedMesh == null)
            {
                Debug.LogWarning("EditableMeshComponent requires a MeshFilter with a mesh to convert.", this);
                return;
            }

            editableMesh = EditableMesh.FromUnityMesh(targetMeshFilter.sharedMesh);
        }
    }

    public enum MeshSelectionMode
    {
        Vertex,
        Edge,
        Face
    }
}
