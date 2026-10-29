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
    public sealed class AvatarBranchRenameWindow : EditorWindow
    {
        string folder;
        string requestedName;
        string error;
        bool needsRecovery;
        [NonSerialized] BranchRenamePreview preview;
        [NonSerialized] BranchReport result;
        Vector2 scroll;
        bool showPaths;

        static AvatarBranchRenameWindow()
        {
            SceneHierarchyHooks.addItemsToGameObjectContextMenu += (menu, selected) => {
                if (selected == null) return;
                string target = AvatarBrancher.FindCopyFolder(selected);
                if (target != null) menu.AddItem(new GUIContent(AvatarForkText.RenameMenu), false, () => Open(target));
            };
            SceneHierarchyHooks.addItemsToSceneHeaderContextMenu += (menu, scene) => {
                string target = AvatarBrancher.FindBranchFolder(scene.path);
                if (target != null) menu.AddItem(new GUIContent(AvatarForkText.RenameMenu), false, () => Open(target));
            };
        }

        [MenuItem("Assets/Avatar Fork/Rename Copy…", false, 2000)]
        static void OpenSelected() { Open(SelectedFolder()); }

        [MenuItem("Assets/Avatar Fork/Rename Copy…", true)]
        static bool CanOpenSelected() { return SelectedFolder() != null; }

        static string SelectedFolder()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path)) return null;
            return File.Exists(path + "/" + AvatarBrancher.ReportName) ? path : AvatarBrancher.FindBranchFolder(path);
        }

        public static AvatarBranchRenameWindow Open(string folder)
        {
            var window = GetWindow<AvatarBranchRenameWindow>(AvatarForkText.RenameTitle);
            window.minSize = new Vector2(560, 360);
            window.folder = folder;
            window.requestedName = Path.GetFileName(folder);
            window.preview = null;
            window.result = null;
            window.error = null;
            window.needsRecovery = false;
            window.Show();
            return window;
        }

        void OnGUI()
        {
            titleContent = new GUIContent(AvatarForkText.RenameTitle);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            AvatarForkText.LanguageSelector();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AvatarForkText.RenameHeading, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(AvatarForkText.Target, folder ?? "", EditorStyles.wordWrappedLabel);
            EditorGUI.BeginChangeCheck();
            requestedName = EditorGUILayout.TextField(AvatarForkText.NewName, requestedName ?? "");
            if (EditorGUI.EndChangeCheck()) { preview = null; result = null; error = null; }
            EditorGUILayout.HelpBox(AvatarForkText.RenameNote, MessageType.Info);
            EditorGUILayout.LabelField(AvatarForkText.RenamePreserved, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(AvatarForkText.RenameCustomNote, MessageType.Warning);
            if (GUILayout.Button(AvatarForkText.PreviewRename))
            {
                try { preview = AvatarBrancher.PreviewRename(folder, requestedName); error = null; }
                catch (Exception exception) { preview = null; error = exception.Message; needsRecovery = exception.Data.Contains(AvatarBrancher.RecoveryRequiredKey); }
            }
            if (preview != null)
            {
                EditorGUILayout.LabelField(AvatarForkText.ResolvedName, preview.name);
                EditorGUILayout.LabelField(AvatarForkText.RenamedDestination, preview.destination, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(AvatarForkText.ChangedFiles, preview.changes.Length.ToString());
                if (preview.additionalFiles > 0) EditorGUILayout.LabelField(AvatarForkText.AdditionalFiles, preview.additionalFiles.ToString());
                showPaths = EditorGUILayout.Foldout(showPaths, AvatarForkText.FileChanges, true);
                if (showPaths) foreach (string change in preview.changes) EditorGUILayout.LabelField(change, EditorStyles.wordWrappedLabel);
                EditorGUILayout.HelpBox(AvatarForkText.RenameUnsavedNote, MessageType.None);
                using (new EditorGUI.DisabledScope(preview.changes.Length == 0 || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button(AvatarForkText.ApplyRename, GUILayout.Height(34)))
                    {
                        try { result = AvatarBrancher.Rename(folder, requestedName); folder = result.folder; preview = null; error = null; }
                        catch (Exception exception) { error = exception.Message; needsRecovery = exception.Data.Contains(AvatarBrancher.RecoveryRequiredKey); }
                    }
            }
            if (!string.IsNullOrEmpty(error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
                if (needsRecovery && GUILayout.Button(AvatarForkText.RecoveryInstructions)) Application.OpenURL(AvatarForkText.RecoveryUrl);
            }
            if (result != null)
            {
                EditorGUILayout.HelpBox(AvatarForkText.RenameComplete, MessageType.Info);
                if (GUILayout.Button(AvatarForkText.ShowProject)) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<DefaultAsset>(result.folder));
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
