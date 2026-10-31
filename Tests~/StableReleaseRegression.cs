using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrcAvatarWorkspace.Branching;

class EditorCommand
{
    static readonly List<string> Passed = new List<string>();
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); Passed.Add(name); }
    static string Hash(string path) { using (var sha = System.Security.Cryptography.SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
    static bool Recovery(Action action)
    {
        try { action(); return false; }
        catch (InvalidOperationException e) { return e.Data.Contains("AvatarFork.RecoveryRequired"); }
    }
    static object Execute()
    {
        Passed.Clear();
        string work = "Assets/99_検証用/AvatarFork_Stable_" + GUID.Generate().ToString().Substring(0, 8);
        Directory.CreateDirectory(work);
        AssetDatabase.Refresh();
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        string record = "Library/AvatarFork/Rename/Stable_" + GUID.Generate();
        try
        {
            EditorSceneManager.SetActiveScene(scene);
            var root = new GameObject("StableFixture");
            BranchReport nested = null;
            var renderer = root.AddComponent<MeshRenderer>();
            EditorSceneManager.SaveScene(scene, work + "/Source.unity");
            foreach (string kind in new[] { "relative", "absolute", "leaf", "header", "unsupported", "unsupportedPragma", "angle", "sjisLeaf", "sjisInc", "macro" })
            {
                string folder = work + "/" + kind;
                Directory.CreateDirectory(folder);
                string outer = folder + "/outer" + (kind == "header" ? ".h" : kind.StartsWith("unsupported", StringComparison.Ordinal) || kind == "sjisLeaf" ? ".custom" : ".inc");
                string inner = folder + "/inner.inc";
                File.WriteAllText(inner, "float4 FixtureColor() { return float4(1,0,0,1); }\n");
                File.WriteAllText(outer, kind == "leaf" ? File.ReadAllText(inner) : kind == "angle" ? "#include <inner.inc>\n" : "#include \"" + (kind == "absolute" ? inner : "inner.inc") + "\"\n");
                if (kind.StartsWith("sjis", StringComparison.Ordinal))
                    File.WriteAllBytes(outer, new byte[] { 47, 47, 130, 160, 10 }.Concat(System.Text.Encoding.ASCII.GetBytes(File.ReadAllText(inner))).ToArray());
                if (kind == "macro") File.WriteAllText(outer, "#define NEXT_INCLUDE \"inner.inc\"\n#include NEXT_INCLUDE\n");
                if (kind == "unsupportedPragma") File.WriteAllText(outer, "#include_with_pragmas \"inner.inc\"\n");
                string shaderPath = folder + "/Fixture.shader";
                File.WriteAllText(shaderPath, "Shader \"Hidden/Stable" + kind + "\" { SubShader { Pass { CGPROGRAM\n#pragma vertex vert_img\n#pragma fragment frag\n#include \"UnityCG.cginc\"\n#include \"" + Path.GetFileName(outer) + "\"\nfloat4 frag(v2f_img i):SV_Target { return FixtureColor(); }\nENDCG } } }");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                if (kind == "angle" && ShaderUtil.ShaderHasError(shader))
                {
                    Check(ShaderUtil.GetShaderMessages(shader).Length > 0, "angle include rejected by this Unity compiler before copying");
                    continue;
                }
                Check(shader != null && (kind == "macro" || !ShaderUtil.ShaderHasError(shader)), kind + " source Shader fixture imports");
                string materialPath = folder + "/Fixture.mat";
                AssetDatabase.CreateAsset(new Material(shader), materialPath);
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (kind.StartsWith("unsupported", StringComparison.Ordinal) || kind == "sjisInc" || kind == "macro")
                {
                    bool rejected = false;
                    try { AvatarBrancher.Create(root, work, "Copy" + kind); }
                    catch (InvalidOperationException e) { rejected = e.Message.Contains(outer); }
                    Check(rejected, kind + " unsupported text is refused with its file location");
                    var failed = JsonUtility.FromJson<BranchReport>(File.ReadAllText(work + "/Copy" + kind + "/" + AvatarBrancher.ReportName));
                    Check(failed.status == "Failed", "unsupported include never creates a Complete report");
                    continue;
                }
                var report = AvatarBrancher.Create(root, work, "Copy" + kind);
                var copiedShader = AssetDatabase.LoadAssetAtPath<Shader>(report.assets.Single(a => a.source == shaderPath).destination);
                Check(report.status == "Complete" && !ShaderUtil.ShaderHasError(copiedShader), kind + " copy Shader compiles");
                Check(AvatarBrancher.Verify(report.folder).status == "Complete", kind + " copy passes reference validation");
                if (kind == "leaf")
                {
                    string copiedOuter = report.assets.Single(a => a.source == outer).destination;
                    byte[] bytes = File.ReadAllBytes(copiedOuter);
                    File.WriteAllBytes(copiedOuter, new byte[] { 47, 47, 130, 160, 10 }.Concat(bytes).ToArray());
                    var invalid = AvatarBrancher.Verify(report.folder);
                    Check(invalid.status == "Failed" && invalid.problems.Any(p => p.Contains(copiedOuter)), "legacy non-UTF8 inc returns Failed with the file path");
                    File.WriteAllBytes(copiedOuter, bytes);
                }
                else if (kind == "sjisLeaf")
                    Check(File.ReadAllBytes(outer).SequenceEqual(File.ReadAllBytes(report.assets.Single(a => a.source == outer).destination)), "non-UTF8 terminal custom include retains its bytes");
                else
                {
                    string copiedOuter = report.assets.Single(a => a.source == outer).destination;
                    string copiedInner = report.assets.Single(a => a.source == inner).destination;
                    Check(File.ReadAllText(copiedOuter).Contains(copiedInner), kind + " nested include points into the copy");
                    string text = File.ReadAllText(copiedOuter);
                    File.WriteAllText(copiedOuter, "#include \"" + inner + "\"\n");
                    Check(AvatarBrancher.Verify(report.folder).status != "Complete", kind + " original include is detected in existing copies");
                    File.WriteAllText(copiedOuter, "#include \"../../../" + outer.Substring("Assets/".Length) + "\"\n");
                    Check(AvatarBrancher.Verify(report.folder).status != "Complete", kind + " unresolved or external relative include is detected");
                    File.WriteAllText(copiedOuter, text);
                    report = AvatarBrancher.Rename(report.folder, "Renamed" + kind);
                    var renamedShader = AssetDatabase.LoadAssetAtPath<Shader>(report.assets.Single(a => a.source == shaderPath).destination);
                    Check(AvatarBrancher.Verify(report.folder).status == "Complete" && !ShaderUtil.ShaderHasError(renamedShader), kind + " nested Shader remains valid after rename");
                    string renamedOuter = report.assets.Single(a => a.source == outer).destination;
                    Check(File.ReadAllText(renamedOuter).Contains(report.folder + "/Shaders/"), kind + " renamed include points to the new folder");
                    if (kind == "relative") nested = report;
                }
            }
            renderer.sharedMaterial = null;
            EditorSceneManager.SaveScene(scene);
            var branch = AvatarBrancher.Create(root, work, "Interrupted");
            string temporary = work + "/AvatarForkRename_Stable";
            var preview = AvatarBrancher.PreviewRename(branch.folder, "Destination");
            Directory.CreateDirectory(record);
            File.WriteAllText(record + "/operation.json", JsonUtility.ToJson(preview));
            File.WriteAllText(record + "/moves.jsonl", "{\"source\":\"" + branch.folder + "\",\"destination\":\"" + temporary + "\",\"completed\":false}\n");
            Check(AssetDatabase.MoveAsset(branch.folder, temporary) == "", "prepare interrupted temporary folder");
            Check(Recovery(() => AvatarBrancher.PreviewRename(temporary, "Retry")), "temporary path offers recovery even without completed move line");
            Check(Recovery(() => AvatarBrancher.Create(root, work, "Interrupted")) && !Directory.Exists(branch.folder), "Create cannot reuse an unresolved old folder");
            Check(Recovery(() => AvatarBrancher.Create(root, work, "Destination")) && !Directory.Exists(preview.destination), "Create cannot reuse an unresolved destination");
            Check(Recovery(() => AvatarBrancher.Create(root, work, "INTERRUPTED")), "recovery checks are case insensitive");
            var interruptedInstance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(temporary + "/" + Path.GetFileName(branch.prefab)), scene);
            Check(Recovery(() => AvatarBrancher.Create(interruptedInstance, work, "FromInterrupted")) && !Directory.Exists(work + "/FromInterrupted"), "interrupted Prefab cannot become a new copy source");
            UnityEngine.Object.DestroyImmediate(interruptedInstance);
            var unrelated = AvatarBrancher.Create(root, work, "Unrelated");
            Check(unrelated.status == "Complete", "unrelated copies remain available");
            Check(Recovery(() => AvatarBrancher.PreviewRename(unrelated.folder, "Interrupted")), "another rename cannot reuse the unresolved folder");
            File.AppendAllText(record + "/moves.jsonl", "{\"source\":");
            Check(Recovery(() => AvatarBrancher.Create(root, work, "CorruptJournal")), "truncated move journal requires recovery");
            File.WriteAllText(record + "/restored.txt", "Restored");
            Check(AvatarBrancher.PreviewRename(unrelated.folder, "Allowed").name == "Allowed", "resolved records do not block later work");
            EditorSceneManager.OpenScene(nested.scene, OpenSceneMode.Single);
            var rollbackExternal = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(rollbackExternal, work + "/RollbackExternal.unity");
            EditorSceneManager.CloseScene(rollbackExternal, false);
            string shaderFile = nested.assets.Single(a => a.source.EndsWith("relative/Fixture.shader", StringComparison.Ordinal)).destination;
            string shaderText = File.ReadAllText(shaderFile);
            File.WriteAllText(shaderFile, shaderText.Replace("VrcAvatarBranch/" + nested.branchName + "/", "ManualShader/"));
            AssetDatabase.ImportAsset(shaderFile);
            var hashes = Directory.GetFiles(nested.folder, "*", SearchOption.AllDirectories).Concat(new[] { nested.folder + ".meta" }).ToDictionary(p => p, Hash);
            var beforeSetup = EditorSceneManager.GetSceneManagerSetup();
            bool rolledBack = false;
            try { AvatarBrancher.Rename(nested.folder, "ExpectedRollback"); }
            catch (InvalidOperationException) { rolledBack = true; }
            Check(rolledBack && !Directory.Exists(work + "/ExpectedRollback"), "rename failure with an unloaded external Scene rolls back");
            Check(hashes.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value), "Scene-only rollback restores all file and meta bytes");
            Check(beforeSetup.Select(s => s.path + "|" + s.isLoaded + "|" + s.isActive).SequenceEqual(EditorSceneManager.GetSceneManagerSetup().Select(s => s.path + "|" + s.isLoaded + "|" + s.isActive)), "Scene-only rollback restores the full loaded and unloaded Scene setup");
            File.WriteAllText(shaderFile, shaderText);
            AssetDatabase.ImportAsset(shaderFile);
            EditorSceneManager.OpenScene(unrelated.scene, OpenSceneMode.Single);
            var unloaded = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(unloaded, work + "/External.unity");
            EditorSceneManager.CloseScene(unloaded, false);
            var renamed = AvatarBrancher.Rename(unrelated.folder, "WithUnloaded");
            var after = EditorSceneManager.GetSceneManagerSetup();
            Check(renamed.status == "Complete" && AvatarBrancher.Verify(renamed.folder).status == "Complete", "rename succeeds with only a copied Scene loaded");
            Check(after.Length == 2 && after[0].path == renamed.scene && after[0].isLoaded && after[0].isActive
                && after[1].path == work + "/External.unity" && !after[1].isLoaded, "rename preserves the unloaded external Scene header and active Scene");
            return new { success = true, checks = Passed.ToArray(), work };
        }
        finally
        {
            if (Directory.Exists(record)) Directory.Delete(record, true);
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }
}
