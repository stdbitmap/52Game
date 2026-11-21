using System;
using System.Collections.Generic;
using Game.Contracts;
using Game.Core;

namespace Game.Content
{
    /// <summary>
    /// 提供首个可测试的挑战内容。四个剧本使用相同的牌组、回合上限、目标和抽牌量。
    /// </summary>
    public static class BattleDemoContent
    {
        // 卡牌定义保存稳定 ID、显示名称和效果。调查放得越靠前，后续排队的卡能提供越多加成。
        private static readonly CardDefinition Investigate = new CardDefinition(
            "investigate", "调查", new[]
            {
                new CardEffect(CardEffectKind.GainBenefit, 8),
                new CardEffect(CardEffectKind.GainBenefitPerLaterCard, 1)
            });

        // 两段各 5 点收益，只占执行牌自己的一个槽位。
        private static readonly CardDefinition Execute = new CardDefinition(
            "execute", "执行", new[] { new CardEffect(CardEffectKind.GainBenefit, 5, repeatCount: 2) });

        // 协作每遇到一张比它先结算的牌，就获得 4 点收益。空槽不计入张数。
        // 测试排牌顺序时可先放固定收益牌，再放协作，并比较把协作移到第一槽后的收益。
        private static readonly CardDefinition Collaborate = new CardDefinition(
            "collaborate", "协作", new[] { new CardEffect(CardEffectKind.GainBenefitPerEarlierCard, 4) });

        // 先抽牌再读取本回合的实际结算抽牌数，所以整理本次抽到的牌也计入加成。
        private static readonly CardDefinition Organize = new CardDefinition(
            "organize", "整理", new[]
            {
                new CardEffect(CardEffectKind.GainBenefit, 5),
                new CardEffect(CardEffectKind.Draw, 1),
                new CardEffect(CardEffectKind.GainBenefitPerDrawnCard, 2)
            });

        // 每个数组位置对应牌组中的一张牌。重复引用同一个不可变定义可以增加它的张数。
        // 创建 BattleSession 时，每个位置都会生成有独立实例 ID 的卡牌，可以分别入队和结算。
        private static readonly CardDefinition[] StartingDeck =
        {
            Investigate, Investigate, Investigate, Investigate,
            Execute, Execute, Execute, Execute,
            Collaborate, Collaborate,
            Organize, Organize
        };

        /// <summary>
        /// 从 ScenarioTerms 读取剧本 ID、显示名称、收益名称和设施名称。
        /// 将来的启动层可以用这份列表展示剧本选项，并把所选项交给 CreateChallenge。
        /// </summary>
        public static IReadOnlyList<ScenarioTerms> Scenarios { get; } = Array.AsReadOnly(new[]
        {
            new ScenarioTerms("technology", "科技", "科技点", "设施"),
            new ScenarioTerms("ecology", "生态", "生态点", "保护区"),
            new ScenarioTerms("culture", "文化", "文化点", "博物馆"),
            new ScenarioTerms("economy", "经济", "经济点", "子公司")
        });

        /// <summary>
        /// 为所选剧本生成挑战配置，调用方可将配置与随机种子一起传给 BattleSession。
        /// </summary>
        public static ChallengeDefinition CreateChallenge(ScenarioTerms scenario)
        {
            // 依次为回合上限、目标收益和每回合基础抽牌量。改动后用相同种子和排牌顺序对比四剧本的结算。
            return new ChallengeDefinition(scenario, 3, 100, 5, StartingDeck);
        }
    }
}
