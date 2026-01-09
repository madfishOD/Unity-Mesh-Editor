using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MeshEditTools.Editor
{
    /// <summary>
    /// Manages the active editable mesh session without requiring a scene component.
    /// </summary>
    [InitializeOnLoad]
    public static class EditableMeshSession
    {
        private static readonly Dictionary<int, EditableMeshSessionData> MeshCache = new();
        private static MeshFilter activeMeshFilter;
        private static EditableMeshSessionData activeData;
        private static bool suppressSelectionChange;

        static EditableMeshSession()
        {
            Selection.selectionChanged += OnSelectionChanged;
        }

        public static bool EditModeEnabled { get; private set; }

        public static MeshSelectionMode SelectionMode { get; private set; } = MeshSelectionMode.Vertex;

        public static MeshFilter ActiveMeshFilter => activeMeshFilter;

        public static EditableMeshSessionData ActiveData => activeData;

        public static void SetEditMode(bool enabled)
        {
            if (EditModeEnabled == enabled)
                return;

            EditModeEnabled = enabled;
            if (enabled)
            {
                SetActiveFromSelection();
            }
            else
            {
                activeMeshFilter = null;
                activeData = null;
            }

            SceneView.RepaintAll();
        }

        public static void SetSelectionMode(MeshSelectionMode mode)
        {
            if (SelectionMode == mode)
                return;

            SelectionMode = mode;
            SceneView.RepaintAll();
        }

        public static void ConvertFromMeshFilter()
        {
            if (activeMeshFilter == null || activeData == null || activeMeshFilter.sharedMesh == null)
                return;

            Undo.RecordObject(activeData, "Convert From Mesh");
            activeData.EditableMesh = EditableMesh.FromUnityMesh(activeMeshFilter.sharedMesh);
            EditorUtility.SetDirty(activeData);
            SceneView.RepaintAll();
        }

        public static void BakeToMeshFilter()
        {
            if (activeMeshFilter == null || activeData == null || activeData.EditableMesh == null)
                return;

            Undo.RecordObject(activeMeshFilter, "Bake To Mesh");
            Mesh unityMesh = activeData.EditableMesh.BakeToUnityMesh();
            unityMesh.name = activeMeshFilter.sharedMesh != null
                ? $"{activeMeshFilter.sharedMesh.name}_Baked"
                : $"{activeMeshFilter.name}_Baked";
            activeMeshFilter.sharedMesh = unityMesh;
            EditorUtility.SetDirty(activeMeshFilter);
        }

        private static void OnSelectionChanged()
        {
            if (!EditModeEnabled)
                return;

            if (suppressSelectionChange)
                return;

            if (activeMeshFilter != null && Selection.activeGameObject != activeMeshFilter.gameObject)
            {
                suppressSelectionChange = true;
                Selection.activeGameObject = activeMeshFilter.gameObject;
                suppressSelectionChange = false;
                return;
            }

            SetActiveFromSelection();
        }

        private static void SetActiveFromSelection()
        {
            MeshFilter selected = GetSelectedMeshFilter();
            if (selected == activeMeshFilter)
                return;

            activeMeshFilter = selected;
            activeData = null;

            if (selected == null)
                return;

            activeData = GetOrCreateData(selected);
        }

        private static MeshFilter GetSelectedMeshFilter()
        {
            if (Selection.activeGameObject == null)
                return null;

            return Selection.activeGameObject.GetComponent<MeshFilter>();
        }

        private static EditableMeshSessionData GetOrCreateData(MeshFilter filter)
        {
            int id = filter.GetInstanceID();
            if (MeshCache.TryGetValue(id, out var data) && data != null)
                return data;

            data = ScriptableObject.CreateInstance<EditableMeshSessionData>();
            data.hideFlags = HideFlags.HideAndDontSave;
            MeshCache[id] = data;

            if (filter.sharedMesh != null)
            {
                data.EditableMesh = EditableMesh.FromUnityMesh(filter.sharedMesh);
            }

            return data;
        }
    }

    /// <summary>
    /// Scriptable storage for editable mesh data used by the editor session.
    /// </summary>
    public class EditableMeshSessionData : ScriptableObject
    {
        [SerializeField] private EditableMesh editableMesh;

        public EditableMesh EditableMesh
        {
            get => editableMesh;
            set => editableMesh = value;
        }
    }
}
