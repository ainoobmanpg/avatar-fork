# Avatar Fork

改変状態を引き継ぎ、独立して編集できるアバターを作るUnity Editor拡張。

**v0.1.0-beta.1 ベータ版**。今後も変更・改良を続けるテスト版です。制作プロジェクトをバックアップしてから使用してください。Windows・Unity 2022.3.22f1で検証していますが、すべてのアバター・ギミックの動作を保証するものではありません。

Hierarchyのアバター、またはScene名を右クリックし、**アバターを独立して複製…**を選ぶ。複製名と保存先を指定して実行すると、現在の改変状態を持つScene・Prefab・素材一式を作る。作ったSceneから同じ操作で、さらに独立したコピーを作れる。

## 導入

Unity 2022.3向け。WindowsのUnity 2022.3.22f1で検証している。Mac・Linuxと他のUnity版は未検証。まずプロジェクト全体をバックアップする。

### ALCOM / VCCから導入する（推奨）

ALCOMまたはVRChat Creator Companionへ、次のVPMリポジトリURLを追加する。

```text
https://raw.githubusercontent.com/ainoobmanpg/avatar-fork/main/vpm/index.json
```

対象プロジェクトのパッケージ管理で「Avatar Fork」を追加する。ベータ版なので、一覧に表示されない場合はプレリリース版を表示する設定を有効にする。更新・削除も同じパッケージ管理から行う。

### unitypackageから導入する

