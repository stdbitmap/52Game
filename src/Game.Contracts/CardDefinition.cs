using System;
using System.Collections.Generic;

namespace Game.Contracts
{
    /// <summary>卡牌进入结算队列后，在回合结束时可执行的效果类型。</summary>
    public enum CardEffectKind
    {
        /// <summary>每段增加指定数量的本场挑战收益。默认只结算一段。</summary>
        GainBenefit,

        /// <summary>按本回合先前已结算的卡牌张数增加收益。第一张牌结算此效果时收益为零。</summary>
        GainBenefitPerEarlierCard,

        /// <summary>从牌堆抽取指定张数的牌。牌不足时，结算记录保存实际抽到的张数。</summary>
        Draw,

        /// <summary>队列中每有一张排在本卡之后的牌，就增加指定数量的收益。</summary>
        GainBenefitPerLaterCard,

        /// <summary>
        /// 本回合结算中每实际抽到一张牌，就增加指定数量的收益。
        /// 包括本卡中写在此效果之前的抽牌，回合开始时的基础抽牌不计入。
        /// </summary>
        GainBenefitPerDrawnCard
    }

    /// <summary>卡牌定义中的一条不可变效果。真正应用效果由战斗核心负责。</summary>
    public sealed class CardEffect
    {
        public CardEffect(CardEffectKind kind, int amount) : this(kind, amount, 1)
        {
        }

        /// <param name="kind">效果类型。</param>
        /// <param name="amount">单段收益、每张牌对应的收益，或请求抽牌的张数。</param>
        /// <param name="repeatCount">固定收益的段数，其余效果为 1。</param>
        public CardEffect(CardEffectKind kind, int amount, int repeatCount)
        {
            if (!Enum.IsDefined(typeof(CardEffectKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            if (repeatCount < 1 || (kind != CardEffectKind.GainBenefit && repeatCount != 1))
            {
                throw new ArgumentOutOfRangeException(nameof(repeatCount), "Only fixed benefit can have multiple grants.");
            }

            Kind = kind;
            Amount = amount;
            RepeatCount = repeatCount;
        }

        public CardEffectKind Kind { get; }

        /// <summary>效果的非负数值，按类型表示每段收益、每张关联卡牌的收益或抽牌张数。</summary>
        public int Amount { get; }

        /// <summary>单卡内一项效果的段数。多段收益仍只占该卡所在的一个槽位。</summary>
        public int RepeatCount { get; }
    }

    /// <summary>一类卡牌共用的内容模板，保存卡牌名称和按声明顺序结算的效果。</summary>
    public sealed class CardDefinition
    {
        public CardDefinition(string id, string name, IEnumerable<CardEffect> effects)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("The card ID is required.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("The card name is required.", nameof(name));
            }

            // 拷贝输入集合，避免配置方修改原列表时改变已经创建的卡牌。
            var orderedEffects = new List<CardEffect>(effects);
            if (orderedEffects.Contains(null))
            {
                throw new ArgumentException("Card effects cannot contain null entries.", nameof(effects));
            }

            Id = id;
            Name = name;
            Effects = orderedEffects.AsReadOnly();
        }

        /// <summary>内容 ID。同类卡牌共用此值，适合用于配置和界面资源的查找。</summary>
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<CardEffect> Effects { get; }
    }

    /// <summary>一场挑战中可在手牌、队列和牌堆之间移动的一张实际卡牌。</summary>
    public sealed class CardInstance
    {
        public CardInstance(int instanceId, CardDefinition definition)
        {
            InstanceId = instanceId;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        /// <summary>当前挑战内的卡牌实例 ID。拖拽时使用此值区分多张同类卡牌。</summary>
        public int InstanceId { get; }
        public CardDefinition Definition { get; }
    }
}
