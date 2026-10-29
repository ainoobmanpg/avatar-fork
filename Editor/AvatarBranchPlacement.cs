using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrcAvatarWorkspace.Branching
{
    sealed class BranchScenePlacement : IDisposable
    {
        Scene originalScene;
        string sceneGuid;
        readonly Vector3 position;
        readonly Quaternion rotation;
        readonly Vector3 scale;
        readonly bool active;
        GameObject placed;
        GlobalObjectId placedId;
        int instanceId;
        public bool HasPlaced { get; private set; }

        public BranchScenePlacement(GameObject source)
        {
            if (source == null) throw new ArgumentException(AvatarForkText.SelectHierarchy);
            originalScene = source.scene;
            sceneGuid = AssetDatabase.AssetPathToGUID(originalScene.path);
            position = source.transform.position;
            rotation = source.transform.rotation;
            scale = source.transform.lossyScale;
            active = source.activeSelf;
            var expected = source.transform.localToWorldMatrix;
            var actual = Matrix4x4.TRS(position, rotation, scale);
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(expected[i] - actual[i]) > .0001f * Mathf.Max(1, Mathf.Abs(expected[i])))
                    throw new InvalidOperationException(AvatarForkText.ParentShear);
            EditorApplication.hierarchyChanged += Track;
            EditorSceneManager.sceneSaved += SceneSaved;
            EditorSceneManager.sceneOpened += SceneOpened;
            EditorSceneManager.sceneClosing += SceneClosing;
        }

        public void Dispose()
        {
            EditorApplication.hierarchyChanged -= Track;
            EditorSceneManager.sceneSaved -= SceneSaved;
            EditorSceneManager.sceneOpened -= SceneOpened;
            EditorSceneManager.sceneClosing -= SceneClosing;
        }

        void SceneSaved(Scene scene) { Track(); }
        void SceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (!string.IsNullOrEmpty(sceneGuid) && AssetDatabase.AssetPathToGUID(scene.path) == sceneGuid) originalScene = scene;
        }
        void SceneClosing(Scene scene, bool removingScene) { Track(); }

        void Track()
        {
            if (originalScene.IsValid() && originalScene.isLoaded)
                sceneGuid = AssetDatabase.AssetPathToGUID(originalScene.path);
            if (placed == null) return;
            instanceId = placed.GetInstanceID();
            placedId = GlobalObjectId.GetGlobalObjectIdSlow(placed);
        }

        public GameObject Resolve()
        {
            if (placed == null && instanceId != 0) placed = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (placed == null && placedId.identifierType != 0)
                placed = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(placedId) as GameObject;
            Track();
            return placed;
        }

        public void Bind(GameObject instance, BranchReport report)
        {
            if (instance == null || !instance.scene.IsValid() || !instance.scene.isLoaded || EditorSceneManager.IsPreviewScene(instance.scene)
                || PrefabUtility.GetNearestPrefabInstanceRoot(instance) != instance
                || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance) != report.prefab)
                throw new InvalidOperationException(AvatarForkText.SelectPlacedCopy);
            placed = instance;
            HasPlaced = true;
            Track();
        }

        public GameObject Add(BranchReport report, bool createAnother = false)
        {
            var existing = Resolve();
            if (!createAnother && existing != null) return existing;
            if (!createAnother && HasPlaced) throw new InvalidOperationException(AvatarForkText.PlacementUnresolved);
            var scene = originalScene.IsValid() && originalScene.isLoaded ? originalScene : SceneManager.GetSceneByPath(AssetDatabase.GUIDToAssetPath(sceneGuid));
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
                throw new InvalidOperationException(AvatarForkText.PlacementSceneClosed);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(report.prefab);
            if (prefab == null) throw new IOException(AvatarForkText.OutputMissing);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                if (instance == null) throw new InvalidOperationException(AvatarForkText.OutputMissing);
                Undo.RegisterCreatedObjectUndo(instance, AvatarForkText.Create);
                Undo.RegisterFullObjectHierarchyUndo(instance, AvatarForkText.Create);
                instance.transform.SetPositionAndRotation(position, rotation);
                instance.transform.localScale = scale;
                instance.SetActive(active);
                instance.transform.SetAsLastSibling();
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                EditorSceneManager.MarkSceneDirty(scene);
                placed = instance;
                HasPlaced = true;
                Track();
                return instance;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                throw;
            }
            finally { Undo.CollapseUndoOperations(group); }
        }
    }

    public static partial class AvatarBrancher
    {
        internal static string FindCopyFolder(GameObject selected)
        {
            var root = AvatarRoot(selected);
            if (root == null) return null;
            string prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (string.IsNullOrEmpty(prefab)) return null;
            if (PrefabUtility.GetNearestPrefabInstanceRoot(root) != root) return null;
            string folder = FindBranchFolder(prefab);
            if (folder == null) return null;
            try
            {
                var report = JsonUtility.FromJson<BranchReport>(File.ReadAllText(folder + "/" + ReportName, Utf8));
                return report != null && report.version == 1 && report.status == "Complete" && report.folder == folder && report.prefab == prefab ? folder : null;
            }
            catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException) { return null; }
        }
    }
}
