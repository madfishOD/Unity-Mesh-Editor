using UnityEngine;

namespace MeshEditTools
{
    /// <summary>
    /// MonoBehaviour wrapper that exposes an editable mesh and conversion helpers.
    /// </summary>
    public class EditableMeshComponent : MonoBehaviour
    {
        [SerializeField] private EditableMesh editableMesh = new();
        [SerializeField] private MeshFilter targetMeshFilter;
        [SerializeField] private MeshSelectionMode selectionMode = MeshSelectionMode.Vertex;

        /// <summary>
        /// Gets the editable mesh instance.
        /// </summary>
        public EditableMesh Mesh => editableMesh;
        /// <summary>
        /// Gets the current editor selection mode.
        /// </summary>
        public MeshSelectionMode SelectionMode => selectionMode;

        /// <summary>
        /// Bakes the editable mesh into a Unity Mesh on the target mesh filter.
        /// </summary>
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

        /// <summary>
        /// Converts the target mesh filter's Unity Mesh into the editable mesh format.
        /// </summary>
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

            editableMesh ??= new EditableMesh();
            editableMesh.LoadFromUnityMesh(targetMeshFilter.sharedMesh);
        }
    }

    /// <summary>
    /// Determines which mesh element type is selectable in the editor.
    /// </summary>
    public enum MeshSelectionMode
    {
        Vertex,
        Edge,
        Face
    }
}
