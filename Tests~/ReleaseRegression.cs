using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrcAvatarWorkspace.Branching;

class EditorCommand
{
    static readonly List<string> Passed = new List<string>();
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
    static object Execute()
    {
        string work = "Assets/99_検証用/AvatarFork_Release_" + GUID.Generate().ToString().Substring(0, 8);
        Directory.CreateDirectory(work);
        AssetDatabase.Refresh();
        var active = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var serialization = EditorSettings.serializationMode;
        string record = "Library/AvatarFork/Rename/ReleaseFixture_" + GUID.Generate();
        try
        {
            EditorSceneManager.SaveScene(scene, work + "/Source.unity");
            var root = new GameObject("ReleaseFixture");
            SceneManager.MoveGameObjectToScene(root, scene);
            foreach (string name in new[] { ".hidden", "Copy~", "CVS", "copy.tmp", "a/b", "CON", "a\"b" })
                Check(Reject(() => AvatarBrancher.ResolveDestination(work, name)), "reject ignored or invalid name: " + name);
            foreach (string parent in new[] { work + "/.hidden", work + "/Ignored~", work + "/../Other" })
                Check(Reject(() => AvatarBrancher.ResolveDestination(parent, "Valid")), "reject ignored or relative parent: " + parent);
            var branch = AvatarBrancher.Create(root, work, "Generated");
            Check(Reject(() => AvatarBrancher.ResolveDestination(branch.folder + "/Child", "Nested")), "reject nested independent copies before output");
            Check(!Directory.Exists(branch.folder + "/Child"), "nested rejection creates no directory");
            foreach (string name in new[] { ".hidden", "Copy~", "CVS", "copy.tmp" })
                Check(Reject(() => AvatarBrancher.PreviewRename(branch.folder, name)), "reject ignored rename: " + name);
            Directory.CreateDirectory(record);
            File.WriteAllText(record + "/operation.json", JsonUtility.ToJson(new BranchRenamePreview { folder = branch.folder, destination = work + "/Interrupted" }));
            Check(Reject(() => AvatarBrancher.Rename(branch.folder, "Blocked")), "reject incomplete operation before another rename");
            Check(!Directory.Exists(work + "/Blocked") && AvatarBrancher.Verify(branch.folder).status == "Complete", "incomplete operation rejection leaves copy unchanged");
            File.WriteAllText(record + "/restored.txt", "Restored");
            Check(AvatarBrancher.PreviewRename(branch.folder, "Allowed").name == "Allowed", "restored operation does not block later rename");
            File.Delete(record + "/restored.txt");
            File.WriteAllText(record + "/operation.json", "{");
            bool corruptRejected = false;
            try { AvatarBrancher.PreviewRename(branch.folder, "BlockedCorrupt"); }
            catch (InvalidOperationException e) { corruptRejected = e.Message.Contains("中断記録") && e.Message.Contains("operation.json"); }
            Check(corruptRejected, "reject corrupt recovery records with their location");
            File.WriteAllText(record + "/restored.txt", "Restored");
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AvatarBrancher).Assembly);
            if (package != null)
            {
                string include = "Packages/" + package.name + "/README.md";
                var resolve = typeof(AvatarBrancher).GetMethod("ResolveInclude", BindingFlags.Static | BindingFlags.NonPublic);
                Check(AssetDatabase.AssetPathToGUID(include) != "" && (string)resolve.Invoke(null, new object[] { work + "/Owner.shader", include }) == include,
                    "resolve package include in local-package installation");
            }
            var material = new Material(Shader.Find("Standard"));
            EditorSettings.serializationMode = SerializationMode.Mixed;
            string binary = work + "/Binary.mat";
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new UnityEngine.Object[] { material }, binary, false);
            UnityEngine.Object.DestroyImmediate(material);
            AssetDatabase.ImportAsset(binary, ImportAssetOptions.ForceSynchronousImport);
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(binary);
            bool binaryRejected = false;
            try { AvatarBrancher.Create(root, work, "BinaryCopy"); }
            catch (InvalidOperationException e) { binaryRejected = e.Message.Contains("バイナリ"); }
            Check(binaryRejected, "reject binary Unity assets");
            Check(!Directory.GetFiles(work + "/BinaryCopy", "*.mat", SearchOption.AllDirectories).Any(), "binary rejection copies no material with original references");
            return new { success = true, checks = Passed.ToArray(), work };
        }
        finally
        {
            EditorSettings.serializationMode = serialization;
            if (Directory.Exists(record)) Directory.Delete(record, true);
            EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }
}
