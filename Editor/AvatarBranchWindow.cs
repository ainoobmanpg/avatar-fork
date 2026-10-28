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
        BranchReport result;
        string error;
        Vector2 scroll;

        static AvatarBranchWindow()
        {
            SceneHierarchyHooks.addItemsToGameObjectContextMenu += AddAvatarMenu;
            SceneHierarchyHooks.addItemsToSceneHeaderContextMenu += AddSceneMenu;
        }

        static void AddAvatarMenu(GenericMenu menu, GameObject selected)
        {
            var root = AvatarBrancher.AvatarRoot(selected);
            if (root != null) menu.AddItem(new GUIContent("アバターを独立して複製…"), false, () => Open(root));
        }

        static void AddSceneMenu(GenericMenu menu, Scene scene)
        {
            if (scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene))
                menu.AddItem(new GUIContent("アバターを独立して複製…"), false, () => OpenScene(scene));
        }

        [MenuItem("Tools/アバターを独立して複製…")]
        static void OpenSelected()
        {
            var root = AvatarBrancher.AvatarRoot(Selection.activeGameObject);
            if (root != null) Open(root); else OpenScene(SceneManager.GetActiveScene());
        }

        public static AvatarBranchWindow Open(GameObject root)
        {
            var window = GetWindow<AvatarBranchWindow>("アバターの独立複製");
            window.minSize = new Vector2(530, 410);
            window.source = root;
            window.candidates = new[] { root };
            window.branchName = root.name + "_分岐";
            window.result = null;
            window.error = null;
            window.Show();
            return window;
        }

        static void OpenScene(Scene scene)
        {
            var avatars = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Select(t => t.gameObject).Where(g => g.GetComponents<Component>().Any(c => c != null && c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor")).ToArray();
            var selected = AvatarBrancher.AvatarRoot(Selection.activeGameObject);
            var window = selected != null && selected.scene == scene ? Open(selected) : avatars.Length > 0 ? Open(avatars[0]) : GetWindow<AvatarBranchWindow>("アバターの独立複製");
            window.candidates = avatars;
            if (avatars.Length == 0)
            {
                window.source = null;
                window.result = null;
                window.error = "このSceneにVRChatアバターが見つかりません。Hierarchyで複製したいオブジェクトを右クリックしてください。";
            }
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("現在の改変から、独立した作業用コピーを作ります", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            if (candidates.Length > 1)
            {
                int old = Math.Max(0, Array.IndexOf(candidates, source));
                int chosen = EditorGUILayout.Popup("複製するアバター", old, candidates.Select(g => g == null ? "削除済み" : g.name).ToArray());
                if (chosen != old) { source = candidates[chosen]; branchName = source.name + "_分岐"; result = null; }
            }
            var newSource = (GameObject)EditorGUILayout.ObjectField("複製元", source, typeof(GameObject), true);
            if (newSource != source) { source = AvatarBrancher.AvatarRoot(newSource); result = null; }
            branchName = EditorGUILayout.TextField("複製名", branchName ?? "");
            EditorGUILayout.BeginHorizontal();
            parent = EditorGUILayout.TextField("保存するフォルダー", parent);
            if (GUILayout.Button("選ぶ", GUILayout.Width(52)))
            {
                string chosen = EditorUtility.OpenFolderPanel("Assets内の保存先を選択", Application.dataPath, "");
                if (!string.IsNullOrEmpty(chosen))
                {
                    string project = Path.GetFullPath(".").Replace('\\', '/') + "/";
                    if (chosen.StartsWith(project + "Assets/", StringComparison.OrdinalIgnoreCase)) parent = chosen.Substring(project.Length);
                    else if (chosen == project + "Assets") parent = "Assets/07_改変用/独立複製";
                    else error = "Assets内のフォルダーを選んでください。";
                }
            }
            EditorGUILayout.EndHorizontal();
            bool validDestination = false;
            try
            {
                var preview = AvatarBrancher.ResolveDestination(parent, branchName);
                EditorGUILayout.LabelField("次に作成する名前", preview.name);
                EditorGUILayout.LabelField("Scene / Prefab", preview.name + ".unity / " + preview.name + ".prefab", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("素材名の例", "Materials/Purple__" + preview.name + ".mat", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("保存先", preview.folder, EditorStyles.wordWrappedLabel);
                validDestination = true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException
                || exception is IOException || exception is NotSupportedException || exception is UnauthorizedAccessException)
            { EditorGUILayout.HelpBox(exception.Message, MessageType.Warning); }
            EditorGUILayout.HelpBox("同名がある場合は末尾に番号を付け、Hierarchy・Scene・Prefab・素材へ同じ名前を使います。骨・子オブジェクト・アニメーションの制御名は維持します。", MessageType.None);
            EditorGUILayout.HelpBox("マテリアルの親子関係、画像、メッシュ、アニメーション、メニュー、設定、独自Shaderを一式コピーします。元のSceneと素材は保存し直しません。", MessageType.Info);
            EditorGUILayout.HelpBox("複製先のBlueprint IDは空にします。アップロード先を引き継がず、新規アバターとして扱われる状態で保存します。MAやギミックの構成・設定は引き継ぎます。", MessageType.Info);
            EditorGUILayout.HelpBox("Unity標準素材、Packages内のSDK・MA・lilToon等、スクリプト・DLLは共通です。それ以外に元の素材への参照が残った場合は、失敗として知らせます。", MessageType.None);
            using (new EditorGUI.DisabledScope(source == null || !validDestination || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
                if (GUILayout.Button("一式を複製して、独立しているか検査する", GUILayout.Height(34))) CreateBranch();

            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (result != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("複製と参照検査が完了しました。コピーした素材: " + (result.assets.Length + result.extractedMaterials.Length) + "ファイル", MessageType.Info);
                EditorGUILayout.LabelField("作成済み", result.branchName ?? Path.GetFileName(result.folder));
                EditorGUILayout.LabelField("Blueprint IDは空です。複製元の公開アバターへ上書きするIDを持っていません。", EditorStyles.wordWrappedLabel);
                if (result.missingSourceReferences.Length > 0)
                    EditorGUILayout.HelpBox("元から参照先が存在しないデータが " + result.missingSourceReferences.Length + "件ありました。コピー側では参照を空にして、後から元へ再接続されないようにしています。詳細は保存先のBranchReport.jsonに記録しました。", MessageType.Warning);
                EditorGUILayout.SelectableLabel(result.folder, EditorStyles.wordWrappedLabel, GUILayout.Height(38));
                EditorGUILayout.LabelField("作成後のSceneとPrefabは同じフォルダーにあります。", EditorStyles.wordWrappedLabel);
                if (GUILayout.Button("作成したSceneを開く")) OpenResult();
                if (GUILayout.Button("保存先をProjectで表示")) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<DefaultAsset>(result.folder));
                if (GUILayout.Button("共有するプログラム・標準素材の一覧")) SharedWindow.Open(result.shared);
            }
            EditorGUILayout.EndScrollView();
        }

        void CreateBranch()
        {
            error = null;
            result = null;
            try { result = AvatarBrancher.Create(source, parent, branchName); }
            catch (Exception exception) { error = "複製は完了していません。\n" + exception.Message; Debug.LogError("[AvatarBrancher] " + error); }
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
            var window = GetWindow<SharedWindow>("共有するプログラムと標準素材");
            window.paths = paths;
            window.Show();
        }
        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("この一覧のプログラムやパッケージ自体を更新した場合は、同じUnity Project内のアバターに影響します。", MessageType.Info);
            foreach (var path in paths) EditorGUILayout.LabelField(path, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }
    }
}
