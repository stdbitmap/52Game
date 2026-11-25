using System;
using System.Collections.Generic;
using Game.Contracts;

namespace Game.Core
{
    /// <summary>
    /// 局外持有的牌组。顺序开启的挑战可以共用此对象，馈赠会移除其中对应的一张牌。
    /// </summary>
    public sealed class PlayerDeck
    {
        private readonly List<CardInstance> _cards = new List<CardInstance>();
        private int _nextInstanceId = 1;

        public PlayerDeck(IEnumerable<CardDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            foreach (var definition in definitions)
            {
                // 每个牌组位置创建一张独立实例，两张同定义的牌也可以分别移除。
                Add(definition);
            }

            Cards = _cards.AsReadOnly();
        }

        /// <summary>当前仍持有的牌。挑战从此列表复制牌堆，不直接修改列表。</summary>
        public IReadOnlyList<CardInstance> Cards { get; }

        internal void Add(CardDefinition definition)
        {
            // 已移除牌的编号保留给原实例，后续奖励不重用该编号。
            _cards.Add(new CardInstance(_nextInstanceId, definition));
            _nextInstanceId++;
        }

        internal void Remove(CardInstance card)
        {
            _cards.Remove(card);
        }
    }
}
