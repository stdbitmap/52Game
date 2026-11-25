using System;
using System.Collections.Generic;

namespace Game.Contracts
{
    /// <summary>决定卡牌何时可以使用、何时结算，以及结算后去向的规则标签。</summary>
    [Flags]
    public enum CardKeyword
    {
        None = 0,
        /// <summary>投入空槽即刻结算，从当前挑战和局外牌组移除这张牌。</summary>
        Gift = 1,
        /// <summary>结算后从本次挑战移除，下次挑战仍从局外牌组取得这张牌。</summary>
        Exhaust = 2,
        /// <summary>仅可在最后一回合使用，即使同时带有馈赠也要遵守。</summary>
        Emergency = 4
    }

    /// <summary>一类卡牌共用的内容模板，保存卡牌名称和按声明顺序结算的效果。</summary>
    public sealed class CardDefinition
    {
        private const CardKeyword AvailableKeywords = CardKeyword.Gift | CardKeyword.Exhaust | CardKeyword.Emergency;

        public CardDefinition(string id, string name, IEnumerable<CardEffect> effects)
            : this(id, name, effects, CardKeyword.None)
        {
        }

        public CardDefinition(
            string id,
            string name,
            IEnumerable<CardEffect> effects,
            CardKeyword keywords = CardKeyword.None,
            string artworkAddress = null,
            string descriptionAddress = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("The card ID is required.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("The card name is required.", nameof(name));
            }

            if ((keywords & ~AvailableKeywords) != CardKeyword.None)
            {
                throw new ArgumentOutOfRangeException(nameof(keywords));
            }

            // 拷贝输入集合，避免配置方修改原列表时改变已经创建的卡牌。
            var orderedEffects = new List<CardEffect>(effects);
            if (orderedEffects.Contains(null))
            {
                throw new ArgumentException("Card effects cannot contain null entries.", nameof(effects));
            }

            Id = id;
            Name = name;
            Keywords = keywords;
            ArtworkAddress = artworkAddress?.Trim() ?? string.Empty;
            DescriptionAddress = descriptionAddress?.Trim() ?? string.Empty;
            Effects = orderedEffects.AsReadOnly();
        }

        /// <summary>内容 ID。同类卡牌共用此值，适合用于配置和界面资源的查找。</summary>
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<CardEffect> Effects { get; }

        /// <summary>可以通过按位或为同一张牌组合多个关键字。</summary>
        public CardKeyword Keywords { get; }

        /// <summary>供表现层查询卡图的资源键。空字符串表示沿用普通卡面。</summary>
        public string ArtworkAddress { get; }

        /// <summary>供表现层查询卡牌详细说明的文本资源键。</summary>
        public string DescriptionAddress { get; }

        /// <summary>查询本牌是否具有指定的所有关键字。</summary>
        public bool HasKeyword(CardKeyword keyword) => (Keywords & keyword) == keyword;
    }

    /// <summary>一张独立的牌，挑战会在手牌、队列和牌堆之间移动它。</summary>
    public sealed class CardInstance
    {
        public CardInstance(int instanceId, CardDefinition definition)
        {
            InstanceId = instanceId;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        /// <summary>同一局外牌组中稳定的实例 ID。拖拽时使用此值区分多张同类卡牌。</summary>
        public int InstanceId { get; }
        public CardDefinition Definition { get; }
    }
}