[Releases](https://github.com/ainoobmanpg/avatar-fork/releases)から`AvatarFork-<版>.unitypackage`を取得する。Unityの`Assets > Import Package > Custom Package…`から読み込み、`Assets/AvatarFork`のツール本体・ライセンス・文書を取り込む。

**ALCOM / VCC版とunitypackage版を同時に導入しない。** 方式を切り替えるときは、先にプロジェクトをバックアップし、既存のAvatar Fork本体だけを取り除く。生成したアバターと素材は残す。詳しくは[導入・更新・削除・復元](Documentation~/installation-and-recovery.md)を参照する。

開発用にはcloneしたフォルダーの`package.json`をPackage Managerの「Add package from disk…」で指定できる。PowerShell 7で確認したWindowsの`scripts~/install.ps1 -Project <Unityプロジェクトのパス>`もこの外部フォルダー参照を登録する。実行前にUnityを閉じる。元のmanifestは`Library/AvatarFork/Install/`へ保存される。旧`Assets/Editor/AvatarBranching`と併用しない。

既定の保存先は `Assets/07_改変用/独立複製`。同じ名前があれば新しいフォルダーを作り、既存のコピーへ上書きしない。コピー後は画面の「作成したSceneを開く」から移る。

## 独立するデータ

- マテリアルと親マテリアル、画像、モデル・メッシュ、アニメーション、Animator Controller、メニュー、パラメーター、各種設定。
- モデルの外部マテリアル対応。内部マテリアルはコピー側で外部ファイルに取り出し、モデルへ割り当てる。
- Assets内の独自Shaderとinclude。コピーごとにShader名を変え、Fallback・UsePassも対応させる。
- 保存されたGUID参照と、設定に保存された素材パス。新しいGUIDを割り当て、コピー内のファイルへ付け替える。

Prefabとの接続を解除した現在のHierarchyを保存する。名前のあるSceneの未保存変更と、マテリアルなどUnity形式の素材の未保存変更もコピーへ転記する。元のScene・素材を保存し直さず、元ファイルの内容が変わっていないことを処理内で確認する。

## 現在の命名と配置

「複製名」へ一度入力すると、作成名・Scene / Prefab・素材名の例・保存先が表示される。例えば `るるね赤` と入力した場合、Hierarchyのルート名、`るるね赤.unity`、`るるね赤.prefab`、保存フォルダーを同じ名前にする。同名があれば `るるね赤 1` のように採番し、素材も含めて同じ確定名を使う。

| 対象 | 保存名の例 |
| --- | --- |
| マテリアル | `Materials/Purple__るるね赤.mat` |
| モデル内のマテリアル | `Materials/元モデル名_元材質名__るるね赤.mat` |
| 画像 | `Textures/元名__るるね赤.png` |
| Controller | `Controllers/FX__るるね赤.controller` |
| モデル | `Models/元名__るるね赤.fbx` |

アニメーションはAnimations、PrefabはPrefabs、設定はSettings、独自ShaderはShadersなどにまとめる。同じ種類に同名の素材がある場合だけ、複製名の前へ短い識別子を加える。Material・Controller・設定assetの表示名もファイル名に揃える。子オブジェクト・骨・BlendShape・Animatorのstateとparameter・Clip内部名・モデル内部の割当識別子は維持する。

再分岐は `BranchReport.json` に記録した元名と識別子を使う。`Purple__るるね赤.mat` から `rurune blue` へ分岐した場合は `Purple__rurune blue.mat` になり、前の複製名を積み重ねない。元の名前に `__` があっても分割しない。この記録は素材一式と一緒に保持する。旧形式のコピーからも新形式へ分岐できる。既存のコピーを自動改名する機能ではない。

2026-10-28に命名を実装した。日本語・空白入り英語・同名採番・再分岐・内部材質・大文字小文字だけが違う素材名と、参照の独立性・Blueprint IDを含む回帰42項目をUnity内で確認した。

## 共通のプログラムと制約

Unity標準素材、Packages内のSDK・MA・lilToon等、スクリプト・DLLは共通。これらの更新からProject全体を隔離する機能ではない。Assets内に入れたShaderは独自Shaderと同じく複製・改名の対象になる。Shader独自の操作で元のShaderへ再接続される場合があるため、描画設定の変更後も見た目と参照を確認する。共有するものは完了画面の一覧と `BranchReport.json` に残す。

元から参照先が存在しないGUIDは、コピー側の参照を空にし、元のGUIDを保持しない。現在のMissing相当の状態を保ち、後から原本の復元で再接続されることを防ぐ。元データを修復する機能ではなく、件数と対象を完了画面・記録で知らせる。

アバター外のSceneオブジェクトへの参照、名前のない未保存Scene、未保存の読み込み済みモデルへの直接変更、未適用のImporter設定、バイナリ形式のUnity素材など、保持を確認できない条件では停止する。依存する独自スクリプトが実行中に名前やパスから探す素材を、一般的な方法で完全に解析することはできない。検証したパッケージ・ギミック以外は、その動作も確認する。

Unityの読み込み後とScene保存後に、素材・meta・親マテリアル・Unityの依存関係を検査する。参照漏れを検出した場合は失敗として知らせ、出力フォルダーに失敗記録を残す。検証APIの `AvatarBrancher.Verify(folder)` は複製直後の未登録コピー向けで、Blueprint IDが空かどうかも検査する。アップロード後にIDが設定されたコピーは、その理由でFailedとなる。登録後のIDを消してこの検査へ合わせない。報告JSON内の元パスは出典の記録であり、アバターが使う参照には含めない。

VRChatのBlueprint IDは、複製先で必ず空にする。新規アバターとしてアップロードする状態にし、元の公開アバターの更新先を引き継がない。MA・ギミック・見た目の設定は保持する。保存したPrefabとSceneの両方でIDが空であることを検査し、残っていれば失敗とする。後から複製元のIDを手動で設定した場合は、独立したアップロード先ではなくなる。

## 作ったコピーの名前を一式変更する

コピーのSceneかアバターをHierarchyで右クリックし、**この複製の名前を一式変更…**を選ぶ。Projectでコピーのフォルダーを右クリックしても開ける。新しい名前を入力し、「変更内容を確認」で移動・改名するファイルを確かめてから実行する。同じ親フォルダーに同名がある場合は停止する。

Hierarchyのルート・Scene・Prefab・保存先と、生成時の名前を保っている素材を改名する。手で付けた素材のファイル名や表示名、後から追加したファイルの名前は維持する。骨・子オブジェクト・Animatorの制御名も維持する。元名と識別子の記録を引き継ぐため、改名後の再分岐でも前の複製名を重ねない。

素材のGUIDと既存のBlueprint IDは維持する。新しいコピーを作る操作ではないため、アップロード済みなら次のアップロードも同じ登録先への更新になる。コピー内の素材パス、Shader名とincludeなどは追従する。コピー外に文字列で保存した素材パスは更新しない。

複製後にコピー外の素材を追加参照した場合は、改名時の独立性検査で停止する。必要なら独立複製で新しいコピーへまとめ直す。対象のScene・素材・Importerに未保存変更がある場合は停止し、自動保存や破棄はしない。保存済みの対象Sceneは処理中に閉じ、改名後に開き直す。途中で例外が起きた場合は元のパスとファイル内容へ戻す。復元用ファイルと移動の記録は制作Projectの `Library/AvatarFork/Rename/` に保持する。Unityの強制終了などで処理が途切れた場合の自動復元は行わないため、[中断時の復元手順](Documentation~/installation-and-recovery.md#改名の途中でunityが終了した場合)で復元するまで再実行しない。

開いていたSceneの並び順・読み込み状態・アクティブSceneも維持する。シンボリックリンク・ジャンクションを含む保存先と、そのようなリンクを含むコピーの改名には対応しない。コピー外の物理ファイルを書き換えないため、処理前に停止する。

名前のないSceneが開いている場合は、Unityが対象Sceneを開き直せないため改名を始めない。先にSceneへ名前を付けて保存する。

## 検証コード

[回帰試験の実行方法](Tests~/README.md)に前提条件と実行手順を記載している。`Tests~/Regression.cs` はUnity内で実行する検証用C#。`Tests~/BranchFixtureData.cs` は検証時のみ通常のAssetsフォルダーへ導入する型。Editorフォルダーへ置かない。購入アバターを使う個別試験のコード・生データは配布しない。これらは通常のインストールに含めない。

## 記録と権利

[仕様・検証の記録](Documentation~/validation-history.md)を保持する。詳細な画像・ログはローカルのGit管理外フォルダーにあり、GitHubには含めない。検証コピー1体のWindows向けPrivateアップロードと、VRChatのDesktopモードでの装着・代表的な表示や姿勢の変化は確認済み。全メニューを直接操作した結果ではなく、VR機器・掴み・着火・接触・発声・他クライアントからの同期は未確認。

ツール本体と付属文書は[MITライセンス](LICENSE)で提供する。作者は[sakuchichi_vrc](https://x.com/sakuchichi_vrc)。ツール本体・改造版を配布するときは、作者名・Xリンクを含む著作権表示とMITライセンス全文を残す。商品ページや操作画面へのクレジット表示は求めない。

このツールで制作・複製・改名したアバターや素材一式には、本ツールのクレジット表示を求めない。制作物に含まれるアバター・素材の利用条件は、それぞれの権利者の規約に従う。第三者のアバター・素材・Asset Organizerのコードは本リポジトリの配布対象に含めない。
