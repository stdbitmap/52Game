using System;

namespace Game.Contracts
{
    /// <summary>卡牌进入结算队列后，在回合结束时可执行的效果类型。</summary>
    public enum CardEffectKind
    {
        /// <summary>每段增加指定数量的本场挑战收益。默认只结算一段。</summary>
        GainBenefit,

        /// <summary>按本回合先前已结算的卡牌张数增加收益。</summary>
        GainBenefitPerEarlierCard,

        /// <summary>从牌堆抽取指定张数的牌。牌不足时，结算记录保存实际抽到的张数。</summary>
        Draw,

        /// <summary>队列中每有一张排在本卡之后的牌，就增加指定数量的收益。</summary>
        GainBenefitPerLaterCard,

        /// <summary>按本回合结算时实际抽到的牌数增加收益，基础抽牌不计入。</summary>
        GainBenefitPerDrawnCard
    }

    /// <summary>在即将执行一条效果时判断的条件，可用于收益效果或抽牌效果。</summary>
    public enum CardEffectCondition
    {
        /// <summary>无条件执行。</summary>
        Always,

        /// <summary>本卡是本轮第一张实际结算的牌。队列左侧的空槽不计入。</summary>
        FirstCard,

        /// <summary>本卡是本轮最后一张实际结算的牌。队列右侧的空槽不计入。</summary>
        LastCard,

        /// <summary>开始结算时，五个槽位都有牌。已结算的牌也计入这五张。</summary>
        FullQueue,

        /// <summary>同一张牌上紧邻的上一条效果实际抽到至少一张牌。</summary>
        PreviousEffectDrewCards
    }

    /// <summary>一条不可变的效果配置，由战斗核心在结算时读取。</summary>
    public sealed class CardEffect
    {
        public CardEffect(CardEffectKind kind, int amount)
            : this(kind, amount, CardEffectCondition.Always)
        {
        }

        public CardEffect(CardEffectKind kind, int amount, int repeatCount)
            : this(kind, amount, CardEffectCondition.Always, repeatCount)
        {
        }

        /// <param name="kind">效果类型。</param>
        /// <param name="amount">单段收益、每张牌对应的收益，或请求抽牌的张数。</param>
        /// <param name="condition">执行此效果前检查的条件。同一卡上的其他效果依次独立判断。</param>
        /// <param name="repeatCount">固定收益的段数，其余效果为 1。</param>
        public CardEffect(
            CardEffectKind kind, int amount, CardEffectCondition condition, int repeatCount = 1)
        {
            if (!Enum.IsDefined(typeof(CardEffectKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            if (!Enum.IsDefined(typeof(CardEffectCondition), condition))
            {
                throw new ArgumentOutOfRangeException(nameof(condition));
            }

            if (repeatCount < 1 || (kind != CardEffectKind.GainBenefit && repeatCount != 1))
            {
                throw new ArgumentOutOfRangeException(nameof(repeatCount), "Only fixed benefit can have multiple grants.");
            }

            Kind = kind;
            Amount = amount;
            Condition = condition;
            RepeatCount = repeatCount;
        }

        public CardEffectKind Kind { get; }

        /// <summary>根据效果类型表示每段收益、每张关联卡牌的收益或抽牌张数。</summary>
        public int Amount { get; }

        public CardEffectCondition Condition { get; }

        /// <summary>固定收益在单张牌内的段数。多段收益仍只占一个槽位。</summary>
        public int RepeatCount { get; }
    }
}
