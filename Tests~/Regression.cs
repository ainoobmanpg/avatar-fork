using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
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
    static string Copy(BranchReport report, string source) { return report.assets.Single(a => a.source == source).destination; }
    static void Save(Object asset) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
    static object Execute()
    {
        string work = "Assets/99_検証用/AvatarBrancher_Regression_" + GUID.Generate().ToString().Substring(0, 8);
        Directory.CreateDirectory(work + "/Source");
        AssetDatabase.Refresh();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            EditorSceneManager.SaveScene(scene, work + "/SourceFixture.unity");
            string shaderPath = work + "/Source/Fixture.shader";
            string includePath = work + "/Source/Fixture.cginc";
            File.WriteAllText(includePath, "float4 BranchFixtureColor() { return float4(1,1,1,1); }\n");
            File.WriteAllText(shaderPath, "Shader \"Hidden/AvatarBranchFixture\" { Properties { _Color(\"Color\", Color)=(1,1,1,1) _MainTex(\"Texture\", 2D)=\"white\"{} } SubShader { Pass { CGPROGRAM\n#pragma vertex vert_img\n#pragma fragment frag\n#include \"UnityCG.cginc\"\n#include \"Fixture.cginc\"\nfloat4 _Color; float4 frag(v2f_img i):SV_Target { return _Color*BranchFixtureColor(); }\nENDCG } } }");
            var image = new Texture2D(2, 2); image.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white }); image.Apply();
            string texturePath = work + "/Source/Texture.png";
            File.WriteAllBytes(texturePath, image.EncodeToPNG());Object.DestroyImmediate(image);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "custom shader and include compile");
            var parent = new Material(shader); parent.SetColor("_Color", Color.red); parent.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            string parentPath = work + "/Source/Parent.mat"; AssetDatabase.CreateAsset(parent, parentPath);
            var child = new Material(parent); child.parent = parent;
            string childPath = work + "/Source/Child.mat"; AssetDatabase.CreateAsset(child, childPath);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };mesh.RecalculateBounds();
            string meshPath = work + "/Source/Mesh.asset";AssetDatabase.CreateAsset(mesh, meshPath);
            var clip = new AnimationClip();
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(MeshRenderer), "m_Materials.Array.data[0]"), new[] { new ObjectReferenceKeyframe { time = 0, value = child }, new ObjectReferenceKeyframe { time = 1, value = parent } });
            string clipPath = work + "/Source/Swap.anim"; AssetDatabase.CreateAsset(clip, clipPath);
            string controllerPath = work + "/Source/Motion.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);var state = controller.layers[0].stateMachine.AddState("Swap");state.motion = clip;Save(controller);
            var fixtureData = state.AddStateMachineBehaviour<BranchFixtureData>();fixtureData.materialPath = parentPath;fixtureData.texturePath = texturePath;
            fixtureData.entries = new[] { new BranchFixtureEntry { materialPath = parentPath } };Save(controller);
            string missingGuid = GUID.Generate().ToString();
            File.WriteAllText(controllerPath, File.ReadAllText(controllerPath).Replace("optionalMaterial: {fileID: 0}", "optionalMaterial: {fileID: 2100000, guid: " + missingGuid + ", type: 2}"));
            AssetDatabase.ImportAsset(controllerPath, ImportAssetOptions.ForceUpdate);
            var unreferenced = new Material(shader);string unreferencedPath = work + "/Source/Unreferenced.mat";AssetDatabase.CreateAsset(unreferenced, unreferencedPath);
            unreferenced.SetColor("_Color", Color.cyan);EditorUtility.SetDirty(unreferenced);var unreferencedFlags = unreferenced.hideFlags;
            var root = new GameObject("Fixture");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;root.AddComponent<MeshRenderer>().sharedMaterial = child;root.AddComponent<Animator>().runtimeAnimatorController = controller;
            Directory.CreateDirectory(work + "/Source/Second");AssetDatabase.Refresh();
            string duplicatePath = work + "/Source/Second/parent.mat";
            AssetDatabase.CreateAsset(new Material(shader) { color = Color.gray }, duplicatePath);
            var second = new GameObject("Keep_Bone__And_Parameter_Name");second.transform.SetParent(root.transform, false);
            second.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(duplicatePath);
            var originalPipeline = root.AddComponent<VRC.Core.PipelineManager>();
            string originalBlueprint = "avtr_" + Guid.NewGuid().ToString();originalPipeline.blueprintId = originalBlueprint;
            root.transform.localScale = Vector3.one * .93f;
            parent.SetColor("_Color", Color.yellow);EditorUtility.SetDirty(parent);
            string parentDiskBefore = File.ReadAllText(parentPath);
            bool sourceDirtyBefore = scene.isDirty;
            var a = AvatarBrancher.Create(root, work + "/Branches", "るるね__赤");
            Check(a.branchName == "るるね__赤" && Path.GetFileNameWithoutExtension(a.prefab) == a.branchName && Path.GetFileNameWithoutExtension(a.scene) == a.branchName && AssetDatabase.LoadAssetAtPath<GameObject>(a.prefab).name == a.branchName, "one Japanese name is used by the folder, Scene, Prefab and Hierarchy root");
            Check(a.assets.All(x => Path.GetFileNameWithoutExtension(x.destination).EndsWith("__" + a.branchName, StringComparison.Ordinal)), "every copied file includes the resolved branch name");
            Check(a.status == "Complete" && AvatarBrancher.Verify(a.folder).status == "Complete", "A has no source asset references");
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(a.prefab).GetComponent<VRC.Core.PipelineManager>().blueprintId == "" && originalPipeline.blueprintId == originalBlueprint, "A clears the upload ID without changing the original upload ID");
            Check(scene.isDirty == sourceDirtyBefore && root.transform.localScale == Vector3.one * .93f, "source Scene state is preserved");
            Check(File.ReadAllText(parentPath) == parentDiskBefore && EditorUtility.IsDirty(parent), "unsaved source material stays unsaved and unchanged on disk");
            Check(unreferenced != null && unreferenced.GetColor("_Color") == Color.cyan && EditorUtility.IsDirty(unreferenced) && unreferenced.hideFlags == unreferencedFlags, "unreferenced unsaved material survives import GC with its original flags");
            var parentA = AssetDatabase.LoadAssetAtPath<Material>(Copy(a, parentPath));
            Check(Copy(a, parentPath).StartsWith(a.folder + "/Materials/Parent__") && Copy(a, duplicatePath).StartsWith(a.folder + "/Materials/parent__") && !string.Equals(Copy(a, parentPath), Copy(a, duplicatePath), StringComparison.OrdinalIgnoreCase), "case-insensitive duplicate material filenames are disambiguated in the Materials folder");
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(a.prefab).transform.Find(second.name) != null, "control and bone object names are preserved");
            var childA = AssetDatabase.LoadAssetAtPath<Material>(Copy(a, childPath));
            Check(childA.parent == parentA && parentA != parent && parentA.GetColor("_Color") == Color.yellow, "variant parent and unsaved settings belong to A");
            Check(childA.shader != shader && !ShaderUtil.ShaderHasError(childA.shader), "shader is separate and compiles");
            Check(File.ReadAllText(Copy(a, shaderPath)).Contains(Copy(a, includePath)), "shader include points to A");
            var swapA = AssetDatabase.LoadAssetAtPath<AnimationClip>(Copy(a, clipPath));
            Check(AnimationUtility.GetObjectReferenceCurve(swapA, AnimationUtility.GetObjectReferenceCurveBindings(swapA).Single()).All(k => AssetDatabase.GetAssetPath(k.value).StartsWith(a.folder + "/")), "animation material switches point to A");
            var controllerA = AssetDatabase.LoadAssetAtPath<AnimatorController>(Copy(a, controllerPath));
            var dataA = (BranchFixtureData)controllerA.layers[0].stateMachine.states.Single().state.behaviours.Single();
            Check(dataA.materialPath == Copy(a, parentPath) && dataA.texturePath == Copy(a, texturePath), "unquoted YAML asset path strings point to A");
            Check(dataA.entries.Single().materialPath == Copy(a, parentPath), "first string field of serialized list entry points to A");
            Check(a.missingSourceReferences.Length > 0 && dataA.optionalMaterial == null && !File.ReadAllText(Copy(a, controllerPath)).Contains(missingGuid), "preexisting missing references remain null without retaining an original GUID");
            Check(parentA.hideFlags == parent.hideFlags, "temporary GC protection flags do not enter copied assets");
            Color childColorA = childA.GetColor("_Color");parent.SetColor("_Color", Color.magenta);
            Check(parentA.GetColor("_Color") == Color.yellow && childA.GetColor("_Color") == childColorA, "editing original parent does not affect A");
            var prefabA = AssetDatabase.LoadAssetAtPath<GameObject>(a.prefab);var liveA = (GameObject)PrefabUtility.InstantiatePrefab(prefabA, scene);
            liveA.transform.localScale = Vector3.one * .77f;parentA.SetColor("_Color", Color.green);Save(parentA);
            string uploadedABlueprint = "avtr_" + Guid.NewGuid().ToString();liveA.GetComponent<VRC.Core.PipelineManager>().blueprintId = uploadedABlueprint;
            var previewB = AvatarBrancher.ResolveDestination(work + "/Branches", "るるね__赤");
            var b = AvatarBrancher.Create(liveA, work + "/Branches", "るるね__赤");
            Check(b.branchName == "るるね__赤 1" && b.folder == previewB.folder && b.prefab == previewB.prefab && b.scene == previewB.scene && AssetDatabase.LoadAssetAtPath<GameObject>(b.prefab).name == b.branchName, "the preview and all outputs use the same collision suffix");
            Check(b.assets.All(x => Path.GetFileNameWithoutExtension(x.destination).EndsWith("__" + b.branchName, StringComparison.Ordinal) && x.originalName == a.assets.Single(y => y.destination == x.source).originalName), "rebranching replaces the ownership name using metadata without splitting double underscores");
            Check(b.assets.Where(x => Path.GetExtension(x.destination) == ".mat").All(x => AssetDatabase.LoadAssetAtPath<Material>(x.destination).name == Path.GetFileNameWithoutExtension(x.destination)), "material display names match their readable filenames after rebranching");
            Check(b.status == "Complete" && AvatarBrancher.Verify(b.folder).status == "Complete", "B has no original or A asset references");
            var parentB = AssetDatabase.LoadAssetAtPath<Material>(Copy(b, AssetDatabase.GetAssetPath(parentA)));
            var childB = AssetDatabase.LoadAssetAtPath<Material>(Copy(b, AssetDatabase.GetAssetPath(childA)));
            var prefabB = AssetDatabase.LoadAssetAtPath<GameObject>(b.prefab);
            Check(prefabB.GetComponent<VRC.Core.PipelineManager>().blueprintId == "" && liveA.GetComponent<VRC.Core.PipelineManager>().blueprintId == uploadedABlueprint, "B clears an ID assigned to A and preserves A's current upload ID");
            var uploadScene = EditorSceneManager.OpenScene(b.scene, OpenSceneMode.Additive);
            var scenePipeline = uploadScene.GetRootGameObjects().Single().GetComponent<VRC.Core.PipelineManager>();
            Check(scenePipeline.blueprintId == "", "saved and reloaded B Scene has no upload ID");
            scenePipeline.blueprintId = originalBlueprint;EditorUtility.SetDirty(scenePipeline);EditorSceneManager.SaveScene(uploadScene);
            Check(AvatarBrancher.Verify(b.folder).status == "Failed", "verification rejects a Blueprint ID stored as a Scene override");
            scenePipeline.blueprintId = "";EditorUtility.SetDirty(scenePipeline);EditorSceneManager.SaveScene(uploadScene);EditorSceneManager.CloseScene(uploadScene, true);
            Check(AvatarBrancher.Verify(b.folder).status == "Complete", "verification accepts the Scene after clearing its upload ID");
            Check(prefabB.transform.localScale == Vector3.one * .77f && parentB.GetColor("_Color") == Color.green, "B captures current Scene and material edits in A");
            Color childColorB = childB.GetColor("_Color");parentA.SetColor("_Color", Color.blue);Save(parentA);
            Check(parentB.GetColor("_Color") == Color.green && childB.GetColor("_Color") == childColorB && childB.parent == parentB, "editing A parent does not affect B");
            parentB.SetColor("_Color", Color.cyan);Save(parentB);
            Check(parentA.GetColor("_Color") == Color.blue && parent.GetColor("_Color") == Color.magenta, "editing B does not affect A or original");
            childB.parent = parent;Save(childB);
            Check(AvatarBrancher.Verify(b.folder).status == "Failed", "verification rejects a deliberately restored original parent reference");
            childB.parent = parentB;Save(childB);
            Check(AvatarBrancher.Verify(b.folder).status == "Complete", "verification passes after repairing the deliberate reference");
            string objPath = work + "/Source/Imported.obj";
            File.WriteAllText(objPath, "o Imported\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");AssetDatabase.ImportAsset(objPath, ImportAssetOptions.ForceSynchronousImport);
            var importedMesh = AssetDatabase.LoadAllAssetsAtPath(objPath).OfType<Mesh>().First();
            root.GetComponent<MeshFilter>().sharedMesh = importedMesh;
            importedMesh.vertices = importedMesh.vertices.Select(v => v + Vector3.one).ToArray();EditorUtility.SetDirty(importedMesh);
            bool dirtyMeshRejected = false;try { AvatarBrancher.Create(root, work + "/Branches", "DirtyImportedMesh"); } catch (InvalidOperationException e) { dirtyMeshRejected = e.Message.Contains("未保存"); }
            Check(dirtyMeshRejected, "unsaved imported mesh edits are rejected instead of silently lost");
            root.GetComponent<MeshFilter>().sharedMesh = mesh;AssetDatabase.ImportAsset(objPath, ImportAssetOptions.ForceUpdate);
            string modelPath = work + "/Source/Embedded.obj";
            File.WriteAllText(work + "/Source/Embedded.mtl", "newmtl Embedded\nKd 0.2 0.4 0.6\n");
            File.WriteAllText(modelPath, "mtllib Embedded.mtl\no Embedded\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl Embedded\nf 1 2 3\n");
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
            var modelImporter = (ModelImporter)AssetImporter.GetAtPath(modelPath);modelImporter.materialLocation = ModelImporterMaterialLocation.InPrefab;modelImporter.SaveAndReimport();
            var embedded = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().First();
            root.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Mesh>().First();root.GetComponent<MeshRenderer>().sharedMaterial = embedded;
            var modelBranch = AvatarBrancher.Create(root, work + "/Branches", "EmbeddedMaterial");
            var extractedMaterial = AssetDatabase.LoadAssetAtPath<GameObject>(modelBranch.prefab).GetComponent<MeshRenderer>().sharedMaterial;
            Check(modelBranch.extractedMaterials.Length > 0 && extractedMaterial != null && AssetDatabase.GetAssetPath(extractedMaterial).StartsWith(modelBranch.folder + "/") && extractedMaterial.color == embedded.color && AvatarBrancher.Verify(modelBranch.folder).status == "Complete", "embedded model material is extracted with its value and references preserved");
            Check(extractedMaterial.hideFlags == HideFlags.None, "extracted model material can be edited as an ordinary material");
            Check(Path.GetFileName(AssetDatabase.GetAssetPath(extractedMaterial)) == "Embedded_Embedded__EmbeddedMaterial.mat" && extractedMaterial.name == "Embedded_Embedded__EmbeddedMaterial", "embedded materials use model and material names instead of numeric IDs");
            var extractedRoot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelBranch.prefab), scene);
            var extractedB = AvatarBrancher.Create(extractedRoot, work + "/Branches", "embedded red");
            Check(Path.GetFileName(Copy(extractedB, AssetDatabase.GetAssetPath(extractedMaterial))) == "Embedded_Embedded__embedded red.mat" && AvatarBrancher.Verify(extractedB.folder).status == "Complete", "embedded material base names survive another branch with an English name containing spaces");
            AssetDatabase.ImportAsset(Copy(modelBranch, modelPath), ImportAssetOptions.ForceUpdate);
            Check(AvatarBrancher.Verify(modelBranch.folder).status == "Complete" && AssetDatabase.LoadAssetAtPath<GameObject>(Copy(modelBranch, modelPath)).GetComponentInChildren<Renderer>().sharedMaterial == extractedMaterial, "model material remapping remains independent after reimport");
            string swappedPath = work + "/Source/Swapped.obj";
            File.WriteAllText(work + "/Source/Swapped.mtl", "newmtl Body\nKd 1 0 0\nnewmtl Clothes\nKd 0 0 1\n");
            File.WriteAllText(swappedPath, "mtllib Swapped.mtl\no Swapped\nv 0 0 0\nv 1 0 0\nv 0 1 0\nv 1 1 0\nusemtl Body\nf 1 2 3\nusemtl Clothes\nf 2 4 3\n");
            AssetDatabase.ImportAsset(swappedPath, ImportAssetOptions.ForceSynchronousImport);
            var body = new Material(shader) { name = "Clothes", color = Color.blue };AssetDatabase.CreateAsset(body, work + "/Source/BodyExternal.mat");
            var clothes = new Material(shader) { name = "Body", color = Color.red };AssetDatabase.CreateAsset(clothes, work + "/Source/ClothesExternal.mat");
            var swappedImporter = (ModelImporter)AssetImporter.GetAtPath(swappedPath);swappedImporter.materialLocation = ModelImporterMaterialLocation.InPrefab;
            swappedImporter.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Body"), body);
            swappedImporter.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Clothes"), clothes);swappedImporter.SaveAndReimport();
            root.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAllAssetsAtPath(swappedPath).OfType<Mesh>().First();root.GetComponent<MeshRenderer>().sharedMaterial = body;
            var swapped = AvatarBrancher.Create(root, work + "/Branches", "Swapped");
            var originalColors = AssetDatabase.LoadAssetAtPath<GameObject>(swappedPath).GetComponentInChildren<Renderer>().sharedMaterials.Select(m => m.color).ToArray();
            var copiedColors = AssetDatabase.LoadAssetAtPath<GameObject>(Copy(swapped, swappedPath)).GetComponentInChildren<Renderer>().sharedMaterials.Select(m => m.color).ToArray();
            Check(originalColors.Length == 2 && originalColors.SequenceEqual(copiedColors), "model identifiers retain external mappings when material asset names are swapped");
            var swappedRoot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(swapped.prefab), scene);
            var swappedB = AvatarBrancher.Create(swappedRoot, work + "/Branches", "SwappedB");
            Check(originalColors.SequenceEqual(AssetDatabase.LoadAssetAtPath<GameObject>(Copy(swappedB, Copy(swapped, swappedPath))).GetComponentInChildren<Renderer>().sharedMaterials.Select(m => m.color)) && AvatarBrancher.Verify(swappedB.folder).status == "Complete", "rebranching preserves external model mappings with renamed material assets");
            root.GetComponent<MeshFilter>().sharedMesh = mesh;root.GetComponent<MeshRenderer>().sharedMaterial = child;
            var textureImporter = AssetImporter.GetAtPath(texturePath);EditorUtility.SetDirty(textureImporter);
            bool dirtyImporterRejected = false;try { AvatarBrancher.Create(root, work + "/Branches", "DirtyImporter"); } catch (InvalidOperationException e) { dirtyImporterRejected = e.Message.Contains("読込設定"); }
            Check(dirtyImporterRejected, "unapplied importer settings are rejected");EditorUtility.ClearDirty(textureImporter);
            var outside = new GameObject("Outside");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(outside, scene);
            var constraint = root.AddComponent<ParentConstraint>();constraint.AddSource(new ConstraintSource { sourceTransform = outside.transform, weight = 1 });
            bool rejected = false;try { AvatarBrancher.Create(root, work + "/Branches", "Invalid"); } catch (InvalidOperationException e) { rejected = e.Message.Contains("アバター外"); }
            Check(rejected && !Directory.Exists(work + "/Branches/Invalid"), "external Scene reference is rejected before copying");
            var result = new { success = true, checks = Passed.ToArray(), work };
            string reportPath = Environment.GetEnvironmentVariable("AVATAR_FORK_TEST_REPORT");
            if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
            return result;
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }
}
