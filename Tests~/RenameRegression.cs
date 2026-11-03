using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrcAvatarWorkspace.Branching;
using Object = UnityEngine.Object;

class EditorCommand
{
    static readonly List<string> Passed = new List<string>();
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        Passed.Add(name);
    }
    static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
    static Dictionary<string, string> Hashes(string folder) { return Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, Hash); }
    static bool Unchanged(Dictionary<string, string> before) { return before.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value); }
    static bool Reject(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
    static object Execute()
    {
        Passed.Clear();
        string work = "Assets/99_検証用/AvatarFork_Rename_" + GUID.Generate().ToString().Substring(0, 8);
        Directory.CreateDirectory(work + "/Source");
        AssetDatabase.Refresh();
        var active = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            EditorSceneManager.SaveScene(scene, work + "/Source.unity");
            string shaderPath = work + "/Source/Fixture.shader";
            File.WriteAllText(work + "/Source/Fixture.cginc", "float4 FixtureColor() {return float4(1,1,1,1);}");
            File.WriteAllText(shaderPath, "Shader \"Hidden/AvatarForkRenameFixture\" { Properties { _Color(\"Color\",Color)=(1,1,1,1) _MainTex(\"Texture\",2D)=\"white\"{} } SubShader { Pass { CGPROGRAM\n#pragma vertex vert_img\n#pragma fragment frag\n#include \"UnityCG.cginc\"\n#include \"Fixture.cginc\"\nfloat4 _Color; float4 frag(v2f_img i):SV_Target {return FixtureColor()*_Color;}\nENDCG } } }");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(shaderPath));
            string materialPath = work + "/Source/元__名前.mat";
            AssetDatabase.CreateAsset(material, materialPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(work + "/Source/FX.controller");
            var state = controller.layers[0].stateMachine.AddState("状態は維持");
            var clip = new AnimationClip();AssetDatabase.CreateAsset(clip, work + "/Source/Move.anim");clip.name = "Clip内部は維持";EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);state.motion = clip;
            var data = state.AddStateMachineBehaviour<BranchFixtureData>();
            data.materialPath = materialPath;
            data.entries = new[] { new BranchFixtureEntry { materialPath = materialPath } };
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
            var root = new GameObject("Fixture");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            root.AddComponent<VRC.Core.PipelineManager>();
            root.AddComponent<MeshRenderer>().sharedMaterial = material;
            root.AddComponent<Animator>().runtimeAnimatorController = controller;
            var bone = new GameObject("骨__変えない");bone.transform.SetParent(root.transform, false);
            var a = AvatarBrancher.Create(root, work + "/Branches", "旧名__赤");
            var other = AvatarBrancher.Create(root, work + "/Branches", "Other");
            var others = Hashes(other.folder);
            var sources = Hashes(work + "/Source");
            var guids = a.assets.ToDictionary(x => x.nameKey, x => x.destinationGuid);
            EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<Shader>(a.assets.Single(x => x.source == shaderPath).destination));
            string originalMaterial = a.assets.Single(x => x.source == materialPath).destination;
            var dirty = AssetDatabase.LoadAssetAtPath<Material>(originalMaterial);
            dirty.color = Color.cyan;EditorUtility.SetDirty(dirty);
            Check(Reject(() => AvatarBrancher.Rename(a.folder, "変更")) && EditorUtility.IsDirty(dirty), "unsaved owned material is rejected and stays unsaved");
            AssetDatabase.SaveAssetIfDirty(dirty);
            var beforeConflict = Hashes(a.folder);
            Check(Reject(() => AvatarBrancher.PreviewRename(a.folder, "Other")) && Unchanged(beforeConflict), "sibling name collision leaves every file unchanged");
            string custom = a.assets.Single(x => x.source == materialPath).destination;
            string manual = Path.GetDirectoryName(custom).Replace('\\', '/') + "/手で付けた名前.mat";
            Check(AssetDatabase.MoveAsset(custom, manual) == "", "manual asset move succeeds before rename");
            var customMaterial = AssetDatabase.LoadAssetAtPath<Material>(manual);
            customMaterial.name = "手で付けた表示名";EditorUtility.SetDirty(customMaterial);AssetDatabase.SaveAssetIfDirty(customMaterial);
            string extra = a.folder + "/追加.txt";File.WriteAllText(extra, "追加ファイル");AssetDatabase.ImportAsset(extra);
            var extraScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var extraRoot = new GameObject("追加Sceneは維持");SceneManager.MoveGameObjectToScene(extraRoot, extraScene);extraRoot.AddComponent<TextMesh>().text = custom;
            var folderText = new GameObject("FolderPath");SceneManager.MoveGameObjectToScene(folderText, extraScene);folderText.AddComponent<TextMesh>().text = a.folder + "/Materials";
            EditorSceneManager.SaveScene(extraScene, a.folder + "/追加Scene.unity");EditorSceneManager.CloseScene(extraScene, true);SceneManager.SetActiveScene(scene);
            material.color = Color.magenta;EditorUtility.SetDirty(material);
            var first = AvatarBrancher.Rename(a.folder, "英語 blue");
            Check(first.branchName == "英語 blue" && first.assets.All(x => x.destinationGuid == guids[x.nameKey]), "rename preserves recorded GUIDs and original name keys");
            Check(AvatarBrancher.Verify(first.folder).status == "Complete", "renamed copy retains independent references");
            var preserved = first.assets.Single(x => x.source == materialPath);
            Check(Path.GetFileName(preserved.destination) == "手で付けた名前.mat" && AssetDatabase.LoadAssetAtPath<Material>(preserved.destination).name == "手で付けた表示名", "manual filename and display name are preserved");
            Check(File.ReadAllText(first.folder + "/追加.txt") == "追加ファイル", "unrecorded file follows folder with its name and bytes unchanged");
            Check(EditorUtility.IsDirty(material) && material.color == Color.magenta && Unchanged(sources) && Unchanged(others), "unrelated unsaved material, originals and sibling copy are preserved");
            var fx = AssetDatabase.LoadAssetAtPath<AnimatorController>(first.assets.Single(x => x.source.EndsWith("FX.controller")).destination);
            var renamedData = (BranchFixtureData)fx.layers[0].stateMachine.states.Single().state.behaviours.Single();
            Check(renamedData.materialPath == preserved.destination && renamedData.entries[0].materialPath == preserved.destination, "escaped Japanese paths and nested list strings follow rename");
            Check(fx.layers[0].stateMachine.states.Single().state.name == "状態は維持" && AssetDatabase.LoadAssetAtPath<GameObject>(first.prefab).transform.GetChild(0).name == "骨__変えない", "Animator states and child bones retain their names");
            Check(fx.layers[0].stateMachine.states.Single().state.motion.name == "Clip内部は維持", "animation clip internal name survives file renaming");
            var copiedShader = AssetDatabase.LoadAssetAtPath<Shader>(first.assets.Single(x => x.source == shaderPath).destination);
            Check(copiedShader.name.Contains("/英語 blue/") && !ShaderUtil.ShaderHasError(copiedShader), "renamed custom shader and include compile");
            Check(EditorUtility.IsDirty(material) && Unchanged(sources), "successive property and shader checks do not save unrelated material");
            EditorUtility.ClearDirty(material);AssetDatabase.ImportAsset(materialPath, ImportAssetOptions.ForceUpdate);
            var extraAfter = EditorSceneManager.OpenScene(first.folder + "/追加Scene.unity", OpenSceneMode.Additive);
            Check(extraAfter.GetRootGameObjects().Single(g => g.name == "追加Sceneは維持").GetComponent<TextMesh>().text == preserved.destination
                && extraAfter.GetRootGameObjects().Single(g => g.name == "FolderPath").GetComponent<TextMesh>().text == first.folder + "/Materials", "additional Scene paths and folder strings follow recorded GUIDs after a manual move");
            EditorSceneManager.CloseScene(extraAfter, true);
            var renamedScene = EditorSceneManager.OpenScene(first.scene, OpenSceneMode.Additive);
            Check(renamedScene.GetRootGameObjects().Single().name == first.branchName, "saved Scene root matches new name");
            renamedScene.GetRootGameObjects().Single().transform.position += Vector3.one;EditorSceneManager.MarkSceneDirty(renamedScene);
            Check(Reject(() => AvatarBrancher.Rename(first.folder, "拒否")) && renamedScene.isDirty, "unsaved Scene is rejected without saving or discarding changes");
            EditorSceneManager.SaveScene(renamedScene);
            var second = AvatarBrancher.Rename(first.folder, "Case");
            Check(SceneManager.GetSceneByPath(second.scene).isLoaded && !SceneManager.GetSceneByPath(second.scene).isDirty, "clean open Scene is reopened after rename");
            EditorSceneManager.CloseScene(SceneManager.GetSceneByPath(second.scene), true);
            var third = AvatarBrancher.Rename(second.folder, "CASE");
            Check(third.folder.EndsWith("/CASE", StringComparison.Ordinal) && AvatarBrancher.Verify(third.folder).status == "Complete", "case-only rename preserves GUIDs and references");
            var prefab = PrefabUtility.LoadPrefabContents(third.prefab);
            string id = "avtr_" + Guid.NewGuid();
            try { prefab.GetComponent<VRC.Core.PipelineManager>().blueprintId = id;PrefabUtility.SaveAsPrefabAsset(prefab, third.prefab); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            var fourth = AvatarBrancher.Rename(third.folder, "登録後");
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(fourth.prefab).GetComponent<VRC.Core.PipelineManager>().blueprintId == id, "rename preserves registered upload identity");
            var copyRoot = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fourth.prefab), scene) as GameObject;
            var next = AvatarBrancher.Create(copyRoot, work + "/Branches", "次の複製");
            Check(next.assets.All(x => !Path.GetFileName(x.destination).Contains("__登録後__")) && next.assets.Where(x => x.source.EndsWith(".mat")).All(x => x.originalName == "元__名前.mat"), "rebranching retains original names after repeated rename");
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(next.prefab).GetComponent<VRC.Core.PipelineManager>().blueprintId == "", "new fork still clears registered ID after rename");
            string rollbackShader = fourth.assets.Single(x => x.source == shaderPath).destination;
            string shaderText = File.ReadAllText(rollbackShader);
            File.WriteAllText(rollbackShader, shaderText.Replace("VrcAvatarBranch/登録後/", "ManualShader/登録後/"));AssetDatabase.ImportAsset(rollbackShader);
            var rollbackBefore = Hashes(fourth.folder);
            material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);material.color = Color.yellow;EditorUtility.SetDirty(material);
            Check(Reject(() => AvatarBrancher.Rename(fourth.folder, "失敗予定")) && Unchanged(rollbackBefore) && !Directory.Exists(work + "/Branches/失敗予定"), "failure after moving files restores original paths and bytes");
            Check(EditorUtility.IsDirty(material) && material.color == Color.yellow && Unchanged(sources), "rollback preserves unrelated unsaved values without saving them");
            EditorUtility.ClearDirty(material);
            File.WriteAllText(rollbackShader, shaderText);AssetDatabase.ImportAsset(rollbackShader);
            Check(Unchanged(sources) && Unchanged(others), "original and sibling files remain unchanged after failure recovery");
            string external = work + "/External";
            Directory.CreateDirectory(external);
            var pixels = new Texture2D(2, 2);
            File.WriteAllBytes(external + "/Outside.png", pixels.EncodeToPNG());
            Object.DestroyImmediate(pixels);
            File.WriteAllText(external + "/Outside.cginc", "float4 FixtureColor() {return float4(1,1,1,1);}");
            File.WriteAllText(external + "/Outside.custom", "#include \"Outside.cginc\"\n");
            File.WriteAllText(external + "/Outside.mtl", "newmtl Outside\nKd 1 1 1\nmap_Kd Outside.png\n");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var outsideTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(external + "/Outside.png");
            var outsideMaterial = new Material(Shader.Find("Unlit/Texture")) { mainTexture = outsideTexture };
            AssetDatabase.CreateAsset(outsideMaterial, external + "/Outside.mat");
            customMaterial = AssetDatabase.LoadAssetAtPath<Material>(fourth.assets.Single(x => x.source == materialPath).destination);
            customMaterial.mainTexture = outsideTexture;
            EditorUtility.SetDirty(customMaterial);AssetDatabase.SaveAssetIfDirty(customMaterial);
            fx = AssetDatabase.LoadAssetAtPath<AnimatorController>(fourth.assets.Single(x => x.source.EndsWith("FX.controller")).destination);
            renamedData = (BranchFixtureData)fx.layers[0].stateMachine.states.Single().state.behaviours.Single();
            renamedData.materialPath = external + "/Outside.mat";
            renamedData.texturePath = external + "/Outside.png";
            renamedData.entries[0].materialPath = external + "/Outside.mat";
            renamedData.optionalMaterial = outsideMaterial;
            EditorUtility.SetDirty(renamedData);EditorUtility.SetDirty(fx);AssetDatabase.SaveAssetIfDirty(fx);
            prefab = PrefabUtility.LoadPrefabContents(fourth.prefab);
            try { prefab.GetComponent<MeshRenderer>().sharedMaterials = new[] { customMaterial, outsideMaterial };PrefabUtility.SaveAsPrefabAsset(prefab, fourth.prefab); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            string copiedInclude = fourth.assets.Single(x => x.source.EndsWith("Fixture.cginc")).destination;
            File.WriteAllText(rollbackShader, File.ReadAllText(rollbackShader).Replace(copiedInclude, external + "/Outside.custom"));
            string externalPaths = "path: " + external + "/Outside.mat\npath: \"" + external + "/Outside.png\"\n";
            File.WriteAllText(fourth.folder + "/References.txt", externalPaths);
            File.WriteAllText(fourth.folder + "/Outside.obj", "mtllib ../../External/Outside.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl Outside\nf 1 2 3\n");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Check(AvatarBrancher.Verify(fourth.folder).problems.Any(p => p.Contains(external + "/Outside.png")), "copy verification still reports external texture references");
            string missingReference = fourth.folder + "/MissingReference.mat";
            File.WriteAllText(missingReference, File.ReadAllText(AssetDatabase.GetAssetPath(customMaterial))
                .Replace(AssetDatabase.AssetPathToGUID(external + "/Outside.png"), "ffffffffffffffffffffffffffffffff"));
            AssetDatabase.ImportAsset(missingReference, ImportAssetOptions.ForceSynchronousImport);
            var missingBefore = Hashes(fourth.folder);
            Check(Reject(() => AvatarBrancher.Rename(fourth.folder, "MissingReference")) && Unchanged(missingBefore)
                && !Directory.Exists(work + "/Branches/MissingReference"), "missing GUID still rejects rename without changing files");
            AssetDatabase.DeleteAsset(missingReference);
            var outsideBefore = Hashes(external);
            var renamedExternal = AvatarBrancher.Rename(fourth.folder, "外部参照を維持");
            var externalRenderer = AssetDatabase.LoadAssetAtPath<GameObject>(renamedExternal.prefab).GetComponent<MeshRenderer>();
            Check(externalRenderer.sharedMaterials[0].mainTexture == outsideTexture && externalRenderer.sharedMaterials[1] == outsideMaterial,
                "rename preserves external texture and material references");
            var externalFx = AssetDatabase.LoadAssetAtPath<AnimatorController>(renamedExternal.assets.Single(x => x.source.EndsWith("FX.controller")).destination);
            var externalData = (BranchFixtureData)externalFx.layers[0].stateMachine.states.Single().state.behaviours.Single();
            Check(externalData.materialPath == external + "/Outside.mat" && externalData.texturePath == external + "/Outside.png"
                && externalData.entries[0].materialPath == external + "/Outside.mat" && externalData.optionalMaterial == outsideMaterial
                && File.ReadAllText(renamedExternal.folder + "/References.txt") == externalPaths, "external serialized references and quoted and plain paths stay unchanged");
            var externalShader = AssetDatabase.LoadAssetAtPath<Shader>(renamedExternal.assets.Single(x => x.source == shaderPath).destination);
            Check(File.ReadAllText(AssetDatabase.GetAssetPath(externalShader)).Contains(external + "/Outside.custom")
                && !ShaderUtil.ShaderHasError(externalShader), "external nested shader include stays unchanged and compiles after rename");
            Check(File.ReadAllText(renamedExternal.folder + "/Outside.obj").Contains("../../External/Outside.mtl")
                && AssetDatabase.LoadAssetAtPath<GameObject>(renamedExternal.folder + "/Outside.obj") != null, "external relative model material stays unchanged after rename");
            Check(renamedExternal.assets.All(x => x.destinationGuid == guids[x.nameKey])
                && AssetDatabase.LoadAssetAtPath<GameObject>(renamedExternal.prefab).GetComponent<VRC.Core.PipelineManager>().blueprintId == id
                && Unchanged(outsideBefore) && Unchanged(sources) && Unchanged(others), "rename with external references preserves GUIDs, Blueprint ID, external files, originals and sibling copy");
            var result = new { success = true, checks = Passed.ToArray(), work };
            string reportPath = Environment.GetEnvironmentVariable("AVATAR_FORK_RENAME_REPORT");
            if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
            return result;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }
}
