# Unity 微型卡牌游戏联合开发计划

> **Agent 执行入口**　后续实现时使用 `superpowers:executing-plans` 按勾选项推进。每次接手一个任务，先读本文的规则和接口，再验证该任务的完成条件。

**目标**　交付一款 Windows 单人牌组构筑原型。玩家从开局走到胜利或失败，途中经历选路、回合出牌和战后选牌。

**架构**　你负责纯 C# 游戏核心和对外契约。Unity UI 依赖契约，团队成员可以先用假会话制作界面，再在启动场景注入真实核心。卡牌、敌人和路线采用独立内容文件。

**技术栈**　建议 Unity 6.3 LTS、2D、uGUI、TextMesh Pro、Unity Test Framework。建工程时把团队使用的具体编辑器版本和包版本写进项目文件。[Unity 版本支持](https://unity.com/releases/unity-6/support) · [Unity UI 对比](https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/ui-system-compare)

## 需求和参考

- **已明确**　做一款接近《杀戮尖塔》玩法的微型卡牌游戏，使用 Unity 做 UI。你负责游戏主体，团队通过清晰接口协作。主题待定。
- **游玩实例**　借鉴分岔路线、战后选牌或跳过，以及事件选择。科技背景、限回合达成点数、提前完成奖励和多结局属于附件里的示例。附件末尾的策划分工也属于示例。
- **反编译代码**　用来定位回合与牌堆、敌方意图、选路及奖励的职责。团队自行实现规则，使用原创卡牌、敌人和界面素材。团队仓库只需提供本计划和自己的代码即可接手。
- **本计划建议**　首版采用生命值战斗、单敌人、手工路线和占位内容。可交付范围以一局为单位，局内进度保留在内存中。拖拽、程序生成地图、存档、元进度、遗物和多角色留在后续版本评估。

## 首版玩法

### 一局的范围

```text
起点 → 常规战 1 ─┬→ 事件 ────┬→ 常规战 2 → 首领
               └→ 可选战斗 ─┘
```

- 玩家从 35 点生命值和 9 张起始牌开始。起始牌组是 4 张「轻击」、4 张「架势」、1 张「试探」。当前生命值和构筑牌组跨战斗保留。
- 每场都是单敌人，攻击牌自动作用于该敌人。常规战与可选战胜利后给 3 张候选牌，玩家选一张加入本局牌组，或跳过。首领战获胜直接结算。
- 事件二选一。恢复 5 点生命值，上限 35；或消耗 3 点生命值，获得「定点击」。生命值不高于 3 时，付费选项禁用。可选战斗多一次构筑机会，但会消耗战斗资源。
- 玩家生命值归零即失败。胜败结算都能开新局。首版使用主题中性的中文名和原创占位图形。

### 一场战斗的规则

1. 开战时从本局牌组创建战斗卡实例，洗入抽牌堆。玩家、敌人的格挡清零，展示敌人动作序列中的第一项。
2. 玩家回合开始时清除玩家旧格挡，能量置为 3，抽 5 张。抽牌堆空时洗弃牌堆补充；两个牌堆都空时停止抽牌。
3. 出牌先检查阶段、手牌实例与能量。合法时扣费，将牌移出手牌，按定义顺序结算效果。伤害先扣目标格挡，再扣生命值。敌人生命值归零即停止余下效果，牌进入弃牌堆后切到胜利对应的阶段。短暂移出的牌不会被自身抽牌效果抽到。
4. 结束回合时弃掉剩余手牌，清除敌人的旧格挡，执行已经展示的敌人动作。若玩家存活，敌方意图推进到下一项并开新回合。敌方格挡因此能抵挡下一个玩家回合的攻击。

### 占位内容

以下是实现起点，数值在联调后按实际试玩调整。稳定 ID 供牌组、奖励和测试引用，显示名和表现资源可以随主题替换。

| ID | 显示名 | 费用 | 顺序效果 |
| --- | --- | ---: | --- |
| `jab` | 轻击 | 1 | 伤害 5 |
| `brace` | 架势 | 1 | 格挡 4 |
| `probe` | 试探 | 1 | 伤害 3，格挡 2 |
| `aimed` | 定点击 | 1 | 伤害 7 |
| `heavy` | 重踏 | 2 | 伤害 12 |
| `cover` | 掩护 | 1 | 格挡 7 |
| `quick` | 快挡 | 0 | 格挡 3 |
| `reset` | 调整 | 1 | 格挡 3，抽牌 2 |
| `counter` | 返击 | 1 | 格挡 3，伤害 5 |

| 节点 ID | 类型 | 敌人生命值与循环意图 | 战后候选 |
| --- | --- | --- | --- |
| `start` | 起点 | — | — |
| `fight_1` | 常规战 | 18，攻击 5 → 攻击 6 | `aimed, cover, reset` |
| `event_1` | 事件 | — | — |
| `fight_optional` | 可选战斗 | 23，攻击 7 → 格挡 5 → 攻击 8 | `heavy, quick, counter` |
| `fight_2` | 常规战 | 24，格挡 4 → 攻击 8 | `aimed, quick, counter` |
| `boss` | 首领 | 39，攻击 7 → 格挡 5 → 攻击 11 | — |

## 模块与维护人

| 程序集或目录 | 主要维护人 | 交付与依赖 |
| --- | --- | --- |
| `Assets/Game/Contracts/` · `Game.Contracts` | 你，UI 负责人共同评审 | `IGameSession.cs`、`GameCommands.cs`、`GameSnapshot.cs`。纯 C#。 |
| `Assets/Game/Core/` · `Game.Core` | 你 | `GameSession.cs`、`Battle.cs`、`GameCatalog.cs`。只依赖 `Game.Contracts`。 |
| `Assets/Game/UnityUI/` · `Game.UnityUI` | UI 负责人 | `GamePresenter.cs`、`Prefabs/GameShell.prefab` 及各面板。依赖 `Game.Contracts` 和 Unity UI。 |
| `Assets/Game/Content/` | 内容负责人，可由任一成员兼任 | `cards.json`、`encounters.json`、`route.json`。提出规则需求，由你决定是否扩展核心。 |
| `Assets/Game/UnityApp/` · `Game.UnityApp` | 集成负责人 | `ContentLoader.cs`、`GameEntry.cs`、`Scenes/Main.unity`。启动层依赖核心、契约、UI，并在此完成接线。 |

核心与契约的 asmdef 启用 `No Engine References`。核心是局内状态与规则的唯一写入方。Unity 的 Prefab、动画、计时和文本均由表现层负责。UI 模块采用独立 Prefab，`Main.unity` 由集成负责人维护，减少多人同时改场景产生的冲突。[Unity Assembly Definition 文档](https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/script-compilation/assembly-definition-files/class-assembly-definition-importer)

直接用 MonoBehaviour 编写规则能更快做出一场战斗，但规则会和场景一起演进。纯 C# 核心加 UI 适配层多出一份契约，换来两边可以各自测试和并行开发。首版采用后一种；同步命令加快照足以覆盖当前交互。

## 核心与 Unity 公共接口

```csharp
public interface IGameSession
{
    RunSnapshot Snapshot { get; }
    CommandResult Execute(GameCommand command, int expectedRevision);
}
```

`GameSession(GameCatalog catalog)` 实现 `IGameSession`。Unity 的 `GamePresenter.Bind(IGameSession session)` 接入真实或假会话。所有命令为 `GameCommand` 的具体类型，参数如下。

| 命令 | 合法阶段 | 成功后的状态 |
| --- | --- | --- |
| `StartRun(int seed)` | `Ready`、`Won`、`Lost` | 创建新局，进入 `Map`，当前节点为 `start` |
| `SelectNode(string nodeId)` | `Map` | 只接受当前节点的后继，进入 `Combat` 或 `Event` |
| `PlayCard(int handCardInstanceId)` | `Combat` | 更新战斗，或进入 `Reward`、`Won` |
| `EndTurn()` | `Combat` | 结算敌人行动，留在 `Combat` 或进入 `Lost` |
| `ChooseReward(string cardId)` | `Reward` | 加入所选牌，回到当前节点的 `Map` |
| `SkipReward()` | `Reward` | 回到当前节点的 `Map`，牌组大小保持不变 |
| `ChooseEventOption(string optionId)` | `Event` | 结算一次事件效果，回到当前节点的 `Map` |

**读取数据**

下表固定字段名。列表类型使用 `IReadOnlyList<T>`。除标明类型的字段外，ID、名称和描述用 `string`，数值字段用 `int`，`IsSelectable`、`CanPlay`、`CanChoose` 用 `bool`。

| 类型 | 只读字段 |
| --- | --- |
| `RunSnapshot` | `Revision, Phase:RunPhase, Seed:int?, PlayerHp, PlayerMaxHp, DeckCount, Map:MapView?, Battle:BattleView?, RewardOptions:IReadOnlyList<CardSummary>, EventOptions:IReadOnlyList<EventOptionView>` |
| `MapView` / `MapNodeView` | 地图提供 `CurrentNodeId, Nodes:IReadOnlyList<MapNodeView>`。节点提供 `Id, Name, Kind:NodeKind, NextIds:IReadOnlyList<string>, IsSelectable`。 |
| `BattleView` | `Turn, Energy, PlayerBlock, DrawCount, DiscardCount, EnemyName, EnemyHp, EnemyMaxHp, EnemyBlock, IntentKind:MoveKind, IntentAmount, Hand:IReadOnlyList<HandCardView>` |
| `CardSummary` / `HandCardView` | 卡牌摘要提供 `Id, Name, Cost, Description`。手牌项提供 `InstanceId:int, Card:CardSummary, CanPlay, BlockReason:CommandError`。 |
| `EventOptionView` | `Id, Name, Description, CanChoose, BlockReason:CommandError` |

- `RunPhase` 取命令表中的阶段，`NodeKind` 和 `MoveKind` 对应内容字段的类型。卡牌效果描述由核心按效果序列生成。
- `Ready` 的 `Map` 和 `Seed` 为 `null`，生命值和牌组数为 0。开局后各阶段均保留地图，`IsSelectable` 只在地图阶段对合法后继为真。只有战斗阶段有 `Battle`，只有对应阶段提供奖励或事件列表。战斗中顶层生命值取战斗当前值，离开战斗时由核心写回本局状态。
- 快照及其中的列表是独立的只读数据。每张战斗牌有独立实例 ID，同一内容 ID 可以出现多次。实例 ID 在一场战斗内稳定，内容 ID 在各场战斗与本局牌组之间通用。

**提交与刷新**

- `CommandResult` 提供 `Accepted:bool`、`Error:CommandError` 和提交后的 `Snapshot:RunSnapshot`。错误码为 `None`、`StaleSnapshot`、`WrongPhase`、`UnavailableChoice`、`CardNotInHand`、`InsufficientEnergy`、`InsufficientHp`。先检查版本，再检查阶段和参数。不可达节点或当前没有提供的选项返回 `UnavailableChoice`。可用项的 `BlockReason` 为 `None`。
- 同一会话从 `Revision = 0` 开始。接受命令时完整结算一次，版本号加 1，跨重开继续递增。拒绝命令时返回当前快照，状态、版本号和随机序列保持原样。
- Unity 主线程提交命令，带上按钮所绑定的快照版本。UI 从结果重绘，播放反馈时锁输入，重绘结束后恢复可用状态。事件选项禁用、选路权限、能量与伤害由核心决定。
- 每局用 `new Random(seed)` 作为唯一洗牌来源。同一构建、相同内容、种子和成功命令序列应得到相同快照序列。奖励和敌人意图是固定内容。

## 内容交接

`GameCatalog(RunDefinition run, IReadOnlyList<CardDefinition> cards, IReadOnlyList<EncounterDefinition> encounters)` 通过纯 C# 数据构造。三个定义类型声明在核心中，字段含义分别对应下面的路线、卡牌和遭遇数据。测试可以直接传入小型目录。正式目录在 `ContentLoader` 中从三个 `TextAsset` 读入，解析完成后交给核心。[Unity JsonUtility API](https://docs.unity3d.com/cn/current/ScriptReference/JsonUtility.FromJson.html)

| 文件 | 顶层字段和记录 |
| --- | --- |
| `cards.json` | `cards[]`。每张提供 `id, name, cost, effects[]`。效果为 `{ kind, amount }`，`kind` 取 `Damage, Block, Draw`。 |
| `encounters.json` | `encounters[]`。每项提供 `id, name, maxHp, moves[], rewardCardIds[]`。动作同样为 `{ kind, amount }`，`kind` 取 `Attack, Block`，按数组顺序循环。首领奖励数组为空。 |
| `route.json` | `startNodeId, playerMaxHp, startingDeckIds[], nodes[], events[]`。节点提供 `id, name, kind, contentId, nextIds[]`，类型为 `Start, Combat, Event, Boss`。起点的 `contentId` 为空字符串，其余指向遭遇或事件。事件为 `{ id, options[] }`，选项提供 `id, name, hpDelta, addCardId`，没有赠牌时用空字符串。 |

载入时报告重复 ID、引用不存在、未知类型、非法费用或数值、无动作的敌人和无有效终点的路线。错误应包含文件及记录 ID。开局验证用的内容和主题素材可分别编辑，文本资源变化无需改变命令签名。

## 实施任务

### 1. 建立工程和契约

**负责**　你和 UI 负责人。**路径**　`ProjectSettings/`、`Packages/`、`Assets/Game/Contracts/`、`Assets/Game/Tests/EditMode/`。

- [ ] 建立 2D 工程和程序集，记录具体 Unity 版本与包版本。项目版本控制提交 `.meta` 文件，场景和 Prefab 使用文本序列化。
- [ ] 按上表建立 `IGameSession`、命令、结果和只读快照，固定 DTO 字段及阶段。
- [ ] EditMode 验证快照列表已经复制，源列表改变后旧快照内容保持原值。验收时核心和契约可编译，UI 负责人能以同一契约创建假会话。

### 2. 实现可测的一场战斗

**负责**　你或核心 agent。**依赖**　任务 1。**路径**　`Game/Core/{Battle,GameCatalog,GameSession}.cs`、`Game/Tests/EditMode/BattleTests.cs`，均在 `Assets/` 下。

- [ ] 准备测试用的「起点 → 单敌人首领」小目录，通过 `IGameSession` 编写测试。检查初始能量 3 和首回合手牌 5；强制抽牌触发重洗，手牌、抽牌堆、弃牌堆合计仍等于牌组大小。
- [ ] 实现单战会话、战斗内部状态、效果与敌方循环。5 点攻击打到 4 点格挡时应只扣 1 点生命。验证抽牌牌结算时不能抽到自身、合法出牌扣费一次、非法出牌前后快照相同。
- [ ] 验证旧版本的 `EndTurn` 二次提交会被拒绝，敌人只行动一次。验证致死攻击直接进入 `Won`，敌人致死后直接进入 `Lost`。统一使用任务末尾的 EditMode 验证命令。

### 3. 实现地图、奖励和事件

**负责**　你或核心 agent。**依赖**　任务 2。**路径**　`Assets/Game/Core/{GameCatalog,GameSession}.cs`、`Assets/Game/Tests/EditMode/RunTests.cs`。

- [ ] 编写固定分支的会话测试。锁定节点不能进入。普通战胜利进入奖励。选牌使牌组大小从 9 变 10，跳过时保持 9。下一战首回合，手牌和两个牌堆的卡牌总数等于当前牌组数量。
- [ ] 实现路线推进和跨战斗状态。事件测试覆盖满血恢复后仍为 35、3 HP 拒绝付费、4 HP 付费后剩 1 HP 且牌组增加一张。
- [ ] 双击同一奖励只加一张牌。用同种子和同一组命令对比两局的快照，覆盖发牌、推进与结算。结算后重开验证起点、生命值和牌组正确恢复。

### 4. 以假会话并行制作 Unity UI

**负责**　UI 负责人或 UI agent。**依赖**　任务 1，与任务 2、3 并行。**路径**　`Assets/Game/UnityUI/`、`Assets/Game/Tests/PlayMode/UiFlowTests.cs`。

- [ ] 创建带 `GamePresenter.Bind(IGameSession session)` 的 `GameShell.prefab`。开局面板提供开始按钮；地图展示连线和合法去向；战斗面板左侧展示手牌、费用和牌堆数量，右侧展示敌人、生命及下一动作。事件、奖励和结算各有一个面板。首版使用点击出牌。
- [ ] 测试内用假 `IGameSession` 依序返回固定快照并记录收到的命令。点手牌校验传入的是实例 ID 和当时的版本号；测试败局结算后点「再来一局」发送 `StartRun`。
- [ ] PlayMode 验证阶段切换、禁用按钮与失败反馈。UI 只靠换注入对象就能接入真实核心。

### 5. 填写内容并接入正式启动场景

**负责**　内容负责人填写数据，集成负责人接线。**依赖**　数据可按本文提前编写；接线依赖任务 3、4。**路径**　`Assets/Game/Content/`、`Assets/Game/UnityApp/`、`Assets/Game/Tests/EditMode/ContentLoaderTests.cs`。

- [ ] 按占位表填写三份 JSON，以文本评审名称、数字和引用。先给加载器写正常加载及未知卡牌 ID 的失败测试。
- [ ] 实现 `ContentLoader.Load(TextAsset cards, TextAsset encounters, TextAsset route) → GameCatalog`。在 `Main.unity` 中配置三个数据资产、`GameShell.prefab` 和 `GameEntry`，由 `GameEntry` 构造真实 `GameSession` 后调用 `Bind`。
- [ ] 在 Main 场景，从开始按钮进入地图、完成第一场战斗、领取或跳过奖励并回到地图。确认显示使用 JSON 中的占位数据。

### 6. 联调完整小局并交付

**负责**　全员试玩，集成负责人打包。**依赖**　任务 5。**路径**　`README.md`、Windows 构建输出。

- [ ] 真实 Unity UI 跑两条路线，至少覆盖一次首领胜利、一次战斗失败、事件及额外战斗、选牌及跳过、结算重开。记录试玩种子、路线和结果，按试玩反馈调整内容数值。
- [ ] 在 1920×1080 与 1280×720 检查卡牌、费用、敌方意图、生命值和按钮可读，缩放后按钮仍可点击。把 Main 场景加入 Windows 构建配置，运行一次导出的游戏。
- [ ] 在 README 写清打开工程、测试、运行 Main 场景和构建的方法。验收人能从检出的项目运行测试，能从构建产物开始、打完、重开一局。

## 验证与交接

Unity 编辑器内可用 Test Runner 验证 EditMode 或 PlayMode。需要从命令行执行时，在工程根目录运行下面的 PowerShell，修改编辑器路径和测试平台，输出目录须已存在。[Unity Test Framework 命令行文档](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html)

```powershell
$deckbuilderUnity = 'C:\Program Files\Unity\Hub\Editor\<项目版本>\Editor\Unity.exe'
$deckbuilderTestMode = 'EditMode'
$deckbuilderRun = Start-Process -FilePath $deckbuilderUnity -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
    '-batchmode', '-runTests', '-projectPath', ('"{0}"' -f (Get-Location).Path),
    '-testPlatform', $deckbuilderTestMode, '-testResults', "TestResults-$deckbuilderTestMode.xml",
    '-logFile', "TestResults-$deckbuilderTestMode.log"
)
$deckbuilderRun.ExitCode
```

检查进程退出码、测试 XML 是否实际生成、是否执行了预期用例，以及失败数是否为 0。把本任务的实测结果和待验证项一起交接。

- **分工顺序**　你和 UI 负责人先确定任务 1 的字段。之后核心推进任务 2、3，UI 推进任务 4，内容负责人同步准备数据。需要修改公共契约时，先在同一个合并请求写清字段变化，再更新核心、假会话和测试。
- **给 agent 的接手文字**　「执行本计划任务 N。先读本任务及公共接口，按文件范围提交。确有接口调整时写明受影响的调用方，并同步测试。交付时列出改动文件、实测命令和结果。」
- **首个联合验收点**　同一个 `GameShell` 可以绑定假会话与真实会话；真实会话从进入第一战到返回地图贯通。此后再验收完整小局。

## 反编译参考索引

本机参考目录为 `C:/Users/Amazi/Desktop/Spire2Mod/decompiled-csharp`。以下路径相对于该目录。

| 查看位置 | 可借鉴的职责 |
| --- | --- |
| `MegaCrit.Sts2.Core.Combat/CombatManager.cs` · `StartTurn`；`MegaCrit.Sts2.Core.Entities.Players/PlayerCombatState.cs` | 回合推进拥有明确入口，战斗牌堆与能量属于战斗状态。 |
| `MegaCrit.Sts2.Core.Models/CardModel.cs` · `CanPlay`、`SpendResources`；`MegaCrit.Sts2.Core.Commands/CardPileCmd.cs` · `Draw`、`ShuffleIfNecessary` | 出牌先校验和支付，再结算。抽牌与弃牌堆重洗由规则统一处理。 |
| `MegaCrit.Sts2.Core.Models/MonsterModel.cs` · `NextMove`、`PerformMove` | 展示和执行都读取已经选定的下一动作。 |
| `MegaCrit.Sts2.Core.Map/MapPoint.cs` · `Children`；`MegaCrit.Sts2.Core.Runs/RunManager.cs` · `EnterMapCoord`；`MegaCrit.Sts2.Core.Rewards/CardReward.cs` · `OnSelect` | 节点后继定义可走路线，运行会话推进当前节点，奖励选择修改本局构筑牌组。 |
