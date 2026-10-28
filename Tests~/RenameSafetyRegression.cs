using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrcAvatarWorkspace.Branching;

class EditorCommand
{
    static object Execute()
    {
        string work = "Assets/99_検証用/AvatarFork_RenameSafety_" + GUID.Generate().ToString().Substring(0, 8);
        Directory.CreateDirectory(work);
        AssetDatabase.Refresh();
        var active = SceneManager.GetActiveScene();
        var source = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Scene unnamed = default;
        try
        {
            var root = new GameObject("Fixture");
            SceneManager.MoveGameObjectToScene(root, source);
            EditorSceneManager.SaveScene(source, work + "/Source.unity");
            var branch = AvatarBrancher.Create(root, work, "Generated");
            var branchScene = EditorSceneManager.OpenScene(branch.scene, OpenSceneMode.Additive);
            branchScene.GetRootGameObjects().Single().name = "手で付けたルート名";
            EditorSceneManager.SaveScene(branchScene);
            var unloaded = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(unloaded, branch.folder + "/Unloaded.unity");
            EditorSceneManager.CloseScene(unloaded, false);
            EditorSceneManager.MoveSceneBefore(branchScene, source);
            var before = EditorSceneManager.GetSceneManagerSetup();
            var renamed = AvatarBrancher.Rename(branch.folder, "Renamed");
            string actualName = SceneManager.GetSceneByPath(renamed.scene).GetRootGameObjects().Single().name;
            var expectedOrder = before.Select(s => s.path.StartsWith(branch.folder + "/", StringComparison.Ordinal)
                ? s.path == branch.scene ? renamed.scene : renamed.folder + s.path.Substring(branch.folder.Length) : s.path).ToArray();
            var actualOrder = EditorSceneManager.GetSceneManagerSetup().Select(s => s.path).ToArray();
            bool manualNamePreserved = actualName == "手で付けたルート名";
            bool sceneOrderPreserved = expectedOrder.SequenceEqual(actualOrder);
            var unloadedAfter = SceneManager.GetSceneByPath(renamed.folder + "/Unloaded.unity");
            bool unloadedHeaderPreserved = unloadedAfter.IsValid() && !unloadedAfter.isLoaded;
            unnamed = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var bytesBefore = File.ReadAllBytes(renamed.folder + "/" + AvatarBrancher.ReportName);
            bool unnamedSceneRejected = false;
            try { AvatarBrancher.Rename(renamed.folder, "RejectUnnamed"); }
            catch (InvalidOperationException) { unnamedSceneRejected = true; }
            unnamedSceneRejected &= unnamed.IsValid() && bytesBefore.SequenceEqual(File.ReadAllBytes(renamed.folder + "/" + AvatarBrancher.ReportName));
            var result = new {success = manualNamePreserved && sceneOrderPreserved && unloadedHeaderPreserved && unnamedSceneRejected, manualNamePreserved, sceneOrderPreserved, unloadedHeaderPreserved, unnamedSceneRejected, actualName, expectedOrder, actualOrder, work};
            string report = Environment.GetEnvironmentVariable("AVATAR_FORK_SAFETY_REPORT");
            if (!string.IsNullOrEmpty(report)) File.WriteAllText(report, Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
            return result;
        }
        finally
        {
            foreach (var scene in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => s.path.StartsWith(work + "/", StringComparison.Ordinal)).ToArray())
                EditorSceneManager.CloseScene(scene, true);
            if (unnamed.IsValid()) EditorSceneManager.CloseScene(unnamed, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }
}
