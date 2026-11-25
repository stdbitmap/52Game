# 52Game 开发交接

更新于 2026-11-26。当前版本是有完整单局规则和 Unity 界面的可玩原型。

## 接手入口

- 正式仓库为 `C:/Users/Amazi/Desktop/52Game`。用 Unity **6000.6.10f1** 打开 `52Game_unityGUI`，在 `Assets/Scenes/RunDemo.unity` 按 Play，进入主菜单和全宽路线。它也是 Build Settings 的首个场景。
- `Assets/Scenes/BattleDemo.unity` 用于单独调试战斗。`Assets/Scenes/RoomEvent.unity` 由地图以 Additive 模式加载，接收同一局的 `RunSession`，确认后卸载并返回路线。单独打开 RoomEvent 不会创建一局游戏。
- 规则以用户确认的五槽收益玩法为准。`docs/superpowers/plans/2026-11-22-micro-deckbuilder.md` 是历史方案。里面的敌人、攻击、格挡、能量、手工短路线和点击即出牌已废弃。桌面的 `游玩实例.docx` 是策划示例。总收益多结局、升级牌、设施增益等示例应先确认范围，再开发。
- 卡牌的类关系、效果条件和资源接入见 [卡牌模型](docs/card-model.md)。本机 `Desktop/Spire2Mod/decompiled-csharp` 可用于理解《杀戮尖塔》的职责拆分。项目使用自己的实现和素材。

## 已实现

| 模块 | 当前行为与代码入口 |
| --- | --- |
| 卡牌与战斗 | `src/Game.Contracts` 定义卡牌、实例、效果、命令和只读快照。`BattleSession` 管理牌堆和回合，`CardEffectResolver` 处理收益、抽牌、重复、条件及队列位置相关效果。回合内拖牌进入五个槽位，可换到空槽或拖回手牌。结束回合后从左到右结算。限定回合内收益达标即胜，达标后可以提前完成。 |
| 关键字和资源 | **馈赠**拖入空槽就结算，并移出本局及跨战斗牌组。**消耗**结算后移出本场战斗的牌组。**应急**仅最后一回合可用。`CardDefinition` 保存 `ArtworkAddress`、`DescriptionAddress`，卡牌实例共享定义并持有独立编号。 |
| 剧本、随机和地图 | 科技、生态、文化、经济共用玩法，换用各自术语。开局从系统时间取种子，`RunRandom` 的同一随机流驱动 `RunMap`、奖励及战斗洗牌。一张地图有 25 层，支持分岔和汇合。第 1 层选初始套牌，第 11 层稀有奖励，第 25 层 Boss，三层各只有一个节点。 |
| 运营和房间 | `OperationState` 持有跨战斗牌组、地图、路径、楼层、资金及 Insight。`RunSession`、`RoomEvents` 管理战斗、事件和奖励。普通战斗胜利后可领或跳过奖励，Boss 胜利后领取稀有奖励再通关。原型初始 100 资金、3 Insight，普通战斗失败扣 20，Boss 失败扣光，资金归零结束本局。 |
| Insight 与结果 | 事件和奖励的描述可见，实际效果需花 1 Insight 提前查看单个选项。选择后直接显示实际资金变化及领取物品，盲选也是如此。战斗可花 1 Insight 看当下的真实抽牌顺序。普通抽牌堆与弃牌堆查看只显示乱序的卡种和数量。 |
| Unity 界面 | `Assets/BattleUI/BattleScreen.cs`、`Resources/BattleView.uxml` 和 `BattleView.uss` 是战斗界面。`RunScreen.cs` 和 `RunMap.uss` 是菜单、地图、切场景和牌组查看。地图左上退出回主菜单，右上查看当前牌组。`RoomEventScreen.cs` 和 `RoomEvent.uss` 共用一套初始组、剧情、房间奖励和战后奖励界面。战斗采用中央部署、手牌贴底、确认在右上的布局，已补入场、悬停、进度和结算过渡，以及牌堆和牌组抽屉动画。 |

## 存档和资源注意事项

