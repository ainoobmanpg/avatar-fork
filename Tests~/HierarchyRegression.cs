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
    static void Call(object target, string name, params object[] args) { target.GetType().GetMethod(name, Instance).Invoke(target, args); }
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        Passed.Add(name);
    }
    static GameObject Placed(Scene scene, BranchReport report)
    {
        return scene.GetRootGameObjects().Single(g => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) == report.prefab);
    }
    static string CopyFolder(GameObject root)
    {
        return (string)typeof(AvatarBrancher).GetMethod("FindCopyFolder", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root });
    }
    public static void Run()
    {
        try
        {
            var result = Execute();
            Directory.CreateDirectory("Library/AvatarFork/Regression");
            File.WriteAllText("Library/AvatarFork/Regression/hierarchy.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
            Debug.Log("Hierarchy regression passed");
        }
        catch (Exception error) { Debug.LogException(error); if (Application.isBatchMode) EditorApplication.Exit(1); throw; }
    }
    static object Execute()
    {
        Passed.Clear();
        string work = "Assets/99_検証用/AvatarFork_Hierarchy_" + GUID.Generate().ToString().Substring(0, 8);
        Directory.CreateDirectory(work);
        AssetDatabase.Refresh();
        var active = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(active.path)) EditorSceneManager.SaveScene(active, work + "/Empty.unity");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        AvatarBranchWindow window = null;
        try
        {
            var parent = new GameObject("Parent");
            SceneManager.MoveGameObjectToScene(parent, scene);
            parent.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(0, 25, 0));
            parent.transform.localScale = Vector3.one * 1.3f;
            var root = new GameObject("Source");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.localPosition = new Vector3(1, 0, 2);
            root.transform.localRotation = Quaternion.Euler(0, 10, 0);
            root.transform.localScale = Vector3.one * .8f;
            root.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            root.AddComponent<VRC.Core.PipelineManager>().blueprintId = "avtr_source";
            var child = new GameObject("KeepBoneName");
            child.transform.SetParent(root.transform, false);
            var material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, work + "/Source.mat");
            child.AddComponent<MeshRenderer>().sharedMaterial = material;
            root.SetActive(false);
            EditorSceneManager.SaveScene(scene, work + "/Source.unity");
            SceneManager.SetActiveScene(active);
            byte[] originalScene = File.ReadAllBytes(scene.path);
            int roots = scene.rootCount;
            int scenes = SceneManager.sceneCount;
            window = ScriptableObject.CreateInstance<AvatarBranchWindow>();
            Call(window, "SetSource", root);
            Call(window, "SetParent", work);
            Call(window, "SetName", "Generated");
            Call(window, "CreateBranch");
            var copy = (BranchReport)Get(window, "result");
            Check(copy != null && copy.status == "Complete", "copy files are complete");
            Check(scene.rootCount == roots + 1, "UI automatically adds one copy to the source Scene");
            var placed = Placed(scene, copy);
            Check(placed.transform.GetSiblingIndex() == scene.rootCount - 1 && Selection.activeGameObject == placed, "copy is selected at the last root position");
            Check(SceneManager.sceneCount == scenes && SceneManager.GetActiveScene() == active, "open Scenes and active Scene are preserved");
            Check(Vector3.Distance(placed.transform.position, root.transform.position) < .0001f && Quaternion.Angle(placed.transform.rotation, root.transform.rotation) < .001f
                && Vector3.Distance(placed.transform.lossyScale, root.transform.lossyScale) < .0001f, "parented source world Transform is preserved");
            Check(!placed.activeSelf && !root.activeSelf && placed.GetComponent<VRC.Core.PipelineManager>().blueprintId == ""
                && root.GetComponent<VRC.Core.PipelineManager>().blueprintId == "avtr_source", "active state and separate upload identity are preserved");
            Check(AssetDatabase.GetAssetPath(placed.GetComponentInChildren<MeshRenderer>(true).sharedMaterial).StartsWith(copy.folder + "/", StringComparison.Ordinal), "placed copy uses its copied material");
            Check(scene.isDirty && originalScene.SequenceEqual(File.ReadAllBytes(scene.path)), "source Scene changes are not saved to disk");
            Undo.PerformUndo();
            Check(scene.rootCount == roots && File.Exists(copy.prefab), "Undo removes only the placed instance");
            Undo.PerformRedo();
            placed = Placed(scene, copy);
            Check(scene.rootCount == roots + 1 && Vector3.Distance(placed.transform.position, root.transform.position) < .0001f && !placed.activeSelf, "Redo restores placement and active state");
            Call(window, "PlaceResult");
            Check(scene.rootCount == roots + 1, "retry selects an existing instance without duplicating it");
            Undo.PerformUndo();
            string prefabPath = copy.prefab;
            string missingPrefab = work + "/Missing.prefab";
            Check(string.IsNullOrEmpty(AssetDatabase.MoveAsset(prefabPath, missingPrefab)), "temporarily move output to test a failed retry");
            Call(window, "PlaceResult");
            Check(Get(window, "placementError") != null && Get(window, "result") != null && scene.rootCount == roots, "mismatched output rejects retry before instantiation and preserves complete files");
            Check(string.IsNullOrEmpty(AssetDatabase.MoveAsset(missingPrefab, prefabPath)), "restore output before retry");
            Call(window, "PlaceAnotherResult");
            placed = Placed(scene, copy);
            Check(Get(window, "placementError") == null && scene.rootCount == roots + 1, "placement retry reuses the completed output");
            Check(CopyFolder(placed.transform.GetChild(0).gameObject) == copy.folder, "child selection identifies the owning avatar copy");
            placed.transform.SetParent(parent.transform, true);
            Call(window, "PlaceResult");
            Check(placed.transform.parent == parent.transform && scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true)).Count() == 2,
                "selecting a reparented copy does not add another instance");
            EditorSceneManager.SaveScene(scene);
            byte[] beforeRename = File.ReadAllBytes(scene.path);
            var renamed = AvatarBrancher.Rename(copy.folder, "Renamed");
            Check(placed.name == "Renamed" && scene.isDirty && beforeRename.SequenceEqual(File.ReadAllBytes(scene.path)), "rename follows the external Scene instance without saving the Scene");
            Check(placed.transform.parent == parent.transform, "rename preserves a moved copy's parent");
            Undo.RecordObject(root, "Unrelated edit");
            root.name = "Edited source";
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
            Check(root.name == "Source" && placed.name == "Renamed", "Undo of a later edit does not undo the rename");
            Undo.PerformUndo();
            Check(placed.name == "Generated" && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(placed) == renamed.prefab,
                "Undo restores the external instance name while keeping the renamed Prefab connection");
            Undo.PerformUndo();
            Check(placed == null && File.Exists(renamed.prefab), "Undo after rename removes the original placement and preserves renamed output");
            Undo.PerformRedo();
            placed = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject)
                .Single(g => PrefabUtility.GetNearestPrefabInstanceRoot(g) == g && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) == renamed.prefab);
            Check(placed.name == "Generated" && !placed.activeSelf, "Redo of the placement restores one individual connected to the renamed Prefab");
            Undo.PerformRedo();
            Check(placed.name == "Renamed" && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(placed) == renamed.prefab,
                "Redo restores the renamed external instance without changing its Prefab connection");
            Undo.PerformRedo();
            Check(root.name == "Edited source" && placed.name == "Renamed", "Redo of the later edit preserves the renamed individual");
            placed.transform.SetParent(null, true);
            placed.name = "ManualName";
            EditorSceneManager.SaveScene(scene);
            var renamedAgain = AvatarBrancher.Rename(renamed.folder, "RenamedAgain");
            Check(placed.name == "ManualName", "rename preserves a manually named external instance");
            var second = AvatarBrancher.Create(placed, work, "Second");
            Check(AvatarBrancher.Verify(second.folder).status == "Complete" && second.assets.All(a => !a.destination.Contains("__RenamedAgain__Second")), "placed copy can be copied again with independent references");
            var copyScene = EditorSceneManager.OpenScene(renamedAgain.scene, OpenSceneMode.Additive);
            var copyRoot = copyScene.GetRootGameObjects().Single();
            Call(window, "SetSource", copyRoot);
            Call(window, "SetParent", work);
            Call(window, "SetName", "InOtherCopyScene");
            Call(window, "CreateBranch");
            var nestedReport = (BranchReport)Get(window, "result");
            var nested = Placed(copyScene, nestedReport);
            Check(CopyFolder(nested) == nestedReport.folder && CopyFolder(copyRoot) == renamedAgain.folder, "copy B in copy A Scene is identified independently");
            PrefabUtility.UnpackPrefabInstance(nested, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Check(CopyFolder(nested) == null, "an unpacked copy is not mistaken for the copy owning its Scene");
            EditorSceneManager.CloseScene(copyScene, true);
            root.transform.SetParent(parent.transform, false);
            Call(window, "SetSource", root);
            Call(window, "SetName", "Parented");
            Call(window, "CreateBranch");
            var parentedReport = (BranchReport)Get(window, "result");
            var parented = Placed(scene, parentedReport);
            Check(Vector3.Distance(parented.transform.position, root.transform.position) < .0001f && Quaternion.Angle(parented.transform.rotation, root.transform.rotation) < .001f
                && Vector3.Distance(parented.transform.lossyScale, root.transform.lossyScale) < .0001f, "rotated and scaled parent preserves the placed world Transform");
            var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(parentedReport.prefab);
            var savedScene = EditorSceneManager.OpenScene(parentedReport.scene, OpenSceneMode.Additive);
            var savedRoot = savedScene.GetRootGameObjects().Single();
            Check(Vector3.Distance(savedPrefab.transform.position, root.transform.position) < .0001f && Vector3.Distance(savedRoot.transform.position, root.transform.position) < .0001f
                && Vector3.Distance(savedPrefab.transform.localScale, root.transform.lossyScale) < .0001f && Vector3.Distance(savedRoot.transform.localScale, root.transform.lossyScale) < .0001f,
                "saved Prefab and standalone Scene preserve the same parented world Transform");
            EditorSceneManager.CloseScene(savedScene, true);
            parent.transform.localScale = new Vector3(2, 1, 1);
            root.transform.localRotation = Quaternion.Euler(0, 45, 0);
            Call(window, "SetSource", root);
            Call(window, "SetName", "Sheared");
            Call(window, "CreateBranch");
            Check(Get(window, "error") != null && !Directory.Exists(work + "/Sheared"), "unrepresentable parent shear is rejected before creating files");
            return new { success = true, passed = Passed.ToArray(), work };
        }
        finally
        {
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            foreach (var open in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => s.path.StartsWith(work + "/", StringComparison.Ordinal) && s != active).ToArray())
                EditorSceneManager.CloseScene(open, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }
}
