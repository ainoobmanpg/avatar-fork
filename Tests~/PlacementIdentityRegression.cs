using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrcAvatarWorkspace.Branching;

class EditorCommand
{
    static readonly List<string> Passed = new List<string>();
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Get(object target, string name) { return target.GetType().GetField(name, Instance).GetValue(target); }
    static void Set(object target, string name, object value) { target.GetType().GetField(name, Instance).SetValue(target, value); }
    static void Call(object target, string name, params object[] args) { target.GetType().GetMethod(name, Instance).Invoke(target, args); }
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        Passed.Add(name);
    }
    static GameObject[] Copies(Scene scene, string prefab)
    {
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject)
            .Where(g => PrefabUtility.GetNearestPrefabInstanceRoot(g) == g && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) == prefab).ToArray();
    }
    static object Execute()
    {
        Passed.Clear();
        string work = "Assets/99_検証用/AvatarFork_PlacementIdentity_" + GUID.Generate().ToString().Substring(0, 8);
        Directory.CreateDirectory(work);
        AssetDatabase.Refresh();
        var active = SceneManager.GetActiveScene();
        var sourceScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        AvatarBranchWindow window = null;
        try
        {
            var source = new GameObject("Source");
            SceneManager.MoveGameObjectToScene(source, sourceScene);
            source.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            EditorSceneManager.SaveScene(sourceScene, work + "/Source.unity");
            window = ScriptableObject.CreateInstance<AvatarBranchWindow>();
            Call(window, "SetSource", source);
            Call(window, "SetParent", work);
            Call(window, "SetName", "Generated");
            Call(window, "CreateBranch");
            var report = (BranchReport)Get(window, "result");
            Check(report != null, "create output and retain the completion window");
            string movedPrefab = work + "/TemporarilyMoved.prefab";
            Check(string.IsNullOrEmpty(AssetDatabase.MoveAsset(report.prefab, movedPrefab)), "temporarily move output away from the report path");
            Call(window, "RefreshCompletion");
            Check(Get(window, "resultError") != null, "completion refresh reports a mismatched output path");
            Check(string.IsNullOrEmpty(AssetDatabase.MoveAsset(movedPrefab, report.prefab)), "restore the recorded output path");
            Call(window, "RefreshCompletion");
            Check(Get(window, "resultError") == null, "a successful completion refresh clears its previous error");
            var placed = (GameObject)Get(window, "placed");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(report.prefab);
            var other = (GameObject)PrefabUtility.InstantiatePrefab(prefab, sourceScene);
            other.name = "Other source instance";
            var target = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.MoveGameObjectToScene(placed, target);
            var targetOther = (GameObject)PrefabUtility.InstantiatePrefab(prefab, target);
            targetOther.name = "Other target instance";
            var parent = new GameObject("Parent");
            SceneManager.MoveGameObjectToScene(parent, target);
            placed.transform.SetParent(parent.transform, true);
            EditorSceneManager.SaveScene(sourceScene);
            EditorSceneManager.SaveScene(target, work + "/Target.unity");
            var expectedId = GlobalObjectId.GetGlobalObjectIdSlow(placed);
            var standalone = EditorSceneManager.OpenScene(report.scene, OpenSceneMode.Additive);
            EditorSceneManager.CloseScene(target, true);
            Call(window, "PlaceResult");
            Check(Get(window, "placementError") != null && Copies(sourceScene, report.prefab).Length == 1,
                "a closed destination Scene does not cause duplication or adopt another source instance");
            target = EditorSceneManager.OpenScene(work + "/Target.unity", OpenSceneMode.Additive);
            Call(window, "PlaceResult");
            placed = (GameObject)Get(window, "placed");
            Check(placed != null && GlobalObjectId.GetGlobalObjectIdSlow(placed).Equals(expectedId) && placed.transform.parent.name == "Parent"
                && Copies(target, report.prefab).Length == 2 && Copies(sourceScene, report.prefab).Length == 1,
                "reload identifies the moved parented individual among identical Prefabs and a standalone Scene");
            Check(Get(window, "placementError") == null, "resolving a saved individual clears the placement error");
            string renamedTarget = work + "/MovedTarget.unity";
            EditorSceneManager.CloseScene(target, true);
            Check(string.IsNullOrEmpty(AssetDatabase.MoveAsset(work + "/Target.unity", renamedTarget)), "rename the destination Scene asset");
            target = EditorSceneManager.OpenScene(renamedTarget, OpenSceneMode.Additive);
            Call(window, "PlaceResult");
            placed = (GameObject)Get(window, "placed");
            Check(placed != null && placed.scene == target && GlobalObjectId.GetGlobalObjectIdSlow(placed).Equals(expectedId),
                "saved individual survives its Scene asset rename");
            UnityEngine.Object.DestroyImmediate(placed);
            EditorSceneManager.SaveScene(target);
            EditorSceneManager.CloseScene(target, true);
            target = EditorSceneManager.OpenScene(renamedTarget, OpenSceneMode.Additive);
            Call(window, "PlaceResult");
            Check(Get(window, "placementError") != null && (GameObject)Get(window, "placed") == null && Copies(target, report.prefab).Length == 1
                && Copies(sourceScene, report.prefab).Length == 1, "a deleted individual is not replaced by the sole remaining matching instance");
            Set(window, "placementChoice", source);
            Call(window, "BindResult");
            Check(Get(window, "placementError") != null && (GameObject)Get(window, "placed") == null, "manual choice rejects an unrelated avatar");
            targetOther = Copies(target, report.prefab).Single();
            Set(window, "placementChoice", targetOther);
            Call(window, "BindResult");
            Check((GameObject)Get(window, "placed") == targetOther && Get(window, "placementError") == null,
                "explicitly choosing an existing matching individual does not create one");
            var generated = Copies(standalone, report.prefab).Single();
            var generatedParent = new GameObject("Generated parent");
            SceneManager.MoveGameObjectToScene(generatedParent, standalone);
            generated.transform.SetParent(generatedParent.transform, true);
            generatedParent.SetActive(false);
            PrefabUtility.InstantiatePrefab(prefab, standalone);
            var manual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, standalone);
            manual.name = "Manual name";
            PrefabUtility.RecordPrefabInstancePropertyModifications(manual);
            EditorSceneManager.SaveScene(standalone);
            var renamed = AvatarBrancher.Rename(report.folder, "Renamed");
            var refreshed = (BranchReport)Get(window, "result");
            Check(refreshed.folder == renamed.folder && refreshed.prefab == renamed.prefab && refreshed.scene == renamed.scene && (string)Get(window, "branchName") == "Renamed",
                "rename notification refreshes the open completion window and destination paths");
            standalone = SceneManager.GetSceneByPath(renamed.scene);
            var generatedCopies = Copies(standalone, renamed.prefab);
            Check(generatedCopies.Length == 3 && generatedCopies.Count(g => g.name == "Renamed") == 2 && generatedCopies.Count(g => g.name == "Manual name") == 1
                && generatedCopies.Single(g => g.transform.parent != null).transform.parent.name == "Generated parent",
                "standalone Scene renames all automatic names under inactive parents and preserves manual names");
            EditorSceneManager.CloseScene(standalone, true);
            standalone = EditorSceneManager.OpenScene(renamed.scene, OpenSceneMode.Additive);
            Check(Copies(standalone, renamed.prefab).Count(g => g.name == "Renamed") == 2, "standalone instance names remain correct after reload");
            UnityEngine.Object.DestroyImmediate(targetOther);
            EditorSceneManager.SaveScene(target);
            string sourcePath = sourceScene.path;
            EditorSceneManager.CloseScene(sourceScene, true);
            string newSourcePath = work + "/RenamedSource.unity";
            Check(string.IsNullOrEmpty(AssetDatabase.MoveAsset(sourcePath, newSourcePath)), "rename the source Scene asset");
            sourceScene = EditorSceneManager.OpenScene(newSourcePath, OpenSceneMode.Additive);
            Call(window, "PlaceResult");
            Check(Get(window, "placementError") != null && Copies(sourceScene, renamed.prefab).Length == 1,
                "retry after output rename and deletion does not silently recreate or adopt another copy");
            Call(window, "PlaceAnotherResult");
            placed = (GameObject)Get(window, "placed");
            Check(placed != null && placed.scene == sourceScene && Get(window, "placementError") == null && Copies(sourceScene, renamed.prefab).Length == 2,
                "explicit new placement uses renamed output and resolves the renamed source Scene by GUID");
            byte[] previousSource = File.ReadAllBytes(newSourcePath);
            string savedAsPath = work + "/SavedAsSource.unity";
            Check(EditorSceneManager.SaveScene(sourceScene, savedAsPath), "save the reopened source Scene under another asset name");
            UnityEngine.Object.DestroyImmediate(placed);
            EditorSceneManager.SaveScene(sourceScene);
            Call(window, "PlaceAnotherResult");
            placed = (GameObject)Get(window, "placed");
            Check(placed != null && placed.scene == sourceScene && placed.scene.path == savedAsPath && Get(window, "placementError") == null,
                "new placement follows Save As on the reopened source Scene");
            Check(previousSource.SequenceEqual(File.ReadAllBytes(newSourcePath)), "Save As and new placement preserve the previous source Scene file");
            Check(AvatarBrancher.Verify(renamed.folder).status == "Complete", "renamed output remains valid with multiple parented instances");
            Check(!File.Exists(report.prefab) && !Directory.Exists(report.folder), "rename leaves no old output paths");
            return new { success = true, passed = Passed.ToArray(), work };
        }
        finally
        {
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            foreach (var open in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                .Where(s => s.path.StartsWith(work + "/", StringComparison.Ordinal)).ToArray())
                EditorSceneManager.CloseScene(open, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }
}
