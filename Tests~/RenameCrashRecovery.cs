using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrcAvatarWorkspace.Branching;
using Newtonsoft.Json.Linq;

public static class AvatarForkRenameCrashRecovery
{
    public static void CheckInterrupted()
    {
        string current = File.ReadAllText("Library/AvatarFork/current-folder.txt");
        var passed = new List<string>();
        Action<bool, string> check = (condition, label) => { if (!condition) throw new Exception("FAILED: " + label); passed.Add(label); };
        bool rejected = false;
        try { AvatarBrancher.PreviewRename(current, "Retry"); }
        catch (InvalidOperationException e) { rejected = e.Data.Contains("AvatarFork.RecoveryRequired"); }
        check(rejected, "restarted Editor offers recovery for the temporary folder without a completed move line");
        var scene = EditorSceneManager.OpenScene("Assets/CrashFixture/Source.unity", OpenSceneMode.Single);
        var source = scene.GetRootGameObjects().Single(g => g.name == "Source");
        rejected = false;
        try { AvatarBrancher.Create(source, "Assets", "CrashCopy"); }
        catch (InvalidOperationException e) { rejected = e.Data.Contains("AvatarFork.RecoveryRequired"); }
        check(rejected && !Directory.Exists("Assets/CrashCopy"), "restarted Editor cannot reuse the old folder");
        var window = AvatarBranchWindow.Open(source);
        try
        {
            typeof(AvatarBranchWindow).GetField("branchName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(window, "CrashCopy");
            typeof(AvatarBranchWindow).GetMethod("CreateBranch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(window, null);
            check((bool)typeof(AvatarBranchWindow).GetField("recoveryRequired", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(window), "creation window retains the recovery instructions state");
        }
        finally { window.Close(); }
        File.WriteAllText("Library/AvatarFork/interrupted-verified.json", new JObject { ["success"] = true, ["checks"] = JArray.FromObject(passed) }.ToString());
    }
    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
    public static void Start()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Directory.CreateDirectory("Assets/CrashFixture");
        AssetDatabase.Refresh();
        var source = new GameObject("Source");
        source.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
        source.AddComponent<VRC.Core.PipelineManager>().blueprintId = "avtr_source";
        var child = new GameObject("KeepBoneName");
        child.transform.SetParent(source.transform, false);
        var material = new Material(Shader.Find("Standard"));
        AssetDatabase.CreateAsset(material, "Assets/CrashFixture/Source.mat");
        child.AddComponent<MeshRenderer>().sharedMaterial = material;
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/CrashFixture/Source.unity");
        var report = AvatarBrancher.Create(source, "Assets", "CrashCopy");
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(report.prefab));
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        var files = new JObject();
        foreach (string path in Directory.GetFiles(report.folder, "*", SearchOption.AllDirectories).Concat(new[] { report.folder + ".meta" }))
            files[path.Replace('\\', '/')] = Hash(path);
        var guids = new JObject();
        foreach (string path in Directory.GetFiles(report.folder, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".meta")))
            guids[path.Replace('\\', '/')] = AssetDatabase.AssetPathToGUID(path);
        var baseline = new JObject { ["folder"] = report.folder, ["prefab"] = report.prefab, ["files"] = files, ["guids"] = guids,
            ["sourceSceneHash"] = Hash("Assets/CrashFixture/Source.unity"), ["sourceMaterialHash"] = Hash("Assets/CrashFixture/Source.mat") };
        Directory.CreateDirectory("Library/AvatarFork");
        File.WriteAllText("Library/AvatarFork/crash-baseline.json", baseline.ToString());
        AvatarBrancher.Rename(report.folder, "RenamedCopy");
        throw new Exception("The requested crash point was not reached.");
    }

    public static void Verify()
    {
        var baseline = JObject.Parse(File.ReadAllText("Library/AvatarFork/crash-baseline.json"));
        var operation = Directory.GetDirectories("Library/AvatarFork/Rename").Single();
        bool complete = File.Exists(operation + "/complete.txt");
        string folder = complete ? "Assets/RenamedCopy" : "Assets/CrashCopy";
        var passed = new List<string>();
        Action<bool, string> check = (condition, label) => { if (!condition) throw new Exception("FAILED: " + label); passed.Add(label); };
        check(AvatarBrancher.Verify(folder).status == "Complete", "restored or completed copy passes its reference and Blueprint checks");
        check(Hash("Assets/CrashFixture/Source.unity") == (string)baseline["sourceSceneHash"], "the external Scene file retains its saved state");
        check(Hash("Assets/CrashFixture/Source.mat") == (string)baseline["sourceMaterialHash"], "the original material file is unchanged");
        if (!complete)
        {
            check(((JObject)baseline["files"]).Properties().All(p => File.Exists(p.Name) && Hash(p.Name) == (string)p.Value), "all restored files and meta files match their original SHA-256");
            check(((JObject)baseline["guids"]).Properties().All(p => AssetDatabase.AssetPathToGUID(p.Name) == (string)p.Value), "all restored GUIDs match their original assets");
        }
        else
        {
            var completed = JObject.Parse(File.ReadAllText("Library/AvatarFork/crash-completed-files.json"));
            check(completed.Properties().All(p => File.Exists(p.Name) && Hash(p.Name) == (string)p.Value), "completed files and meta files remain unchanged after restart");
            check(!Directory.Exists("Assets/CrashCopy"), "completed rename retains only its new asset folder");
        }
        var scene = EditorSceneManager.OpenScene("Assets/CrashFixture/Source.unity", OpenSceneMode.Single);
        var copy = scene.GetRootGameObjects().Single(g => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g).StartsWith(folder + "/", StringComparison.Ordinal));
        check(copy.GetComponent<VRC.Core.PipelineManager>().blueprintId == "" && copy.transform.GetChild(0).name == "KeepBoneName", "the external Scene resolves the copy with its Blueprint ID cleared and bone name preserved");
        check(copy.GetComponentsInChildren<Component>(true).All(c => c != null) && copy.GetComponentInChildren<MeshRenderer>(true).sharedMaterial != null,
            "the restored Scene contains no missing component or copied material");
        var guids = Directory.GetFiles("Assets", "*.meta", SearchOption.AllDirectories).Select(p => File.ReadLines(p).FirstOrDefault(l => l.StartsWith("guid: "))).Where(g => g != null).ToArray();
        check(guids.Distinct().Count() == guids.Length, "Assets contains no duplicate GUID");
        File.WriteAllText("Library/AvatarFork/crash-verified.json", new JObject { ["success"] = true, ["complete"] = complete,
            ["passed"] = JArray.FromObject(passed), ["externalCopyName"] = copy.name }.ToString());
    }
}
