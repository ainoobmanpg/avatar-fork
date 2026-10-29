using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;
using VrcAvatarWorkspace.Branching;

class EditorCommand
{
    static readonly List<string> Passed = new List<string>();
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        Passed.Add(name);
    }
    static object Get(object target, string name) { return target.GetType().GetField(name, Instance).GetValue(target); }
    static void Set(object target, string name, object value) { target.GetType().GetField(name, Instance).SetValue(target, value); }
    static void Call(object target, string name, params object[] values) { target.GetType().GetMethod(name, Instance).Invoke(target, values); }
    static Exception Failure(Action action)
    {
        try { action(); }
        catch (TargetInvocationException error) { return error.InnerException; }
        catch (Exception error) { return error; }
        throw new Exception("Expected failure");
    }
    static object Execute()
    {
        Passed.Clear();
        string work = "Assets/99_検証用/AvatarFork_UiReview_" + GUID.Generate().ToString().Substring(0, 8);
        Directory.CreateDirectory(work);
        AssetDatabase.Refresh();
        var active = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var mode = EditorSettings.serializationMode;
        AvatarBranchWindow window = null;
        try
        {
            EditorSceneManager.SaveScene(scene, work + "/Source.unity");
            var root = new GameObject("Source Avatar");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<VRC.Core.PipelineManager>().blueprintId = "avtr_source";
            var child = new GameObject("Reference Owner");
            child.transform.SetParent(root.transform);
            var outside = new GameObject("Outside Target");
            SceneManager.MoveGameObjectToScene(outside, scene);
            var constraint = child.AddComponent<ParentConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = outside.transform, weight = 1 });
            var external = Failure(() => AvatarBrancher.Create(root, work, "External"));
            Check(external.Message.Contains("Source Avatar/Reference Owner") && external.Message.Contains("Parent Constraint")
                && external.Message.Contains("Outside Target") && external.Message.Contains("m_Sources.Array.data[0].sourceTransform"), "external reference identifies object, component, target and property");
            Check(!Directory.Exists(work + "/External") && external.Data["AvatarFork.FailedFolder"] == null, "preflight failure creates no incomplete output");
            window = ScriptableObject.CreateInstance<AvatarBranchWindow>();
            Call(window, "SetSource", root);
            Call(window, "SetParent", work);
            Call(window, "SetName", "PreflightFailure");
            Call(window, "CreateBranch");
            Check(Get(window, "error") != null && Get(window, "failedFolder") == null, "window keeps a preflight error without an output folder");
            Call(window, "SetSource", outside);
            Check(Get(window, "error") == null && !Directory.Exists(work + "/PreflightFailure"), "changing the source clears an obsolete preflight error without creating output");
            UnityEngine.Object.DestroyImmediate(constraint);
            var filter = child.AddComponent<MeshFilter>();
            var unsavedMesh = new Mesh { name = "Unsaved Mesh" };
            filter.sharedMesh = unsavedMesh;
            var unsaved = Failure(() => AvatarBrancher.Create(root, work, "Unsaved"));
            Check(unsaved.Message.Contains("Mesh Filter") && unsaved.Message.Contains("Unsaved Mesh") && unsaved.Message.Contains("m_Mesh"), "unsaved reference identifies the field and asset without prescribing a save operation");
            filter.sharedMesh = null;
            UnityEngine.Object.DestroyImmediate(unsavedMesh);

            foreach (var entry in new[] {
                new[] { "a/b", "/" }, new[] { "a:b", ":" }, new[] { "a?b", "?" }, new[] { ".hidden", "." },
                new[] { "ending.", "." }, new[] { "ending~", "~" }, new[] { "ending.TMP", ".TMP" },
                new[] { "CON", "CON" }, new[] { "lpt1.txt", "lpt1.txt" }, new[] { "cvs", "cvs" } })
            {
                string message = Failure(() => AvatarBrancher.ResolveDestination(work, entry[0])).Message;
                Check(message.Contains(entry[1]), "invalid name identifies the reason: " + entry[0]);
            }
            Check(Failure(() => AvatarBrancher.ResolveDestination(work, "line\nbreak")).Message.Contains("改行")
                || Failure(() => AvatarBrancher.ResolveDestination(work, "line\nbreak")).Message.Contains("control characters"), "control characters have an explicit diagnostic");
            Check(AvatarBrancher.ResolveDestination(work, "るるね 赤").name == "るるね 赤", "ordinary Japanese names and spaces remain valid");

            var enqueue = typeof(AvatarBrancher).GetMethod("Enqueue", Static);
            string missing = work + "/Missing.mat";
            var missingError = Failure(() => enqueue.Invoke(null, new object[] { null, missing, work + "/Destination" }));
            Check(missingError is FileNotFoundException && missingError.Message.Contains(missing), "missing asset path is included in the displayed message");
            var resolveInclude = typeof(AvatarBrancher).GetMethod("ResolveInclude", Static);
            string owner = work + "/Owner.shader";
            foreach (string include in new[] { work + "/Missing.cginc", "RelativeMissing.cginc", "Packages/no-such-package/Missing.hlsl" })
            {
                var error = Failure(() => resolveInclude.Invoke(null, new object[] { owner, include }));
                Check(error is FileNotFoundException && error.Message.Contains(owner) && error.Message.Contains(include), "missing include identifies include and owner: " + include);
            }

            var planType = typeof(AvatarBrancher).GetNestedType("Plan", BindingFlags.NonPublic);
            var plan = Activator.CreateInstance(planType, true);
            var assets = (Dictionary<string, CopiedAsset>)planType.GetField("assets").GetValue(plan);
            foreach (string key in new[] { "AvatarForkUiReview4237", "AvatarForkUiReview31913" })
                assets.Add(work + "/" + key + ".mat", new CopiedAsset { source = work + "/" + key + ".mat", originalName = "Material.mat", nameKey = key, sourceGuid = GUID.Generate().ToString() });
            var assign = typeof(AvatarBrancher).GetMethod("AssignDestinations", Static);
            foreach (string parent in new[] { work + "/Collision", work + "/OtherParent/Collision" })
            {
                var error = Failure(() => assign.Invoke(null, new object[] { plan, new BranchReport { folder = parent, branchName = "Collision" } }));
                Check(error is InvalidOperationException && assets.Keys.All(error.Message.Contains) && error.Message.Contains(parent), "name collision identifies both source assets regardless of parent: " + parent);
            }

            var material = new Material(Shader.Find("Standard"));
            EditorSettings.serializationMode = SerializationMode.Mixed;
            string binary = work + "/Binary.mat";
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new UnityEngine.Object[] { material }, binary, false);
            UnityEngine.Object.DestroyImmediate(material);
            AssetDatabase.ImportAsset(binary, ImportAssetOptions.ForceSynchronousImport);
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(binary);
            Call(window, "SetSource", root);
            Call(window, "SetParent", work);
            Call(window, "SetName", "FailedCopy");
            Call(window, "CreateBranch");
            string failed = (string)Get(window, "failedFolder");
            Check(failed == work + "/FailedCopy" && File.Exists(failed + "/BranchReport.json")
                && JsonUtility.FromJson<BranchReport>(File.ReadAllText(failed + "/BranchReport.json")).status == "Failed", "late failure keeps its actual folder and failed report");
            Check(AvatarBrancher.ResolveDestination(work, "FailedCopy").folder == failed + " 1" && Get(window, "result") == null, "next attempt has a separate numbered destination and no completion");
            Call(window, "SetName", "Retry");
            Check((string)Get(window, "failedFolder") == failed && !string.IsNullOrEmpty((string)Get(window, "error")), "editing inputs preserves the previous incomplete output warning");
            renderer.sharedMaterial = null;
            EditorSettings.serializationMode = mode;
            Call(window, "CreateBranch");
            var copy = (BranchReport)Get(window, "result");
            Check(copy != null && copy.status == "Complete" && Get(window, "failedFolder") == null && Get(window, "error") == null, "successful retry replaces the failure with a verified copy");
            Check(root.GetComponent<VRC.Core.PipelineManager>().blueprintId == "avtr_source", "failed and successful attempts preserve source upload identity");

            string reportPath = copy.folder + "/BranchReport.json";
            string savedReport = File.ReadAllText(reportPath);
            try
            {
                foreach (string condition in new[] { "format", "incomplete", "missingName", "mismatch", "unreadable" })
                {
                    var changed = JsonUtility.FromJson<BranchReport>(savedReport);
                    if (condition == "format") changed.version = 99;
                    else if (condition == "incomplete") changed.status = "Failed";
                    else if (condition == "missingName") changed.branchName = "";
                    else if (condition == "mismatch") changed.folder += "Elsewhere";
                    File.WriteAllText(reportPath, condition == "unreadable" ? "{" : JsonUtility.ToJson(changed));
                    var error = Failure(() => AvatarBrancher.PreviewRename(copy.folder, "Renamed"));
                    string message = error.Message;
                    var catalog = typeof(AvatarBrancher).Assembly.GetType("VrcAvatarWorkspace.Branching.AvatarForkText");
                    string key = condition == "format" ? "UnsupportedReport" : condition == "incomplete" ? "IncompleteCopy"
                        : condition == "missingName" ? "NamingRecord" : condition == "mismatch" ? "RenameRecordMismatch" : "UnreadableReport";
                    string expected = (string)catalog.GetProperty(key, Static).GetValue(null);
                    Check(message.StartsWith(expected, StringComparison.Ordinal) && message.Contains(reportPath), "rename rejection explains the actual report condition: " + condition);
                    Check(error.Data.Contains("AvatarFork.RecoveryRequired") == (condition == "unreadable"), "recovery help is offered only for a report that requires recovery: " + condition);
                    Check(Directory.Exists(copy.folder) && !Directory.Exists(work + "/Renamed"), "report rejection changes no copy files: " + condition);
                }
            }
            finally { File.WriteAllText(reportPath, savedReport); }
            Check(AvatarBrancher.PreviewRename(copy.folder, "Renamed").name == "Renamed", "restoring the original report allows normal rename preview");
            return new { success = true, checks = Passed.ToArray(), work };
        }
        finally
        {
            EditorSettings.serializationMode = mode;
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }
}
