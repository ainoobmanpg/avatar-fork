# 更新履歴

## 0.1.0-beta.1

最初の一般公開に向けたベータ版。今後も改良を続けるテスト版です。

- 現在のHierarchyから、Scene・Prefab・素材と参照を分離したコピーを作成します。複製先のBlueprint IDを空にします。
- 既存コピーの名前を一式変更し、GUID・登録済みBlueprint IDと手で付けた素材名を維持します。
- 未保存状態やリンクを含む保存先など、保持を確認できない条件では停止します。改名中の通常の失敗では元へ戻します。
- Windows・Unity 2022.3.22f1で検証しています。他OS・他Unity版、全アバターや全ギミックの動作は未確認です。
- [導入・更新・削除・復元手順](Documentation~/installation-and-recovery.md)と[検証記録](Documentation~/validation-history.md)を参照してください。

ツール本体と付属文書はMITライセンスです。制作したアバターや素材に本ツールのクレジット表示は求めません。
