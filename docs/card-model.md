# 卡牌类与结算关系

卡牌由定义和实例组成。`CardDefinition` 描述一类牌的名称与效果，`CardInstance` 表示一场挑战中的一张牌。`BattleSession` 管理实例的位置。普通牌在结束回合时按队列结算，馈赠牌在拖入空槽时立即结算。

## 类关系

```mermaid
classDiagram
    class IBattleSession {
        <<interface>>
    }
    class CardEffectKind {
        <<enumeration>>
    }
    class CardKeyword {
        <<enumeration>>
    }
    BattleDemoContent ..> ChallengeDefinition : 创建挑战配置
    ChallengeDefinition --> ScenarioTerms : 剧本术语
    ChallengeDefinition --> "1..*" CardDefinition : 起始牌组
    PlayerDeck --> "0..*" CardInstance : 局外持有实例
    CardInstance --> "1" CardDefinition : 引用定义
    CardDefinition --> CardKeyword : 使用规则
    CardDefinition --> "0..*" CardEffect : 有序效果
    CardEffect --> CardEffectKind : 效果类型
    CardEffect --> CardEffectCondition : 触发条件
    BattleSession ..|> IBattleSession
    BattleSession --> ChallengeDefinition : 读取开局配置
    BattleSession --> PlayerDeck : 读取开局牌组与移除馈赠
    BattleSession --> "0..*" CardInstance : 管理牌堆和队列
    BattleSession ..> CardEffectResolver : 委托单牌结算
    BattleSession --> BattleSnapshot : 发布快照
    BattleSnapshot --> "0..*" CardInstance : 手牌和队列
    BattleSnapshot --> "0..*" CardResolution : 本轮即时及最近一轮结算
    BattleSnapshot --> "0..*" PileCardSummary : 牌堆张数汇总
    PileCardSummary --> "1" CardDefinition : 卡牌定义
    CardResolution --> CardInstance : 已结算的牌
    CardResolution --> "0..*" CardEffectResolution : 有序明细
    CardEffectResolution --> CardEffect : 对应效果
```

| 类型 | 职责 |
| --- | --- |
| `CardDefinition` | 保存稳定的内容 ID、显示名、关键字、只读效果列表，以及卡图和描述的资源地址。起始牌组可以多次引用同一个定义。 |
| `PlayerDeck` | 保存一份可以跨挑战复用的局外牌组，为每一张牌分配实例 ID，按实际实例移除已使用的馈赠。 |
| `CardInstance` | 保存卡牌实例 ID 与对应定义。同名卡共用同一个内容 ID，拖拽与移除时通过各自的 `InstanceId` 区分。 |
| `CardEffect`、`CardEffectKind` 与 `CardEffectCondition` | 保存效果的类型、数值、段数和触发条件。效果保存在定义中，由 `CardEffectResolver` 按列表顺序判断和执行。 |
| `ChallengeDefinition` 与 `ScenarioTerms` | 配置起始牌组、回合上限、目标收益和基础抽牌数，并提供当前剧本的显示术语。四个演示剧本共用牌组，收益名称和设施类叫法取自 `ScenarioTerms`。 |
| `BattleSession` 与 `IBattleSession` | 会话拥有手牌、抽牌堆、弃牌堆和队列，负责移动卡牌、抽牌和结算。调用方通过接口读取快照和提交命令。 |
| `BattleSnapshot` | 提供手牌、五槽队列、当前收益、版本、本轮即时结算和最近一次结束回合的记录。界面可以保留旧快照来对比变化。 |
| `CardResolution` 与 `CardEffectResolution` | 前者记录一张牌所在的槽位、总收益和总抽牌，后者记录单条效果的每段收益、收益段数以及实际抽牌数。 |

## 卡牌关键字

`CardDefinition.Keywords` 可以组合多个 `CardKeyword`。关键字决定牌的使用时机和结算后的去向，`CardEffect` 描述实际执行的收益与抽牌。例如 `CardKeyword.Gift | CardKeyword.Emergency` 表示仅在最后一回合可以使用这张馈赠牌。

