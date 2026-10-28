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
        BranchRenamePreview preview;
        BranchReport result;
        Vector2 scroll;
        bool showPaths;

        static AvatarBranchRenameWindow()
        {
            SceneHierarchyHooks.addItemsToGameObjectContextMenu += (menu, selected) => {
                if (selected == null) return;
                string target = AvatarBrancher.FindBranchFolder(selected.scene.path);
                if (target != null) menu.AddItem(new GUIContent("この複製の名前を一式変更…"), false, () => Open(target));
            };
            SceneHierarchyHooks.addItemsToSceneHeaderContextMenu += (menu, scene) => {
                string target = AvatarBrancher.FindBranchFolder(scene.path);
                if (target != null) menu.AddItem(new GUIContent("この複製の名前を一式変更…"), false, () => Open(target));
            };
        }

        [MenuItem("Assets/この複製の名前を一式変更…", false, 2000)]
        static void OpenSelected() { Open(SelectedFolder()); }

        [MenuItem("Assets/この複製の名前を一式変更…", true)]
        static bool CanOpenSelected() { return SelectedFolder() != null; }

        static string SelectedFolder()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path)) return null;
            return File.Exists(path + "/" + AvatarBrancher.ReportName) ? path : AvatarBrancher.FindBranchFolder(path);
        }

        public static AvatarBranchRenameWindow Open(string folder)
        {
            var window = GetWindow<AvatarBranchRenameWindow>("複製の名前を一式変更");
            window.minSize = new Vector2(560, 360);
            window.folder = folder;
            window.requestedName = Path.GetFileName(folder);
            window.preview = null;
            window.result = null;
            window.error = null;
            window.Show();
            return window;
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("この複製が持つ一式の名前を変更します", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("対象", folder ?? "", EditorStyles.wordWrappedLabel);
            EditorGUI.BeginChangeCheck();
            requestedName = EditorGUILayout.TextField("新しい名前", requestedName ?? "");
            if (EditorGUI.EndChangeCheck()) { preview = null; result = null; error = null; }
            EditorGUILayout.HelpBox("ルート・Scene・Prefab・保存先・素材名を揃えます。骨・子オブジェクト・アニメーションの制御名と、登録済みのアップロード先は維持します。", MessageType.Info);
            EditorGUILayout.HelpBox("手で変更した素材名や表示名、後から追加した素材の名前は維持します。コピーの外に文字列で保存した素材パスは、自動更新の対象外です。", MessageType.None);
            if (GUILayout.Button("変更内容を確認"))
            {
                try { preview = AvatarBrancher.PreviewRename(folder, requestedName); error = null; }
                catch (Exception exception) { preview = null; error = exception.Message; }
            }
            if (preview != null)
            {
                EditorGUILayout.LabelField("変更後の名前", preview.name);
                EditorGUILayout.LabelField("変更後の保存先", preview.destination, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("移動・改名するファイル", preview.changes.Length.ToString());
                if (preview.additionalFiles > 0) EditorGUILayout.LabelField("記録外のファイル（名前を維持）", preview.additionalFiles.ToString());
                showPaths = EditorGUILayout.Foldout(showPaths, "ファイルごとの変更", true);
                if (showPaths) foreach (string change in preview.changes) EditorGUILayout.LabelField(change, EditorStyles.wordWrappedLabel);
                EditorGUILayout.HelpBox("未保存変更がある場合は停止します。保存済みの対象Sceneは、処理中に閉じて変更後に開き直します。", MessageType.None);
                using (new EditorGUI.DisabledScope(preview.changes.Length == 0 || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button("この内容で名前を一式変更する", GUILayout.Height(34)))
                    {
                        try { result = AvatarBrancher.Rename(folder, requestedName); folder = result.folder; preview = null; error = null; }
                        catch (Exception exception) { error = exception.Message; }
                    }
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (result != null)
            {
                EditorGUILayout.HelpBox("名前の変更と参照の検査が完了しました。", MessageType.Info);
                if (GUILayout.Button("保存先をProjectで表示")) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<DefaultAsset>(result.folder));
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
