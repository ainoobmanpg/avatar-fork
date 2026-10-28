param([Parameter(Mandatory=$true)][string]$Project)
$ErrorActionPreference = 'Stop'
$resolvedProject = (Resolve-Path -LiteralPath $Project).Path
if (-not (Test-Path -LiteralPath (Join-Path $resolvedProject 'ProjectSettings/ProjectVersion.txt'))) { throw 'Unityプロジェクトを指定してください。' }
if (Test-Path -LiteralPath (Join-Path $resolvedProject 'Temp/UnityLockfile')) { throw '対象のUnityを閉じてから登録してください。' }
if (Test-Path -LiteralPath (Join-Path $resolvedProject 'Packages/com.ainoobmanpg.avatar-fork/package.json')) { throw 'VPM版が既に導入されています。導入方法の切り替え手順を確認してください。' }
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$legacy = Join-Path $resolvedProject 'Assets/Editor/AvatarBranching'
if (Test-Path -LiteralPath $legacy) { throw '旧Assets版が残っています。内容を確認して取り除いてから登録してください。' }
if (Test-Path -LiteralPath (Join-Path $resolvedProject 'Assets/AvatarFork/Editor/AvatarFork.Editor.asmdef')) { throw 'unitypackage版が残っています。二重導入を避けるため、導入方法の切り替え手順を確認してください。' }
$manifestPath = Join-Path $resolvedProject 'Packages/manifest.json'
$encoding = [System.Text.UTF8Encoding]::new($false, $true)
$original = [System.IO.File]::ReadAllText($manifestPath, $encoding)
$manifest = $original | ConvertFrom-Json
if ($null -eq $manifest.dependencies -or $manifest.dependencies -isnot [pscustomobject]) { throw 'manifest.jsonのdependenciesが不正です。変更は行いません。' }
$manifest.dependencies | Add-Member -NotePropertyName 'com.ainoobmanpg.avatar-fork' -NotePropertyValue ('file:' + $packageRoot.Replace('\','/')) -Force
$text = $manifest | ConvertTo-Json -Depth 100
$operationId = [guid]::NewGuid().ToString('N')
$temporary = $manifestPath + '.' + $operationId + '.tmp'
$backupFolder = Join-Path $resolvedProject 'Library/AvatarFork/Install'
$backup = Join-Path $backupFolder ($operationId + '.manifest.json')
try {
    [System.IO.File]::WriteAllText($temporary, $text + [Environment]::NewLine, $encoding)
    $null = [System.IO.File]::ReadAllText($temporary, $encoding) | ConvertFrom-Json
    if ([System.IO.File]::ReadAllText($manifestPath, $encoding) -cne $original) { throw 'manifest.jsonが別の処理で変更されました。変更は行いません。' }
    $null = [System.IO.Directory]::CreateDirectory($backupFolder)
    [System.IO.File]::Replace($temporary, $manifestPath, $backup)
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
[pscustomobject]@{ project=$resolvedProject; package=$packageRoot; backup=$backup; action='registered-local-package' } | ConvertTo-Json
