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
        [NonSerialized] BranchScenePlacement placement;
        [NonSerialized] GameObject placed;
        string reportGuid;
        string prefabGuid;
        GameObject placementChoice;
        string placementError;
        string resultError;
        string error;
        string failedFolder;
        string rejectedSelection;
        bool showDetails;
        Vector2 scroll;

        static AvatarBranchWindow()
        {
            SceneHierarchyHooks.addItemsToGameObjectContextMenu += AddAvatarMenu;
            SceneHierarchyHooks.addItemsToSceneHeaderContextMenu += AddSceneMenu;
        }

        void OnEnable()
        {
            AvatarBrancher.CopyRenamed += CopyRenamed;
            EditorApplication.projectChanged += RefreshCompletion;
        }

        void OnDisable()
        {
            AvatarBrancher.CopyRenamed -= CopyRenamed;
            EditorApplication.projectChanged -= RefreshCompletion;
            placement?.Dispose();
        }

        void CopyRenamed(BranchReport report) { RefreshCompletion(); }

        void RefreshResult()
        {
            if (result == null || string.IsNullOrEmpty(reportGuid)) return;
            string path = AssetDatabase.GUIDToAssetPath(reportGuid);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new IOException(AvatarForkText.OutputMissing);
            var current = JsonUtility.FromJson<BranchReport>(File.ReadAllText(path));
            if (current == null || current.version != 1 || current.status != "Complete"
                || current.folder + "/" + AvatarBrancher.ReportName != path
                || string.IsNullOrEmpty(prefabGuid) || AssetDatabase.GUIDToAssetPath(prefabGuid) != current.prefab)
                throw new IOException(AvatarForkText.OutputMissing);
            result = current;
            branchName = current.branchName;
        }

        void RefreshCompletion()
        {
            try { RefreshResult(); resultError = null; }
            catch (Exception exception) { resultError = exception.Message; }
            Repaint();
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
            window.error = null;
            window.failedFolder = null;
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

        void ClearCompletion()
        {
            placement?.Dispose();
            result = null;
            placement = null;
            placed = null;
            reportGuid = null;
            prefabGuid = null;
            placementChoice = null;
            placementError = null;
            resultError = null;
            if (failedFolder == null) error = null;
        }

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
                int old = Array.IndexOf(candidates, source) + 1;
                int chosen = EditorGUILayout.Popup(AvatarForkText.Candidates, old, new[] { AvatarForkText.OtherSource }
                    .Concat(candidates.Select(g => g == null ? AvatarForkText.Deleted : g.name)).ToArray());
                if (chosen != old && chosen > 0) SetSource(candidates[chosen - 1]);
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
            EditorGUILayout.LabelField(failedFolder == null ? AvatarForkText.Destination : AvatarForkText.NextDestination, EditorStyles.boldLabel);
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
            if (!string.IsNullOrEmpty(error))
            {
                EditorGUILayout.HelpBox(AvatarForkText.CopyFailed + "\n" + error, MessageType.Error);
                if (!string.IsNullOrEmpty(failedFolder))
                {
                    EditorGUILayout.LabelField(AvatarForkText.FailedDestination, EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(failedFolder, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(36));
                    EditorGUILayout.HelpBox(AvatarForkText.FailedOutputNote, MessageType.Warning);
                    if (GUILayout.Button(AvatarForkText.CopyFailureInstructions)) Application.OpenURL(AvatarForkText.CopyFailureUrl);
                }
            }
            if (result != null)
            {
                EditorGUILayout.Space();
                if (!string.IsNullOrEmpty(resultError)) EditorGUILayout.HelpBox(resultError, MessageType.Error);
                EditorGUILayout.HelpBox(AvatarForkText.Format(AvatarForkText.Complete, result.assets.Length + result.extractedMaterials.Length), MessageType.Info);
                EditorGUILayout.LabelField(AvatarForkText.ResultBlueprint, EditorStyles.wordWrappedLabel);
                if (result.missingSourceReferences.Length > 0)
                    EditorGUILayout.HelpBox(AvatarForkText.Format(AvatarForkText.MissingReferences, result.missingSourceReferences.Length), MessageType.Warning);
                EditorGUILayout.HelpBox(AvatarForkText.PlacementNote, MessageType.Info);
                if (!string.IsNullOrEmpty(placementError)) EditorGUILayout.HelpBox(AvatarForkText.PlacementFailed + "\n" + placementError, MessageType.Error);
                placed = placement?.Resolve();
                bool placementBusy = placement?.IsBusy ?? (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling);
                using (new EditorGUI.DisabledScope(placementBusy || (placement != null && placement.HasPlaced && placed == null)))
                    if (GUILayout.Button(placed == null ? AvatarForkText.AddToHierarchy : AvatarForkText.SelectInHierarchy)) PlaceResult();
                if (placement != null && placement.HasPlaced && placed == null)
                {
                    EditorGUILayout.HelpBox(AvatarForkText.PlacementUnresolved, MessageType.Warning);
                    placementChoice = (GameObject)EditorGUILayout.ObjectField(AvatarForkText.ExistingCopy, placementChoice, typeof(GameObject), true);
                    using (new EditorGUI.DisabledScope(placementBusy || placementChoice == null))
                        if (GUILayout.Button(AvatarForkText.UseExistingCopy)) BindResult();
                    using (new EditorGUI.DisabledScope(placementBusy))
                        if (GUILayout.Button(AvatarForkText.AddAnotherCopy)) PlaceAnotherResult();
                }
                if (GUILayout.Button(AvatarForkText.ShowProject))
                {
                    try { RefreshResult(); EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<DefaultAsset>(result.folder)); resultError = null; }
                    catch (Exception exception) { resultError = exception.Message; }
                }
                if (GUILayout.Button(AvatarForkText.SharedItems)) SharedWindow.Open(result.shared);
            }
            EditorGUILayout.EndScrollView();
        }

        void CreateBranch()
        {
            ClearCompletion();
            error = null;
            failedFolder = null;
            try
            {
                placement = source == null ? null : new BranchScenePlacement(source);
                result = AvatarBrancher.Create(source, parent, branchName);
                reportGuid = AssetDatabase.AssetPathToGUID(result.folder + "/" + AvatarBrancher.ReportName);
                prefabGuid = AssetDatabase.AssetPathToGUID(result.prefab);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                failedFolder = exception.Data[AvatarBrancher.FailedFolderKey] as string;
                Debug.LogError("[Avatar Fork] " + AvatarForkText.CopyFailed + "\n" + error + (failedFolder == null ? "" : "\n" + failedFolder));
            }
            if (result != null) PlaceResult();
            Repaint();
        }

        void PlaceResult()
        {
            Place(false);
        }

        void PlaceAnotherResult()
        {
            Place(true);
        }

        void BindResult()
        {
            try
            {
                RefreshResult();
                placement.Bind(placementChoice, result);
                PlaceResult();
            }
            catch (Exception exception) { placementError = exception.Message; }
        }

        void Place(bool createAnother)
        {
            try
            {
                if (result == null || placement == null) return;
                RefreshResult();
                placed = placement.Add(result, createAnother);
                placementError = null;
                Selection.activeGameObject = placed;
                EditorGUIUtility.PingObject(placed);
                EditorApplication.RepaintHierarchyWindow();
            }
            catch (Exception exception) { placementError = exception.Message; }
            Repaint();
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
