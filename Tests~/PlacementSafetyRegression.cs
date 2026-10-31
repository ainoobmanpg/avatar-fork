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
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Type Placement = typeof(AvatarBrancher).Assembly.GetType("VrcAvatarWorkspace.Branching.BranchScenePlacement");
    static object Get(object target, string name) { return Placement.GetField(name, Fields).GetValue(target); }
    static void Set(object target, string name, object value) { Placement.GetField(name, Fields).SetValue(target, value); }
    static object Call(object target, string name, params object[] args)
    {
        try { return Placement.GetMethod(name, Fields).Invoke(target, args); }
        catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    static bool HasPlaced(object target) { return (bool)Placement.GetProperty("HasPlaced").GetValue(target); }
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        Passed.Add(name);
    }
    static GameObject Add(object placement, BranchReport report, bool another = false) { return (GameObject)Call(placement, "Add", report, another); }
    static object Execute()
    {
        Passed.Clear();
        var skipped = new List<string>();
        var hook = Placement.GetField("TestStep", BindingFlags.Static | BindingFlags.NonPublic);
        string work = "Assets/99_検証用/AvatarFork_PlacementSafety_" + GUID.Generate().ToString().Substring(0, 8);
        Directory.CreateDirectory(work);
        AssetDatabase.Refresh();
        var active = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(active.path)) EditorSceneManager.SaveScene(active, work + "/Empty.unity");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Scene target = default(Scene);
        object placement = null;
        try
        {
            var source = new GameObject("Source");
            SceneManager.MoveGameObjectToScene(source, scene);
            var child = new GameObject("KeepChildName");
            child.transform.SetParent(source.transform, false);
            child.AddComponent<BoxCollider>().size = new Vector3(2, 3, 4);
            var grandchild = new GameObject("KeepGrandchildName");
            grandchild.transform.SetParent(child.transform, false);
            grandchild.transform.localPosition = new Vector3(1, 2, 3);
            var prefab = PrefabUtility.SaveAsPrefabAsset(source, work + "/Copy.prefab");
            var report = new BranchReport { prefab = work + "/Copy.prefab" };
            EditorSceneManager.SaveScene(scene, work + "/Source.unity");
            target = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(target, work + "/Target.unity");
            placement = Activator.CreateInstance(Placement, new object[] { source });
            var placed = Add(placement, report);
            EditorSceneManager.SaveScene(scene);
            var id = GlobalObjectId.GetGlobalObjectIdSlow(placed);
            var other = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var unrelated = new GameObject("Unrelated");
            SceneManager.MoveGameObjectToScene(unrelated, scene);
            foreach (var candidate in new[] { unrelated, other, prefab })
            {
                Set(placement, "placed", null);
                Set(placement, "instanceId", candidate.GetInstanceID());
                Set(placement, "placedId", id);
                Check((GameObject)Call(placement, "Resolve") == placed && Get(placement, "placedId").Equals(id),
                    "stale instance ID rejects " + candidate.name + " and resolves the recorded individual");
            }
            Set(placement, "placed", null);
            Set(placement, "placedPrefabGuid", GUID.Generate().ToString());
            Check((GameObject)Call(placement, "Resolve") == null && Get(placement, "placedId").Equals(id), "matching object ID with a different Prefab GUID remains unresolved without overwriting identity");
            Call(placement, "Bind", placed, report);
            Undo.IncrementCurrentGroup();
            Undo.MoveGameObjectToScene(placed, target, "Move copy");
            Check((GameObject)Call(placement, "Resolve") == placed && placed.scene == target, "immediate Scene move keeps the live individual");
            Undo.PerformUndo();
            Check((GameObject)Call(placement, "Resolve") == placed && placed.scene == scene, "immediate move Undo keeps the same individual");
            Undo.PerformRedo();
            Check((GameObject)Call(placement, "Resolve") == placed && placed.scene == target, "immediate move Redo keeps the same individual");
            EditorSceneManager.SaveScene(scene);
            EditorSceneManager.SaveScene(target);
            var movedId = GlobalObjectId.GetGlobalObjectIdSlow(placed);
            EditorSceneManager.CloseScene(target, true);
            Set(placement, "instanceId", other.GetInstanceID());
            Check((GameObject)Call(placement, "Resolve") == null && Get(placement, "placedId").Equals(movedId), "closed Scene rejects another individual without overwriting identity");
            target = EditorSceneManager.OpenScene(work + "/Target.unity", OpenSceneMode.Additive);
            placed = (GameObject)Call(placement, "Resolve");
            Check(placed != null && placed.scene == target && GlobalObjectId.GetGlobalObjectIdSlow(placed).Equals(movedId), "reopened Scene restores the moved individual");
            var unsaved = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var unsavedOther = (GameObject)PrefabUtility.InstantiatePrefab(prefab, unsaved);
            var unsavedId = GlobalObjectId.GetGlobalObjectIdSlow(unsavedOther);
            Set(placement, "placed", null);
            Set(placement, "instanceId", unsavedOther.GetInstanceID());
            Set(placement, "placedId", unsavedId);
            Check((GameObject)Call(placement, "Resolve") == null, "an unsaved Scene ID is insufficient for automatic adoption");
            Call(placement, "Bind", placed, report);
            EditorSceneManager.CloseScene(unsaved, true);
            if (hook == null) skipped.Add("fault injection requires the AVATAR_FORK_TESTS scripting define symbol");
            else
            {
                foreach (bool another in new[] { false, true })
                foreach (string step in new[] { "instantiated", "creationUndo", "hierarchyUndo", "tracking", "tracked", "collapse" })
                {
                    using (var trial = (IDisposable)Activator.CreateInstance(Placement, new object[] { source }))
                    {
                        var previous = another ? Add(trial, report) : null;
                        var previousId = Get(trial, "placedId");
                        var previousPrefab = Get(trial, "placedPrefabGuid");
                        int previousInstance = (int)Get(trial, "instanceId");
                        var before = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject.GetInstanceID()).OrderBy(n => n).ToArray();
                        Undo.IncrementCurrentGroup();
                        Undo.RecordObject(source, "Earlier edit");
                        source.name = "Earlier edit";
                        Undo.FlushUndoRecordObjects();
                        Undo.IncrementCurrentGroup();
                        var injected = new InvalidOperationException("Injected " + step);
                        Exception caught = null;
                        hook.SetValue(null, (Action<string>)(point => { if (point == step) throw injected; }));
                        try { Add(trial, report, another); }
                        catch (Exception error) { caught = error; }
                        finally { hook.SetValue(null, null); }
                        string label = (another ? "additional " : "initial ") + step;
                        Check(ReferenceEquals(caught, injected), label + " preserves the original exception");
                        Check(before.SequenceEqual(scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject.GetInstanceID()).OrderBy(n => n)), label + " leaves no generated individual or hierarchy fragments");
                        Check(HasPlaced(trial) == another && (GameObject)Get(trial, "placed") == previous && Get(trial, "placedId").Equals(previousId)
                            && (int)Get(trial, "instanceId") == previousInstance && Equals(Get(trial, "placedPrefabGuid"), previousPrefab), label + " restores the prior tracking state");
                        Undo.PerformUndo();
                        Check(source.name == "Source", label + " preserves the earlier Undo operation");
                        Undo.PerformRedo();
                        Check(source.name == "Earlier edit", label + " preserves the earlier Redo operation");
                        source.name = "Source";
                        var retry = Add(trial, report, another);
                        Check(retry != null && (GameObject)Call(trial, "Resolve") == retry && retry.transform.GetChild(0).name == "KeepChildName"
                            && retry.transform.GetChild(0).GetComponent<BoxCollider>().size == new Vector3(2, 3, 4)
                            && retry.transform.GetChild(0).GetChild(0).name == "KeepGrandchildName", label + " allows retry with the complete hierarchy and components");
                    }
                }
                using (var trial = (IDisposable)Activator.CreateInstance(Placement, new object[] { source }))
                {
                    int before = scene.rootCount;
                    int hierarchyCount = scene.GetRootGameObjects().Sum(g => g.GetComponentsInChildren<Transform>(true).Length);
                    var injected = new InvalidOperationException("Original placement failure");
                    Exception caught = null;
                    hook.SetValue(null, (Action<string>)(point => {
                        if (point == "tracking") throw injected;
                        if (point == "rollback") throw new InvalidOperationException("Undo rollback failed");
                    }));
                    try { Add(trial, report); }
                    catch (Exception error) { caught = error; }
                    finally { hook.SetValue(null, null); }
                    Check(ReferenceEquals(caught, injected) && scene.rootCount == before && !HasPlaced(trial), "Undo rollback failure still destroys the generated individual and retains the original error");
                    Undo.PerformUndo();
                    Undo.PerformRedo();
                    Check(scene.rootCount == before && hierarchyCount == scene.GetRootGameObjects().Sum(g => g.GetComponentsInChildren<Transform>(true).Length),
                        "Undo and Redo after rollback failure do not resurrect the failed placement or its children");
                }
            }
            return new { success = true, passed = Passed.ToArray(), skipped = skipped.ToArray(), work };
        }
        finally
        {
            if (hook != null) hook.SetValue(null, null);
            if (placement != null) ((IDisposable)placement).Dispose();
            if (target.IsValid() && target.isLoaded) EditorSceneManager.CloseScene(target, true);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }
}