| 关键字 | 生效时机 | 牌的去向 |
| --- | --- | --- |
| 馈赠 `Gift` | 将手牌拖入空槽时立即结算。结算后该槽可以继续放牌。 | 从本场挑战和局外 `PlayerDeck` 移除本次使用的实例。其他同名卡保留。 |
| 消耗 `Exhaust` | 通常在结束回合时按槽序结算。结算前可以移回手牌。 | 结算后从本场挑战移除。下次用同一份 `PlayerDeck` 开始挑战时，这张牌会重新加入本场牌堆。 |
| 应急 `Emergency` | 最后一回合才接受入槽。提前拖入会保留原手牌、队列和收益。 | 单独使用此关键字时，结算后按普通牌进入弃牌堆。 |

「临时援助」演示馈赠并获得 12 点收益，「集中投入」演示消耗并获得 15 点收益，「最后冲刺」演示应急并获得 24 点收益。

## 卡牌效果

| `CardEffectKind` | `Amount` 的含义 | 结算方式 |
| --- | --- | --- |
| `GainBenefit` | 每段收益 | 按 `RepeatCount` 结算，默认一段。多段仍只占一槽。 |
| `GainBenefitPerEarlierCard` | 每张前序牌的收益 | 乘以本回合已经结算的牌数，包含馈赠。 |
| `GainBenefitPerLaterCard` | 每张后续牌的收益 | 乘以当前槽位右侧仍在队列中的牌数。馈赠使用拖入时的队列。 |
| `Draw` | 请求抽牌的张数 | 抽牌堆用完时洗入弃牌堆。两堆都空时停止，记录实际抽到的张数。 |
| `GainBenefitPerDrawnCard` | 每张实际抽到的牌对应的收益 | 乘以本回合结算到此为止的实际抽牌数，包括馈赠的抽牌以及同一卡上先执行的 `Draw`。回合开始的基础抽牌不计入。 |

例如把「调查」「执行」「整理」「协作」放进槽 `0`、`2`、`3`、`4`。若「整理」成功抽到一张，依次获得 `8 + 3`、`5 + 5`、`5 + 2`、`4 × 3`，总计 **40 点收益**。槽 `1` 为空，直接跳过。

### 触发条件

每条效果都能配置自己的 `CardEffectCondition`，默认是 `Always`。`BattleSession` 管理卡牌位置，`CardEffectResolver` 在轮到一张牌时逐条判断条件，执行收益或抽牌，并记录实际结果。

| 条件 | 含义 |
| --- | --- |
| `Always` | 执行效果。已有的两参数或固定收益段数构造方式使用此条件。 |
| `FirstCard` | 本卡是本轮第一个实际结算的卡牌。左侧空槽不影响判断。 |
| `LastCard` | 队列结算时，本卡右侧没有待结算的牌。馈赠即时触发时，队列中没有其他待结算的牌。 |
| `FullQueue` | 队列结算以点击结束回合时的槽位为准。馈赠即时结算时，以当前队列加上刚拖入的这一张计算。 |
| `PreviousEffectDrewCards` | 同一张牌上紧邻的上一条效果实际抽到至少一张牌。 |

下面的效果组合先尝试抽一张，抽到以后才获得 3 点收益。

```csharp
var effects = new[]
{
    new CardEffect(CardEffectKind.Draw, 1),
    new CardEffect(CardEffectKind.GainBenefit, 3, CardEffectCondition.PreviousEffectDrewCards)
};
```

`EffectResolutions` 保留每条效果的顺序。条件未满足时该条的 `Applied` 为 `false`，收益段数与抽牌数都为零。抽牌效果执行后即便牌堆空了，`Applied` 仍为 `true`，此时实际抽牌数是零。

## 从开局到结算

