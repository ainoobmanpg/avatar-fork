# 更新・削除と導入方式の切り替え

[基本の導入手順](../README.md#導入) · [困ったときの対処](installation-and-recovery.md)

変更する前に制作プロジェクト全体を別の場所へバックアップしてください。バックアップを制作中のプロジェクトと同じフォルダーに重ねないでください。

このツールで作ったScene・Prefab・素材と`BranchReport.json`は、更新・削除の対象ではありません。ツール本体を置く`Assets/AvatarFork`には、アバターや自作素材を入れないでください。

## ALCOM / VCC版を更新・削除する

1. 対象プロジェクトのUnityを閉じます。
2. ALCOM / VCCのパッケージ管理で、Avatar Forkを更新または削除します。
3. Unityでプロジェクトを開き、Consoleにコンパイルエラーがないことを確認します。更新した場合は、試験用コピーで複製・名前変更と見た目を確認します。

## unitypackage版を更新・削除する

上書きだけで更新すると、古いファイルが残ることがあります。次の順でツール本体を入れ替えてください。

1. `Assets/AvatarFork`がツール本体だけのフォルダーであることを確認します。
2. Unityを閉じ、`Assets/AvatarFork`と隣の`AvatarFork.meta`をプロジェクト外へ保全してから、プロジェクト内のツール本体を取り除きます。
3. 更新する場合はUnityを開き、[Releases](https://github.com/ainoobmanpg/avatar-fork/releases)の新しいunitypackageを読み込みます。削除だけなら、読み込みは不要です。
4. Unityが閉じている場合は開き直し、Consoleにコンパイルエラーがないことを確認します。更新した場合は、試験用コピーで複製・名前変更と見た目を確認します。

ツールの版を戻しても、作ったアバターや素材が更新前の状態へ戻ることはありません。生成データも戻したい場合は、プロジェクトのバックアップを使ってください。

## 導入方式を切り替える

二つの方式は同時に導入しないでください。旧`Assets/Editor/AvatarBranching`とも併用しないでください。

### unitypackage版からALCOM / VCC版へ

1. 上の[unitypackage版の削除手順](#unitypackage版を更新削除する)で、ツール本体を保全して取り除きます。
2. Unityを閉じたまま、[ALCOM / VCCの導入手順](../README.md#alcom--vccから導入する推奨)に従って追加します。

### ALCOM / VCC版からunitypackage版へ

1. Unityを閉じ、ALCOM / VCCのパッケージ管理からAvatar Forkを削除します。
2. Unityを開き、[unitypackageの導入手順](../README.md#unitypackageから導入する)で読み込みます。

どちらの切り替えでも、作ったアバターや素材は削除しません。旧フォルダーを自動で削除する仕組みはありません。開発用の外部フォルダー参照を使っている場合は、[開発者向けの削除手順](development.md#外部フォルダー参照を削除する)を確認してください。

## ツールを削除した後

Avatar ForkはEditor用なので、作ったアバターを使うために残しておく必要はありません。Scene・Prefab・素材と`BranchReport.json`は残してください。SDK・MA・Shaderなど、アバターが共通で使うプログラムは引き続き必要です。

削除後はHierarchyのAvatar Forkの操作が消え、Consoleにエラーがないことを確認してください。復元用の`Library/AvatarFork`を整理する場合は、[復元手順](installation-and-recovery.md#改名の途中でunityが終了した場合)も確認してください。
