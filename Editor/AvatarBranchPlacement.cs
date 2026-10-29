using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrcAvatarWorkspace.Branching
{
    sealed class BranchScenePlacement
    {
        readonly Scene originalScene;
        readonly string scenePath;
        readonly Vector3 position;
        readonly Quaternion rotation;
        readonly Vector3 scale;
        readonly bool active;

        public BranchScenePlacement(GameObject source)
        {
            if (source == null) throw new ArgumentException(AvatarForkText.SelectHierarchy);
            originalScene = source.scene;
            scenePath = originalScene.path;
            position = source.transform.position;
            rotation = source.transform.rotation;
            scale = source.transform.lossyScale;
            active = source.activeSelf;
            var expected = source.transform.localToWorldMatrix;
            var actual = Matrix4x4.TRS(position, rotation, scale);
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(expected[i] - actual[i]) > .0001f * Mathf.Max(1, Mathf.Abs(expected[i])))
                    throw new InvalidOperationException(AvatarForkText.ParentShear);
        }

        public GameObject Add(BranchReport report)
        {
            var scene = originalScene.IsValid() && originalScene.isLoaded ? originalScene : SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
                throw new InvalidOperationException(AvatarForkText.PlacementSceneClosed);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(report.prefab);
            if (prefab == null) throw new IOException(AvatarForkText.OutputMissing);
            var existing = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject)
                .Where(g => PrefabUtility.GetNearestPrefabInstanceRoot(g) == g && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) == report.prefab).ToArray();
            if (existing.Length > 1) throw new InvalidOperationException(AvatarForkText.AmbiguousAvatar);
            if (existing.Length == 1) return existing[0];
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
