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

public static class AvatarForkPlacementPlayModeRegression
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Type Placement = typeof(AvatarBrancher).Assembly.GetType("VrcAvatarWorkspace.Branching.BranchScenePlacement");
    static readonly List<string> Passed = new List<string>();
    static IDisposable placement;
    static BranchReport report;
    static GlobalObjectId expectedId;
    static string work;
    static Scene active;
    static bool previousEnabled;
    static EnterPlayModeOptions previousOptions;
    static double deadline;
    static GameObject tracked;
    static Scene target;
    static int moveStage;
    static int moveWait;
    static Exception pendingError;
    static bool skipSceneReload;

    static object Call(string name, params object[] args)
    {
        try { return Placement.GetMethod(name, Fields).Invoke(placement, args); }
        catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        Passed.Add(name);
    }
    static void Rejected(string name, params object[] args)
    {
        Exception caught = null;
        try { Call(name, args); } catch (InvalidOperationException error) { caught = error; }
        Check(caught != null, name + " rejects " + (EditorApplication.isPlaying ? "Play Mode" : "Play Mode transition"));
    }

    [MenuItem("Tools/Avatar Fork Tests/Run placement Play Mode test (test project only)")]
    public static void Run() { Start(false); }

    public static void RunWithoutSceneReload() { Start(true); }

    static void Start(bool withoutSceneReload)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || placement != null)
            throw new InvalidOperationException("Run once from Edit Mode after compilation.");
        if (Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s => s.isDirty))
            throw new InvalidOperationException("Save the test project's scenes before running.");
        Passed.Clear();
        skipSceneReload = withoutSceneReload;
        previousEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        previousOptions = EditorSettings.enterPlayModeOptions;
        active = SceneManager.GetActiveScene();
        work = "Assets/99_検証用/AvatarFork_PlacementPlayMode_" + GUID.Generate().ToString().Substring(0, 8);
        Directory.CreateDirectory(work);
        AssetDatabase.Refresh();
        if (string.IsNullOrEmpty(active.path)) EditorSceneManager.SaveScene(active, work + "/Empty.unity");
        try
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var source = new GameObject("Source");
            SceneManager.MoveGameObjectToScene(source, scene);
            PrefabUtility.SaveAsPrefabAsset(source, work + "/Copy.prefab");
            report = new BranchReport { prefab = work + "/Copy.prefab" };
            EditorSceneManager.SaveScene(scene, work + "/Source.unity");
            placement = (IDisposable)Activator.CreateInstance(Placement, new object[] { source });
            var placed = (GameObject)Call("Add", report, false);
            EditorSceneManager.SaveScene(scene);
            target = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(target, work + "/Target.unity");
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(report.prefab), target);
            tracked = placed;
            Undo.IncrementCurrentGroup();
            Undo.MoveGameObjectToScene(placed, target, "Move tracked copy");
            moveStage = 0;
            moveWait = 2;
            EditorApplication.update += MoveAfterUpdate;
            EditorApplication.update += Timeout;
            deadline = EditorApplication.timeSinceStartup + 90;
        }
        catch (Exception error) { Finish(error); }
    }

    static void MoveAfterUpdate()
    {
        if (--moveWait > 0) return;
        moveWait = 2;
        try
        {
            var sourceScene = SceneManager.GetSceneByPath(work + "/Source.unity");
            Check((GameObject)Call("Resolve") == tracked && tracked.scene == (moveStage == 1 ? sourceScene : target),
                "Scene move stage " + moveStage + " retains the same individual after Editor updates");
            if (moveStage++ == 0) Undo.PerformUndo();
            else if (moveStage == 2) Undo.PerformRedo();
            else
            {
                EditorApplication.update -= MoveAfterUpdate;
                EditorSceneManager.SaveScene(sourceScene);
                EditorSceneManager.SaveScene(target);
                expectedId = GlobalObjectId.GetGlobalObjectIdSlow(tracked);
                EditorSceneManager.CloseScene(target, true);
                target = EditorSceneManager.OpenScene(work + "/Target.unity", OpenSceneMode.Additive);
                tracked = (GameObject)Call("Resolve");
                Check(tracked != null && GlobalObjectId.GetGlobalObjectIdSlow(tracked).Equals(expectedId) && target.rootCount == 2,
                    "Scene move Undo and Redo followed by reload retain the individual among matching Prefabs");
                if (skipSceneReload)
                {
                    target = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    tracked = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(report.prefab), target);
                    Call("Bind", tracked, report);
                    expectedId = GlobalObjectId.GetGlobalObjectIdSlow(tracked);
                    Check(expectedId.assetGUID.Equals(default(GUID)), "the no-reload test tracks an individual in an unsaved Scene");
                }
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | (skipSceneReload ? EnterPlayModeOptions.DisableSceneReload : EnterPlayModeOptions.None);
                EditorApplication.playModeStateChanged += Changed;
                EditorApplication.isPlaying = true;
                Rejected("Add", report, false);
                Rejected("Add", report, true);
                Rejected("Bind", tracked, report);
            }
        }
        catch (Exception error) { Finish(error); }
    }

    static void Changed(PlayModeStateChange state)
    {
        try
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                var runtime = target.GetRootGameObjects().First();
                int before = runtime.scene.rootCount;
                Rejected("Add", report, false);
                Rejected("Add", report, true);
                Rejected("Bind", runtime, report);
                Check(Call("Resolve") == null && Placement.GetField("placedId", Fields).GetValue(placement).Equals(expectedId), "Play Mode does not replace the Edit Mode identity");
                Check(runtime.scene.rootCount == before, "Play Mode rejection does not create another individual");
                EditorApplication.isPlaying = false;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                Check(Placement.GetField("placedId", Fields).GetValue(placement).Equals(expectedId), "Play Mode exit preserves the recorded Edit Mode identity before resolution");
                var placed = (GameObject)Call("Resolve");
                Check(placed != null && GlobalObjectId.GetGlobalObjectIdSlow(placed).Equals(expectedId), "leaving Play Mode resolves the original Edit Mode individual");
                int count = placed.scene.rootCount;
                Check((GameObject)Call("Add", report, false) == placed && placed.scene.rootCount == count, "Edit Mode selection retains the same individual count");
                Finish(null);
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Rejected("Add", report, false);
                Rejected("Add", report, true);
                Rejected("Bind", tracked, report);
                Check(Call("Resolve") == null, "Play Mode exit keeps tracking suspended until Edit Mode resumes");
            }
        }
        catch (Exception error) { Finish(error); }
    }

    static void Timeout()
    {
        if (EditorApplication.timeSinceStartup > deadline) Finish(new TimeoutException("Play Mode regression timed out."));
    }

    static void Finish(Exception error)
    {
        EditorApplication.update -= MoveAfterUpdate;
        EditorApplication.playModeStateChanged -= Changed;
        EditorApplication.update -= Timeout;
        if (!Application.isBatchMode && EditorApplication.isPlayingOrWillChangePlaymode)
        {
            pendingError = error;
            EditorApplication.playModeStateChanged += FinishAfterPlay;
            EditorApplication.isPlaying = false;
            return;
        }
        EditorSettings.enterPlayModeOptionsEnabled = previousEnabled;
        EditorSettings.enterPlayModeOptions = previousOptions;
        placement?.Dispose();
        placement = null;
        Directory.CreateDirectory("Library/AvatarFork/Regression");
        File.WriteAllText("Library/AvatarFork/Regression/placement-play-mode" + (skipSceneReload ? "-without-scene-reload" : "") + ".json", Newtonsoft.Json.JsonConvert.SerializeObject(
            new { success = error == null, passed = Passed.ToArray(), error = error?.ToString(), work, domainReload = false, sceneReload = !skipSceneReload }, Newtonsoft.Json.Formatting.Indented));
        if (error != null) Debug.LogException(error);
        if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
        else if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (target.IsValid() && target.isLoaded) EditorSceneManager.CloseScene(target, true);
            foreach (var path in new[] { "/Target.unity", "/Source.unity" })
            {
                var scene = SceneManager.GetSceneByPath(work + path);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }

    static void FinishAfterPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= FinishAfterPlay;
        Finish(pendingError);
        pendingError = null;
    }
}