1. `BattleDemoContent.CreateChallenge(scenario)` 生成开局配置。用 `StartingDeck` 创建 `PlayerDeck`，同一份局外牌组可传入不同的 `BattleSession`。每场挑战用当前牌组洗牌并抽出首轮手牌。
2. 规划阶段，`PlaceCard(instanceId, slotIndex)` 将手牌放进空槽，也可把已入队的牌移到另一个空槽。`ReturnCard(instanceId)` 把牌移回手牌。队列固定五槽，索引为 `0` 到 `4`，`null` 表示空槽。普通牌入槽只更改位置，馈赠入槽后立即更新收益和牌组。
3. `EndTurn` 先把未入队的手牌移进弃牌堆，再从左到右结算所有占用的槽。普通牌在完成全部效果后进入弃牌堆，消耗牌结算后移出本场挑战。留在待结算槽中的牌和正在结算的牌都不会被抽到。
4. 队列清空后，若仍有回合，结算中抽到的牌留在手上，再补抽下一回合的基础手牌。最后一轮结算完，累计收益达到目标即获胜。已达标且队列为空时，也能通过 `FinishChallenge` 提前完成。

## 界面接入

界面从 `IBattleSession.Snapshot` 读取手牌和队列。拖拽开始时记下卡牌实例 ID 和快照 `Revision`，松手时把它们交给 `Execute`。每条成功命令返回新快照。若提交时版本已经过期，命令会被拒绝，界面可用返回的快照重新同步。接入 Unity 时，在主线程依次提交同一场挑战的操作。

馈赠拖入后，界面可读 `ImmediateResolutions` 展示本轮即时结算。执行 `EndTurn` 后，`LastResolutions` 先记录本轮已实际触发的馈赠，再记录从左到右结算的排队卡。用 `LastResolvedTurn` 区分结算所属回合。需要逐段展示时，读取 `EffectResolutions` 中的 `BenefitPerGrant` 和 `BenefitGrantCount`。

Unity 演示界面为每个剧本保留一份内存中的 `PlayerDeck`。切换剧本、重开挑战后，界面复用对应的牌组。重载场景会创建新的演示牌组。

快照通过 `DrawPileContents` 和 `DiscardPileContents` 提供牌堆汇总，每条 `PileCardSummary` 只包含卡牌定义和当前张数。点击抽牌堆或弃牌堆按钮时，弹窗用独立随机数源打乱汇总的展示副本，查阅过程不读取实际抽牌顺序，也不改动牌堆或战斗随机状态。

### 配置卡牌资源

界面通过 `card.Definition.ArtworkAddress` 和 `card.Definition.DescriptionAddress` 获取资源键。两者都是相对 Unity `Resources` 目录的地址，使用正斜杠，省略扩展名。未配置时，地址属性返回空字符串。

| 字段 | 资源类型 | 显示方式 |
| --- | --- | --- |
| `ArtworkAddress` | `Resources.Load<Sprite>` | 将图片按 Sprite 类型导入。没有可加载的图片时隐藏卡图区域。 |
| `DescriptionAddress` | `Resources.Load<TextAsset>` | 使用 UTF-8 文本资源，在悬停时提供补充说明。资源未配置或加载失败时跳过这条补充说明，卡面始终显示根据效果生成的文字。 |

演示牌「整理」的描述资源位于 `Assets/BattleUI/Resources/CardDescriptions/organize.txt`，配置为 `descriptionAddress: "CardDescriptions/organize"`。Unity 会缓存加载的卡图与说明，牌的实际结算仍由 `Effects` 决定。

## 源码入口

- 卡牌模板、实例与关键字见 [CardDefinition.cs](../src/Game.Contracts/CardDefinition.cs)。局外牌组见 [PlayerDeck.cs](../src/Game.Core/PlayerDeck.cs)。
- 效果配置和触发条件见 [CardEffect.cs](../src/Game.Contracts/CardEffect.cs)，执行逻辑见 [CardEffectResolver.cs](../src/Game.Core/CardEffectResolver.cs)。
- 会话接口与拖拽命令见 [BattleCommands.cs](../src/Game.Contracts/BattleCommands.cs)。
- 快照与两级结算记录见 [BattleSnapshot.cs](../src/Game.Contracts/BattleSnapshot.cs)。
- 起始牌组与示例效果见 [BattleDemoContent.cs](../src/Game.Content/BattleDemoContent.cs)。
- 抽牌、移牌与结算逻辑见 [BattleSession.cs](../src/Game.Core/BattleSession.cs)。
