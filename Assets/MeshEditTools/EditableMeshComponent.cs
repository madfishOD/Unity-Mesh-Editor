using UnityEngine;

namespace MeshEditTools
{
    public class EditableMeshComponent : MonoBehaviour
    {
        [SerializeField] private EditableMesh editableMesh = new();
        [SerializeField] private MeshFilter targetMeshFilter;

        public EditableMesh Mesh => editableMesh;

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
    }
}
