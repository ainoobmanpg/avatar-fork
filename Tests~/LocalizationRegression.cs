using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrcAvatarWorkspace.Branching;

class EditorCommand
{
    static readonly List<string> Passed = new List<string>();
    const string PreferenceKey = "com.ainoobmanpg.avatar-fork.uiLanguage";
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        Passed.Add(name);
    }
    static bool Reject(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
    }
    static object Field(object target, string name) { return target.GetType().GetField(name, PrivateInstance).GetValue(target); }
    static void Set(object target, string name, object value) { target.GetType().GetField(name, PrivateInstance).SetValue(target, value); }
    static void Call(object target, string name, object value) { target.GetType().GetMethod(name, PrivateInstance).Invoke(target, new[] { value }); }
    static object Execute()
    {
        Passed.Clear();
        bool hadPreference = EditorPrefs.HasKey(PreferenceKey);
        string preference = EditorPrefs.GetString(PreferenceKey);
        string id = GUID.Generate().ToString().Substring(0, 8);
        string work = "Assets/99_検証用/AvatarFork_Localization_" + id;
        Directory.CreateDirectory(work + "/Custom");
        AssetDatabase.Refresh();
        var active = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        AvatarBranchWindow window = null;
        try
        {
            EditorSceneManager.SaveScene(scene, work + "/Source.unity");
            var root = new GameObject("るるね赤");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<VRC.Core.PipelineManager>().blueprintId = "avtr_fixture";
            var catalog = typeof(AvatarBrancher).Assembly.GetType("VrcAvatarWorkspace.Branching.AvatarForkText");
            var texts = catalog.GetProperties(BindingFlags.Static | BindingFlags.NonPublic).Where(p => p.PropertyType == typeof(string)).ToArray();
            var english = new Dictionary<string, string>();
            foreach (string language in new[] { "en", "ja" })
            {
                EditorPrefs.SetString(PreferenceKey, language);
                foreach (var property in texts)
                {
                    string text = (string)property.GetValue(null);
                    Check(!string.IsNullOrWhiteSpace(text), language + " catalog text: " + property.Name);
                    if (language == "en") english[property.Name] = text;
                    else
                    {
                        string[] placeholders = Regex.Matches(text, @"\{\d+\}").Cast<Match>().Select(m => m.Value).OrderBy(x => x).ToArray();
                        Check(placeholders.SequenceEqual(Regex.Matches(english[property.Name], @"\{\d+\}").Cast<Match>().Select(m => m.Value).OrderBy(x => x)), "matching placeholders: " + property.Name);
                    }
                    string.Format(text, "a", "b", "c", "d", "e");
                }
            }
            EditorPrefs.DeleteKey(PreferenceKey);
            Check((string)catalog.GetProperty("CopyTitle", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == "Duplicate Avatar", "English when no language preference exists");
            EditorPrefs.SetString(PreferenceKey, "unsupported");
            Check((string)catalog.GetProperty("CopyTitle", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == "Duplicate Avatar", "English for unknown language preference");
            EditorPrefs.SetString(PreferenceKey, preference);
            Check(AvatarBrancher.DefaultParent == "Assets", "default parent is Assets");
            string rootName = "AvatarForkRoot_" + id;
            var preview = AvatarBrancher.ResolveDestination("Assets", rootName);
            Check(preview.folder == "Assets/" + rootName && !Directory.Exists(preview.folder), "Assets preview creates no folder");
            Check(AvatarBrancher.ResolveDestination("Assets/", rootName).folder == preview.folder, "trailing separator keeps root destination");
            foreach (string parent in new[] { "assets", "AssetsSibling", "Assets/../Other", "Assets/.hidden", "../Assets" })
                Check(Reject(() => AvatarBrancher.ResolveDestination(parent, rootName)), "reject invalid parent: " + parent);
            var copy = AvatarBrancher.Create(root, "Assets", rootName);
            Check(copy.folder == preview.folder && File.Exists(copy.scene) && File.Exists(copy.prefab) && AvatarBrancher.Verify(copy.folder).status == "Complete", "actual copy at Assets root passes reference check");
            Check(root.GetComponent<VRC.Core.PipelineManager>().blueprintId == "avtr_fixture" && AssetDatabase.LoadAssetAtPath<GameObject>(copy.prefab).GetComponent<VRC.Core.PipelineManager>().blueprintId == "", "root output clears only the copied upload identity");
            Check(AvatarBrancher.ResolveDestination("Assets", rootName).name == rootName + " 1", "collision preview shows numbered destination");
            var nested = AvatarBrancher.ResolveDestination(work + "/Custom", "るるね赤");
            Check(nested.folder == work + "/Custom/るるね赤", "custom parent and Japanese copy name");
            Check(Reject(() => AvatarBrancher.ResolveDestination(copy.folder, "Nested")) && !Directory.Exists(copy.folder + "/Nested"), "nested copy rejected without output");
            var renamed = AvatarBrancher.Rename(copy.folder, rootName + "Renamed");
            Check(renamed.folder == "Assets/" + rootName + "Renamed" && AvatarBrancher.Verify(renamed.folder).status == "Complete", "root copy supports rename and verification");
            var live = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(renamed.prefab), scene);
            var rebranch = AvatarBrancher.Create(live, work + "/Custom", "再複製");
            Check(AvatarBrancher.Verify(rebranch.folder).status == "Complete", "root copy supports another copy in a custom parent");
            window = ScriptableObject.CreateInstance<AvatarBranchWindow>();
            Call(window, "SetSource", root);
            Check((string)Field(window, "branchName") == "るるね赤_Copy", "initial name preserves source name with English suffix");
            Call(window, "SetName", "ユーザー名");
            Call(window, "AcceptParentSelection", Path.GetFullPath(work + "/Custom"));
            Check((string)Field(window, "parent") == work + "/Custom", "folder selection accepts custom parent");
            Set(window, "result", rebranch);
            Call(window, "AcceptParentSelection", Path.GetFullPath("."));
            Check((string)Field(window, "parent") == work + "/Custom" && ReferenceEquals(Field(window, "result"), rebranch), "rejected folder preserves accepted parent and completion");
            foreach (string language in new[] { "en", "ja" })
            {
                EditorPrefs.SetString(PreferenceKey, language);
                Check((string)Field(window, "branchName") == "ユーザー名" && (string)Field(window, "parent") == work + "/Custom" && ReferenceEquals(Field(window, "result"), rebranch), "language switch preserves name, parent and result: " + language);
            }
            Call(window, "AcceptParentSelection", "");
            Check(ReferenceEquals(Field(window, "result"), rebranch), "picker cancellation preserves completion");
            Call(window, "AcceptParentSelection", Path.GetFullPath(rebranch.folder));
            Check((string)Field(window, "parent") == work + "/Custom", "picker rejects existing copy interior");
            Call(window, "SetName", "Changed");
            Check(Field(window, "result") == null, "editing the name clears old completion");
            Set(window, "result", rebranch);
            Call(window, "AcceptParentSelection", Application.dataPath.ToUpperInvariant());
            Check((string)Field(window, "parent") == "Assets" && Field(window, "result") == null && Field(window, "rejectedSelection") == null, "Assets root selection is canonical and clears old completion");
            Set(window, "result", rebranch);
            Call(window, "SetSource", null);
            Check(Field(window, "source") == null && Field(window, "result") == null, "missing source clears completion");
            return new { success = true, checks = Passed.ToArray(), work, rootCopy = renamed.folder };
        }
        finally
        {
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            if (hadPreference) EditorPrefs.SetString(PreferenceKey, preference); else EditorPrefs.DeleteKey(PreferenceKey);
            EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }
}
