using System;
using System.Collections.Generic;
using System.Linq;
using Game.Contracts;

namespace Game.Core
{
    /// <summary>一场收益挑战的开局配置，同一份配置可以用来创建多场独立挑战。</summary>
    public sealed class ChallengeDefinition
    {
        /// <param name="scenario">界面展示使用的剧本术语。</param>
        /// <param name="turnLimit">最多可结算的回合数。空队列结束回合也计入次数。</param>
        /// <param name="benefitTarget">从零开始累计的收益目标，恰好达标也算成功。</param>
        /// <param name="cardsPerTurn">每回合的基础抽牌数，额外抽牌效果另计。</param>
        /// <param name="startingDeck">起始牌组中的卡牌定义。相同定义可以出现多次。</param>
        public ChallengeDefinition(
            ScenarioTerms scenario,
            int turnLimit,
            long benefitTarget,
            int cardsPerTurn,
            IEnumerable<CardDefinition> startingDeck)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            if (turnLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(turnLimit));
            }

            if (benefitTarget <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(benefitTarget));
            }

            if (cardsPerTurn <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cardsPerTurn));
            }

            // 保存独立副本。调用方之后修改自己的牌组列表时，既有挑战配置不随之改变。
            var cards = (startingDeck ?? throw new ArgumentNullException(nameof(startingDeck))).ToArray();
            if (cards.Length == 0 || Array.Exists(cards, card => card == null))
            {
                throw new ArgumentException("The starting deck must contain valid card definitions.", nameof(startingDeck));
            }

            TurnLimit = turnLimit;
            BenefitTarget = benefitTarget;
            CardsPerTurn = cardsPerTurn;
            StartingDeck = Array.AsReadOnly(cards);
        }

        public ScenarioTerms Scenario { get; }
        public int TurnLimit { get; }
        public long BenefitTarget { get; }
        public int CardsPerTurn { get; }
        public IReadOnlyList<CardDefinition> StartingDeck { get; }
    }
}
