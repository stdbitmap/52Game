using System;
using System.Collections.Generic;
using Game.Contracts;

namespace Game.Core
{
    /// <summary>本卡触发时的队列与回合状态，用于判定条件和动态收益。</summary>
    internal readonly struct CardEffectContext
    {
        internal CardEffectContext(int earlierCards, int laterCards, int effectDraws, int queuedCards, bool isLastCard)
        {
            EarlierCards = earlierCards;
            LaterCards = laterCards;
            EffectDraws = effectDraws;
            QueuedCards = queuedCards;
            IsLastCard = isLastCard;
        }

        internal int EarlierCards { get; }
        internal int LaterCards { get; }
        internal int EffectDraws { get; }
        internal int QueuedCards { get; }
        internal bool IsLastCard { get; }
    }

    /// <summary>按卡牌定义的顺序执行效果，保留每步结果供后续效果和界面读取。</summary>
    internal static class CardEffectResolver
    {
        internal static CardResolution Resolve(
            CardInstance card,
            int slotIndex,
            CardEffectContext context,
            Func<int, int> drawCards)
        {
            var resolutions = new List<CardEffectResolution>(card.Definition.Effects.Count);
            int previousEffectDraws = 0;
            int effectDraws = context.EffectDraws;

            foreach (var effect in card.Definition.Effects)
            {
                var result = MeetsCondition(effect.Condition, context, previousEffectDraws)
                    ? Apply(effect, context.EarlierCards, context.LaterCards, effectDraws, drawCards)
                    : CardEffectResolution.Skipped(effect);

                // 上一步只看同一卡上的紧邻效果。按抽牌数加收益则统计本回合已发生的所有效果抽牌。
                previousEffectDraws = result.CardsDrawn;
                effectDraws += result.CardsDrawn;
                resolutions.Add(result);
            }

            return new CardResolution(card, slotIndex, resolutions);
        }

        private static bool MeetsCondition(
            CardEffectCondition condition, CardEffectContext context, int previousEffectDraws)
        {
            return condition switch
            {
                CardEffectCondition.Always => true,
                CardEffectCondition.FirstCard => context.EarlierCards == 0,
                CardEffectCondition.LastCard => context.IsLastCard,
                CardEffectCondition.FullQueue => context.QueuedCards == BattleSnapshot.QueueCapacity,
                CardEffectCondition.PreviousEffectDrewCards => previousEffectDraws > 0,
                _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null)
            };
        }

        private static CardEffectResolution Apply(
            CardEffect effect, int earlierCards, int laterCards, int effectDraws, Func<int, int> drawCards)
        {
            if (effect.Kind == CardEffectKind.Draw)
            {
                // 从会话抽牌，沿用同一套抽牌、重洗和卡牌位置规则。
                return new CardEffectResolution(effect, benefitPerGrant: 0, cardsDrawn: drawCards(effect.Amount));
            }

            int multiplier = effect.Kind switch
            {
                CardEffectKind.GainBenefit => 1,
                CardEffectKind.GainBenefitPerEarlierCard => earlierCards,
                CardEffectKind.GainBenefitPerLaterCard => laterCards,
                CardEffectKind.GainBenefitPerDrawnCard => effectDraws,
                _ => throw new InvalidOperationException($"Unsupported card effect: {effect.Kind}.")
            };
            return new CardEffectResolution(effect, (long)effect.Amount * multiplier, cardsDrawn: 0);
        }
    }
}
