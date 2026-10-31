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
#if AVATAR_FORK_TESTS
        internal static Action<string> TestStep;
#endif
        [System.Diagnostics.Conditional("AVATAR_FORK_TESTS")]
        static void PlacementStep(string step)
        {
#if AVATAR_FORK_TESTS
            TestStep?.Invoke(step);
#endif
        }

        Scene originalScene;
        string sceneGuid;
        readonly Vector3 position;
        readonly Quaternion rotation;
        readonly Vector3 scale;
        readonly bool active;
        GameObject placed;
        GlobalObjectId placedId;
        int instanceId;
        string placedPrefabGuid;
        bool trackingSuspended;
        bool reloadSceneForPlay;
        public bool HasPlaced { get; private set; }
        internal bool IsBusy => trackingSuspended || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling;

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
            EditorApplication.playModeStateChanged += PlayModeChanged;
        }

        public void Dispose()
        {
            EditorApplication.hierarchyChanged -= Track;
            EditorSceneManager.sceneSaved -= SceneSaved;
            EditorSceneManager.sceneOpened -= SceneOpened;
            EditorSceneManager.sceneClosing -= SceneClosing;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
        }

        void SceneSaved(Scene scene) { Track(); }
        void SceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (!string.IsNullOrEmpty(sceneGuid) && AssetDatabase.AssetPathToGUID(scene.path) == sceneGuid) originalScene = scene;
        }
        void SceneClosing(Scene scene, bool removingScene) { Track(); }

        void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                reloadSceneForPlay = !EditorSettings.enterPlayModeOptionsEnabled || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) == 0;
                trackingSuspended = true;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (reloadSceneForPlay) placed = null;
                trackingSuspended = false;
            }
        }

        void Track()
        {
            if (IsBusy) return;
            if (originalScene.IsValid() && originalScene.isLoaded)
                sceneGuid = AssetDatabase.AssetPathToGUID(originalScene.path);
            if (placed == null) return;
            instanceId = placed.GetInstanceID();
            placedId = GlobalObjectId.GetGlobalObjectIdSlow(placed);
        }

        public GameObject Resolve()
        {
            if (IsBusy) return null;
            if (placed == null)
            {
                var candidate = instanceId == 0 ? null : EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                if (!Matches(candidate))
                    candidate = placedId.identifierType == 2 ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(placedId) as GameObject : null;
                if (!Matches(candidate)) return null;
                placed = candidate;
            }
            Track();
            return placed;
        }

        bool Matches(GameObject candidate)
        {
            return candidate != null && candidate.scene.IsValid() && candidate.scene.isLoaded && !EditorSceneManager.IsPreviewScene(candidate.scene)
                && placedId.identifierType == 2 && !placedId.assetGUID.Equals(default(GUID)) && placedId.targetObjectId != 0
                && GlobalObjectId.GetGlobalObjectIdSlow(candidate).Equals(placedId)
                && PrefabUtility.GetNearestPrefabInstanceRoot(candidate) == candidate && !string.IsNullOrEmpty(placedPrefabGuid)
                && AssetDatabase.AssetPathToGUID(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(candidate)) == placedPrefabGuid;
        }

        public void Bind(GameObject instance, BranchReport report)
        {
            if (IsBusy)
                throw new InvalidOperationException(AvatarForkText.Busy);
            if (instance == null || !instance.scene.IsValid() || !instance.scene.isLoaded || EditorSceneManager.IsPreviewScene(instance.scene)
                || PrefabUtility.GetNearestPrefabInstanceRoot(instance) != instance
                || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance) != report.prefab)
                throw new InvalidOperationException(AvatarForkText.SelectPlacedCopy);
            placed = instance;
            placedPrefabGuid = AssetDatabase.AssetPathToGUID(report.prefab);
            HasPlaced = true;
            Track();
        }

        public GameObject Add(BranchReport report, bool createAnother = false)
        {
            if (IsBusy)
                throw new InvalidOperationException(AvatarForkText.Busy);
            var previousPlaced = placed;
            var previousId = placedId;
            int previousInstanceId = instanceId;
            string previousPrefabGuid = placedPrefabGuid;
            bool previousHasPlaced = HasPlaced;
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
                PlacementStep("instantiated");
                Undo.RegisterCreatedObjectUndo(instance, AvatarForkText.Create);
                PlacementStep("creationUndo");
                Undo.RegisterFullObjectHierarchyUndo(instance, AvatarForkText.Create);
                PlacementStep("hierarchyUndo");
                instance.transform.SetPositionAndRotation(position, rotation);
                instance.transform.localScale = scale;
                instance.SetActive(active);
                instance.transform.SetAsLastSibling();
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                EditorSceneManager.MarkSceneDirty(scene);
                PlacementStep("collapse");
                Undo.CollapseUndoOperations(group);
                placed = instance;
                placedPrefabGuid = AssetDatabase.AssetPathToGUID(report.prefab);
                HasPlaced = true;
                PlacementStep("tracking");
                Track();
                PlacementStep("tracked");
                return instance;
            }
            catch (Exception error)
            {
                try { PlacementStep("rollback"); Undo.RevertAllDownToGroup(group); }
                catch (Exception cleanup)
                {
                    error.Data["AvatarFork.PlacementUndoError"] = cleanup;
                    Debug.LogException(cleanup);
                    try { if (instance != null) Undo.ClearUndo(instance); }
                    catch (Exception clear) { error.Data["AvatarFork.PlacementClearUndoError"] = clear; Debug.LogException(clear); }
                }
                try { if (instance != null) UnityEngine.Object.DestroyImmediate(instance); }
                catch (Exception cleanup) { error.Data["AvatarFork.PlacementDestroyError"] = cleanup; Debug.LogException(cleanup); }
                placed = previousPlaced;
                placedId = previousId;
                instanceId = previousInstanceId;
                placedPrefabGuid = previousPrefabGuid;
                HasPlaced = previousHasPlaced;
                throw;
            }
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
