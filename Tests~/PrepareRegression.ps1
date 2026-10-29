param([Parameter(Mandatory=$true)][string]$Project)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath $Project).Path
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt'))) { throw '検証専用のUnityプロジェクトを指定してください。' }
if (Test-Path -LiteralPath (Join-Path $projectRoot 'Temp/UnityLockfile')) { throw '対象のUnityを閉じてから準備してください。' }
$target = Join-Path $projectRoot 'Assets/AvatarForkRegression'
if (Test-Path -LiteralPath $target) { throw '検証コードの配置先が既にあります。上書きしません。' }
$encoding = [System.Text.UTF8Encoding]::new($false, $true)
$null = New-Item -ItemType Directory -Path (Join-Path $target 'Editor')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BranchFixtureData.cs') -Destination $target
foreach ($suite in @('Regression','RenameRegression','RenameSafetyRegression','ReleaseRegression','LocalizationRegression','UiReviewRegression')) {
    $source = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot ($suite + '.cs')), $encoding)
    $source = $source.Replace('class EditorCommand', ('public class AvatarForkTest' + $suite)).Replace('static object Execute()', 'public static object Execute()')
    [System.IO.File]::WriteAllText((Join-Path $target ('Editor/' + $suite + '.cs')), $source, $encoding)
}
$runner = @'
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;
public static class AvatarForkRegressionRunner
{
    [MenuItem("Tools/Avatar Fork Tests/Run regression tests (test project only)")]
    public static void RunAll()
    {
        try
        {
            if (Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s => s.isDirty))
                throw new InvalidOperationException("Save the test project's scenes before running regression tests.");
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/AvatarForkRegression/Empty.unity");
            const string languageKey = "com.ainoobmanpg.avatar-fork.uiLanguage";
            bool hadLanguage = EditorPrefs.HasKey(languageKey);
            string originalLanguage = EditorPrefs.GetString(languageKey);
            var result = new JObject();
            try
            {
                foreach (string language in new[] { "en", "ja" })
                {
                    EditorPrefs.SetString(languageKey, language);
                    var suites = new JObject();
                    result[language] = suites;
                    suites["copy"] = JToken.FromObject(AvatarForkTestRegression.Execute());
                    suites["rename"] = JToken.FromObject(AvatarForkTestRenameRegression.Execute());
                    suites["safety"] = JToken.FromObject(AvatarForkTestRenameSafetyRegression.Execute());
                    suites["release"] = JToken.FromObject(AvatarForkTestReleaseRegression.Execute());
                    suites["localization"] = JToken.FromObject(AvatarForkTestLocalizationRegression.Execute());
                    suites["uiReview"] = JToken.FromObject(AvatarForkTestUiReviewRegression.Execute());
                }
            }
            finally
            {
                if (hadLanguage) EditorPrefs.SetString(languageKey, originalLanguage); else EditorPrefs.DeleteKey(languageKey);
            }
            Directory.CreateDirectory("Library/AvatarFork/Regression");
            File.WriteAllText("Library/AvatarFork/Regression/result.json", result.ToString());
            if (result.Properties().Any(locale => ((JObject)locale.Value).Properties().Any(suite => !(bool)suite.Value["success"]))) throw new Exception("A regression suite failed.");
            Debug.Log("Avatar Fork regression tests passed. Library/AvatarFork/Regression/result.json");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw;
        }
    }
}
'@
[System.IO.File]::WriteAllText((Join-Path $target 'Editor/AvatarForkRegressionRunner.cs'), $runner, $encoding)
[pscustomobject]@{ project=$projectRoot; fixtures=$target; executeMethod='AvatarForkRegressionRunner.RunAll' } | ConvertTo-Json
