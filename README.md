# Avatar Fork

改変中のアバターを、元とは別に編集できるコピーにするUnity Editor拡張です。現在の見た目やギミックの設定を引き継いだScene・Prefab・素材一式を作ります。作ったコピーから、さらにコピーすることもできます。

**v0.1.0-beta.5 ベータ版**です。使う前に、制作プロジェクト全体をバックアップしてください。Windows・Unity 2022.3.22f1で検証しています。Mac・Linuxと他のUnity版は未検証で、すべてのアバター・ギミックの動作を保証するものではありません。

## 導入

### ALCOM / VCCから導入する（推奨）

1. 対象プロジェクトのUnityを閉じます。
2. ALCOMまたはVRChat Creator Companionに、次のVPMリポジトリURLを追加します。

   ```text
   https://raw.githubusercontent.com/ainoobmanpg/avatar-fork/main/vpm/index.json
   ```

3. 対象プロジェクトのパッケージ管理で「Avatar Fork」を追加します。一覧に出ない場合は、プレリリース版を表示する設定を有効にしてください。

### unitypackageから導入する

1. [Releases](https://github.com/ainoobmanpg/avatar-fork/releases)から`AvatarFork-<版>.unitypackage`をダウンロードします。
2. Unityの`Assets > Import Package > Custom Package…`で読み込みます。ツールは`Assets/AvatarFork`に入ります。

**ALCOM / VCC版とunitypackage版は同時に導入しないでください。** すでに別の方式で入れている場合や、更新・削除する場合は、[更新・削除と導入方式の切り替え](Documentation~/updating-and-removing.md)を確認してください。

## 使い方

画面は最初は英語です。上部の「Language / 言語」で日本語に切り替えられます。

### アバターを複製する

名前のあるSceneで使います。複製元のSceneや素材に未保存の変更があっても、対応する変更はコピーへ引き継ぎ、元のファイルは保存し直しません。[引き継げる範囲と制約](Documentation~/copy-details.md#未保存の変更と停止する条件)も確認してください。

1. Hierarchyのアバター、またはScene名を右クリックし、「Duplicate Avatar…（アバターを複製…）」を選びます。
2. 「Source avatar（複製元）」が目的のアバターか確認します。空なら、Hierarchyからアバターをこの欄へドラッグします。
3. 「Copy name（複製名）」を入力し、「Destination（作成先）」を確認します。既定の保存先は`Assets/複製名`です。
4. 「Create Copy（複製する）」を押します。
5. 元アバターと同じSceneのHierarchy末尾にコピーが追加され、選択されます。見た目とギミックを確認してください。「Select in Hierarchy（Hierarchyで選択）」で再び選べます。

Sceneは自動保存しません。元の未保存の編集を残したままコピーを追加します。Undoで追加だけを取り消せますが、生成した素材・Prefab・Sceneは残ります。

Hierarchyで選択するだけでは、開いている画面の複製元は変わりません。同じ名前のフォルダーがあれば番号を付けて作り、既存のコピーには上書きしません。

### コピーの名前を変える

対象のScene・素材・インポート設定に変更があれば、保存するか元に戻してから実行してください。

1. 作ったコピーのSceneかアバターをHierarchyで右クリックし、「Rename Copy…（コピーの名前を変更…）」を選びます。Projectでコピーのフォルダーを右クリックしても開けます。
2. 新しい名前を入力し、「Preview Changes（変更内容を確認）」を押します。
3. 変更内容を確認し、「Rename Copy（この内容で名前を変更する）」を押します。

アバターのルート・Scene・Prefab・フォルダーと、自動で付けた素材名のコピー名部分をまとめて変更します。ボーンやAnimatorの制御名、手で付けた素材名は維持します。[名前変更の範囲と制約](Documentation~/copy-details.md#コピーの名前変更)に詳細があります。

## 使う前に知っておくこと

- 複製先のBlueprint ID（VRChatのアップロード先ID）は空になります。新規アバターとしてアップロードする状態です。
- 名前変更ではBlueprint IDを維持します。アップロード済みなら、その後のアップロードも同じアバターの更新になります。
- SDK・MA・Packages内のShader、スクリプトなどは複製元と共通で使います。変更・更新が他のアバターにも影響することがあります。[コピーするもの・共通で使うもの](Documentation~/copy-details.md#コピーするものと共通で使うもの)を確認してください。
- 複製に失敗した出力は完成品として使わないでください。名前変更の途中でUnityが強制終了した場合は、[困ったときの対処](Documentation~/installation-and-recovery.md)で復元するまで再実行しないでください。

## ライセンス

ツール本体と付属文書は[MITライセンス](LICENSE)です。作者は[sakuchichi_vrc](https://x.com/sakuchichi_vrc)です。ツール本体や改造版を配布するときは、作者名・Xリンクを含む著作権表示とMITライセンス全文を残してください。商品ページや操作画面へのクレジット表示は求めません。

このツールで制作したアバターや素材一式へのクレジット表示は不要です。アバター・素材の利用条件は、それぞれの権利者の規約に従ってください。第三者のアバター・素材・Asset Organizerのコードは、本リポジトリの配布対象に含めません。

## 詳しい説明

- [更新・削除と導入方式の切り替え](Documentation~/updating-and-removing.md)
- [困ったときの対処と復元](Documentation~/installation-and-recovery.md)
- [操作・コピー範囲・命名の詳細](Documentation~/copy-details.md)
- [開発者向けの導入・検証と関連文書](Documentation~/development.md)
