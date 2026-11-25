using System.Linq;
using Game.Contracts;
using Game.Core;

namespace Game.Content
{
    /// <summary>地图演示使用的套装、奖励池和挑战参数。</summary>
    public static class RunDemoContent
    {
        private static readonly CardDefinition DetailPlan = new CardDefinition(
            "detail_plan", "细化方案", new[]
            {
                new CardEffect(CardEffectKind.GainBenefit, 9),
                new CardEffect(CardEffectKind.GainBenefitPerLaterCard, 1)
            });

        private static readonly CardDefinition JointWork = new CardDefinition(
            "joint_work", "联合协作", new[]
            {
                new CardEffect(CardEffectKind.GainBenefit, 4),
                new CardEffect(CardEffectKind.GainBenefitPerEarlierCard, 3)
            });

        private static readonly CardDefinition Supplies = new CardDefinition(
            "additional_supplies", "补充材料", new[]
            {
                new CardEffect(CardEffectKind.GainBenefit, 7),
                new CardEffect(CardEffectKind.Draw, 1)
            });

        private static readonly CardDefinition IntegratedWork = new CardDefinition(
            "integrated_work", "统合推进", new[]
            {
                new CardEffect(CardEffectKind.GainBenefit, 15),
                new CardEffect(CardEffectKind.Draw, 1)
            });

        private static readonly CardDefinition CombinedEffort = new CardDefinition(
            "combined_effort", "整体联动", new[]
            {
                new CardEffect(CardEffectKind.GainBenefit, 5),
                new CardEffect(CardEffectKind.GainBenefitPerEarlierCard, 6)
            });

        private static readonly CardDefinition SpecialGrant = new CardDefinition(
            "special_grant", "特别拨款",
            new[] { new CardEffect(CardEffectKind.GainBenefit, 27) }, CardKeyword.Gift);

        public static RunDefinition CreateRun(ScenarioTerms scenario)
        {
            var balanced = BattleDemoContent.CreateChallenge(scenario).StartingDeck;
            var teamwork = balanced.ToArray();
            teamwork[2] = JointWork;
            teamwork[4] = Supplies;
            teamwork[7] = JointWork;

            var starters = new[]
            {
                new StarterSet("稳步推进", balanced),
                new StarterSet("联合攻坚", teamwork)
            };

            return new RunDefinition(
                scenario,
                starters,
                new[] { DetailPlan, JointWork, Supplies },
                new[] { IntegratedWork, CombinedEffort, SpecialGrant },
                new[]
                {
                    new FacilityDefinition("foundation", $"基础{scenario.FacilityName}"),
                    new FacilityDefinition("support_center", $"支援{scenario.FacilityName}"),
                    new FacilityDefinition("cooperation_center", $"协作{scenario.FacilityName}")
                },
                new[]
                {
                    new FacilityDefinition("advanced_center", $"先进{scenario.FacilityName}"),
                    new FacilityDefinition("flagship", $"旗舰{scenario.FacilityName}"),
                    new FacilityDefinition("integrated_center", $"综合{scenario.FacilityName}")
                });
        }
    }
}
