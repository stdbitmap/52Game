using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>一局共用的随机数序列，用于生成地图、抽取奖励和洗牌。</summary>
    public sealed class RunRandom
    {
        private readonly Random _random;

        public RunRandom(long seed)
        {
            Seed = seed;

            // 旧版独立战斗使用 int 种子。保留该范围内的结果，再折叠更长的时间种子。
            int randomSeed = seed >= int.MinValue && seed <= int.MaxValue
                ? (int)seed
                : unchecked((int)(seed ^ (seed >> 32)));
            _random = new Random(randomSeed);
        }

        /// <summary>开局的原始种子，可用于复现同一串游戏操作。</summary>
        public long Seed { get; }

        public static RunRandom FromSystemTime() => new RunRandom(DateTime.UtcNow.Ticks);

        public int Next(int exclusiveMax) => _random.Next(exclusiveMax);

        public void Shuffle<T>(IList<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            // Fisher–Yates。战斗从列表末尾抽牌，保留独立战斗的既有取牌顺序。
            for (int index = items.Count - 1; index > 0; index--)
            {
                int other = Next(index + 1);
                var item = items[index];
                items[index] = items[other];
                items[other] = item;
            }
        }
    }
}
