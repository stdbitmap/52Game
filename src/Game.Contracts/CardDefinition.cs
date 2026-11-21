using System;
using System.Collections.Generic;

namespace Game.Contracts
{
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
