using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrcAvatarWorkspace.Branching
{
    [InitializeOnLoad]
    public sealed class AvatarBranchWindow : EditorWindow
    {
        GameObject source;
        GameObject[] candidates = Array.Empty<GameObject>();
        string branchName;
        string parent = AvatarBrancher.DefaultParent;
        [NonSerialized] BranchReport result;
        string error;
        string rejectedSelection;
        bool showDetails;
        Vector2 scroll;

        static AvatarBranchWindow()
        {
            SceneHierarchyHooks.addItemsToGameObjectContextMenu += AddAvatarMenu;
            SceneHierarchyHooks.addItemsToSceneHeaderContextMenu += AddSceneMenu;
        }

        static void AddAvatarMenu(GenericMenu menu, GameObject selected)
        {
            var root = AvatarBrancher.AvatarRoot(selected);
            if (root != null) menu.AddItem(new GUIContent(AvatarForkText.CopyMenu), false, () => Open(root));
        }

        static void AddSceneMenu(GenericMenu menu, Scene scene)
        {
            if (scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene))
                menu.AddItem(new GUIContent(AvatarForkText.CopyMenu), false, () => OpenScene(scene));
        }

        [MenuItem("Tools/Avatar Fork")]
        static void OpenSelected()
        {
            var root = AvatarBrancher.AvatarRoot(Selection.activeGameObject);
            if (root != null) Open(root); else OpenScene(SceneManager.GetActiveScene());
        }

        public static AvatarBranchWindow Open(GameObject root)
        {
            var window = GetWindow<AvatarBranchWindow>(AvatarForkText.CopyTitle);
            window.minSize = new Vector2(530, 510);
            window.SetParent(AvatarBrancher.DefaultParent);
            window.candidates = root == null ? Array.Empty<GameObject>() : new[] { root };
            window.SetSource(root);
            window.Show();
            return window;
        }

        void SetSource(GameObject value)
        {
            source = value;
            branchName = value == null ? "" : value.name + "_Copy";
            ClearCompletion();
        }

        void ClearCompletion() { result = null; error = null; }

        void SetName(string value)
        {
            if (branchName == value) return;
            branchName = value;
            ClearCompletion();
        }

        void SetParent(string value)
        {
            rejectedSelection = null;
            if (parent == value) return;
            parent = value;
            ClearCompletion();
        }

        static string ParentFromSelection(string chosen)
        {
            string full = Path.GetFullPath(chosen).Replace('\\', '/').TrimEnd('/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            string candidate;
            if (string.Equals(full, assets, StringComparison.OrdinalIgnoreCase)) candidate = "Assets";
            else if (full.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase)) candidate = "Assets" + full.Substring(assets.Length);
            else throw new ArgumentException(AvatarForkText.OutsideAssets);
            AvatarBrancher.ResolveDestination(candidate, "AvatarForkDestination");
            return candidate;
        }

        void AcceptParentSelection(string chosen)
        {
            if (string.IsNullOrEmpty(chosen)) return;
            try { SetParent(ParentFromSelection(chosen)); }
            catch (Exception exception) when (IsDestinationError(exception)) { rejectedSelection = chosen; }
        }

        static bool IsDestinationError(Exception exception)
        {
            return exception is ArgumentException || exception is InvalidOperationException || exception is IOException
                || exception is NotSupportedException || exception is UnauthorizedAccessException;
        }

        static void OpenScene(Scene scene)
        {
            var avatars = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Select(t => t.gameObject).Where(g => g.GetComponents<Component>().Any(c => c != null && c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor")).ToArray();
            var selected = AvatarBrancher.AvatarRoot(Selection.activeGameObject);
            var window = Open(selected != null && selected.scene == scene ? selected : avatars.FirstOrDefault());
            window.candidates = avatars;
        }

        void OnGUI()
        {
            titleContent = new GUIContent(AvatarForkText.CopyTitle);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            AvatarForkText.LanguageSelector();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AvatarForkText.CopyTitle, EditorStyles.boldLabel);
            EditorGUILayout.Space();
            if (candidates.Length > 1)
            {
                int old = Math.Max(0, Array.IndexOf(candidates, source));
                int chosen = EditorGUILayout.Popup(AvatarForkText.Candidates, old, candidates.Select(g => g == null ? AvatarForkText.Deleted : g.name).ToArray());
                if (chosen != old) SetSource(candidates[chosen]);
            }
            var newSource = (GameObject)EditorGUILayout.ObjectField(AvatarForkText.Source, source, typeof(GameObject), true);
            if (newSource != source) SetSource(AvatarBrancher.AvatarRoot(newSource));
            if (source == null) EditorGUILayout.HelpBox(AvatarForkText.SourceMissing, MessageType.Warning);
            SetName(EditorGUILayout.TextField(AvatarForkText.CopyName, branchName ?? ""));
            bool validName = false;
            try
            {
                if (string.IsNullOrWhiteSpace(branchName)) throw new ArgumentException(AvatarForkText.EmptyName);
                AvatarBrancher.RequireBranchName(branchName);
                validName = true;
            }
            catch (ArgumentException exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Warning); }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(AvatarForkText.SaveIn);
            EditorGUILayout.SelectableLabel(parent, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(32));
            if (GUILayout.Button(AvatarForkText.Change, GUILayout.Width(82)))
            {
                string start = Directory.Exists(parent) ? Path.GetFullPath(parent) : Application.dataPath;
                AcceptParentSelection(EditorUtility.OpenFolderPanel(AvatarForkText.ChooseParent, start, ""));
            }
            if (parent != AvatarBrancher.DefaultParent && GUILayout.Button(AvatarForkText.UseAssets, GUILayout.Width(108))) SetParent(AvatarBrancher.DefaultParent);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(AvatarForkText.ParentHelp, EditorStyles.wordWrappedLabel);
            if (!string.IsNullOrEmpty(rejectedSelection))
            {
                string reason = "";
                try { ParentFromSelection(rejectedSelection); }
                catch (Exception exception) when (IsDestinationError(exception)) { reason = "\n" + exception.Message; }
                EditorGUILayout.HelpBox(AvatarForkText.Format(AvatarForkText.RejectedParent, parent) + reason, MessageType.Warning);
            }

            BranchDestination destination = null;
            if (result != null)
                destination = new BranchDestination { name = result.branchName, folder = result.folder, scene = result.scene, prefab = result.prefab };
            else if (validName)
            {
                try { destination = AvatarBrancher.ResolveDestination(parent, branchName); }
                catch (Exception exception) when (IsDestinationError(exception)) { EditorGUILayout.HelpBox(exception.Message, MessageType.Warning); }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AvatarForkText.Destination, EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(destination?.folder ?? "—", EditorStyles.wordWrappedLabel, GUILayout.MinHeight(36));
            EditorGUILayout.LabelField(AvatarForkText.Outputs, EditorStyles.wordWrappedLabel);
            if (result == null && destination != null)
            {
                if (destination.name != branchName.Trim()) EditorGUILayout.HelpBox(AvatarForkText.Format(AvatarForkText.Collision, destination.name), MessageType.Info);
                if (Path.GetFileName(parent) == branchName.Trim()) EditorGUILayout.HelpBox(AvatarForkText.Format(AvatarForkText.DoubleFolder, destination.name), MessageType.Info);
            }
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(AvatarForkText.OriginalNote, MessageType.Info);
            EditorGUILayout.HelpBox(AvatarForkText.BlueprintNote, MessageType.Info);
            EditorGUILayout.HelpBox(AvatarForkText.SharedNote, MessageType.None);
            showDetails = EditorGUILayout.Foldout(showDetails, AvatarForkText.Details, true);
            if (showDetails)
            {
                EditorGUILayout.LabelField(AvatarForkText.DetailsAssets, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(AvatarForkText.DetailsNames, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(AvatarForkText.DetailsCheck, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.Space();
            if (result == null)
                using (new EditorGUI.DisabledScope(source == null || destination == null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
                    if (GUILayout.Button(AvatarForkText.Create, GUILayout.Height(34))) CreateBranch();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(AvatarForkText.CopyFailed + "\n" + error, MessageType.Error);
            if (result != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(AvatarForkText.Format(AvatarForkText.Complete, result.assets.Length + result.extractedMaterials.Length), MessageType.Info);
                EditorGUILayout.LabelField(AvatarForkText.ResultBlueprint, EditorStyles.wordWrappedLabel);
                if (result.missingSourceReferences.Length > 0)
                    EditorGUILayout.HelpBox(AvatarForkText.Format(AvatarForkText.MissingReferences, result.missingSourceReferences.Length), MessageType.Warning);
                if (GUILayout.Button(AvatarForkText.OpenScene)) OpenResult();
                if (GUILayout.Button(AvatarForkText.ShowProject)) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<DefaultAsset>(result.folder));
                if (GUILayout.Button(AvatarForkText.SharedItems)) SharedWindow.Open(result.shared);
            }
            EditorGUILayout.EndScrollView();
        }

        void CreateBranch()
        {
            ClearCompletion();
            try { result = AvatarBrancher.Create(source, parent, branchName); }
            catch (Exception exception) { error = exception.Message; Debug.LogError("[Avatar Fork] " + AvatarForkText.CopyFailed + "\n" + error); }
            Repaint();
        }

        void OpenResult()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(result.scene, OpenSceneMode.Single);
            Selection.activeGameObject = scene.GetRootGameObjects().FirstOrDefault();
            if (Selection.activeGameObject != null && SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(new Bounds(Selection.activeGameObject.transform.position + Vector3.up, Vector3.one * 1.8f), true);
        }
    }

    sealed class SharedWindow : EditorWindow
    {
        string[] paths = Array.Empty<string>();
        Vector2 scroll;
        public static void Open(string[] paths)
        {
            var window = GetWindow<SharedWindow>(AvatarForkText.SharedTitle);
            window.paths = paths;
            window.Show();
        }
        void OnGUI()
        {
            titleContent = new GUIContent(AvatarForkText.SharedTitle);
            AvatarForkText.LanguageSelector();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox(AvatarForkText.SharedHelp, MessageType.Info);
            foreach (var path in paths) EditorGUILayout.LabelField(path, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }
    }
}