- `RunSave.cs` 使用剧本 ID、随机种子和动作日志回放。`RunScreen` 只在进入和退出房间时写入 `Application.persistentDataPath/52GameRun.save`。战斗回合、房内预览、战斗转到领奖界面都不增加存档点。从战斗入房档恢复时重开该战斗。选定奖励或事件时已经结算并保存退出点，确认结果页只是离开界面。旧版 Boss 直接通关的存档有兼容回放。
- 改地图生成、奖励池内容或顺序、稳定 ID、动作编号、随机调用次序与既有效果时，检查旧档和同种子回放。界面展示牌堆时使用自己的随机数，不能消耗单局随机流。
- Unity 通过 `Packages/manifest.json` 的 `file:../../src` 引用本地包 `com.game52.battle`。正式规则维护在 `src`，Unity UI 通过命令和快照与核心交互。移动或复制验证项目时保留这个相对目录关系。新 Unity 文件带上同名 `.meta`，保留已有 GUID。
- 背景素材从 `RunScreen.battleBackground` 或独立战斗的 `BattleScreen.backgroundArtwork` 接入。卡图用 Sprite，描述用 TextAsset，资源地址读取方式见卡牌文档。目前界面使用自绘场地和项目字体资源，正式背景和卡面由团队提供。中文使用随项目提供的思源黑体，重点 HUD 数字使用 Chakra Petch，许可证在资源旁。
- 最新战斗界面参考《明日方舟》[卫戍协议实机](https://m.925g.com/gonglue/303118.html)的部署、手牌、确认层级，以及 [PRTS 排印](https://mooncellwiki.github.io/prts-design/foundations/typography)。布局、图形及素材保持原创。核心、场景切换和规则边界请继续补简明中文注释，方便首次用 Unity 的团队成员维护。

## 仍缺或需要决定

1. **正式内容和数值。** `BattleDemoContent.cs`、`RunDemoContent.cs` 目前是代码内演示卡牌、套牌和奖励池，常规事件只有两个模板。需要团队提供正式卡面、背景和事件内容，并实际试玩调整目标、回合数及奖励。
2. **设施效果。** 设施目前只有 ID 和名称，领到后记录持有状态，没有改变战斗规则的被动。初始套组目前也只配置牌。设施触发时点、初始是否发设施，需先确定策划规则。
3. **附件里的扩展玩法。** 跨挑战累计总收益、多结局、提前完成给不同奖励、卡牌升级、跨战斗增益尚未实现。当前普通战的提前完成与正常获胜共用同一奖励池。是否纳入以新一轮用户要求为准。
4. **事件资金边界。** 已揭示且资金不足的付费选项会在界面禁用。盲选同一选项目前仍可执行，实际扣至 0 并触发失败。统一前先明确付费不足时是允许承担后果，还是拒绝该选项。
5. **交付验证。** 已验证核心可跑完整 25 层，Unity 已验证战斗和房间的关键交互。仍需从 Unity 战斗连续走到战后领奖的联跑记录、全 25 层人工试玩、数值平衡、Windows 独立 Player 构建和启动验收。

## 验证与工作区

- `52Game` 保存可入库源码、Unity 配置和资源、开发文档。测试、验证脚本、镜像工程、日志、图片及构建产物统一放在同级 `../52GameTest`。
- 在 `52GameTest` 使用 .NET 10 运行以下命令。本次交接重跑 **29 项通过**，覆盖卡牌、地图、资金、Insight、盲选回执、普通及 Boss 奖励、新旧存档。

```powershell
dotnet run --project Tests/Game.Battle.Tests --configuration Release --artifacts-path ./artifacts --disable-build-servers
```

- Unity 验证工程为 `../52GameTest/KeywordVerification/52Game/52Game_unityGUI`，编辑器检查脚本放在它的 `Assets/Editor`。可用 `-batchmode -executeMethod BattleUiSmoke.Run` 或 `RunRoomChoiceSmoke.Run` 启动对应验证。已有 1280×720、1024×576 拖放与牌堆检查，以及 `RunRoomChoiceSmoke` 的房间进出、盲选致败、两类奖励、牌组与退出检查。记录和实机截图在 `../52GameTest/UIReview`。跑存档相关烟测时使用镜像的独立存档位置。
- 本次交接时 Git 有未提交修改和新增未跟踪文件，包括运行场景、脚本和对应的 `.meta`。新对话应打开同一正式工作区。使用另一个检出前，确认这些文件已随当前改动一起转移。
