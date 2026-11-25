using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>跨房间保留的地图、走过的路线、局外牌组和资金。</summary>
    public sealed class OperationState
    {
        private readonly List<MapRoom> _path = new List<MapRoom>();

        internal OperationState(RunMap map, int initialFunds, int initialInsight)
        {
            Map = map;
            Funds = initialFunds;
            Insight = initialInsight;
            Path = _path.AsReadOnly();
        }

        public RunMap Map { get; }
        public PlayerDeck Deck { get; internal set; }
        public IReadOnlyList<MapRoom> Path { get; }
        public MapRoom CurrentRoom => _path.Count == 0 ? null : _path[_path.Count - 1];
        public int Floor => CurrentRoom?.Floor ?? 0;
        public int Funds { get; private set; }
        public int Insight { get; private set; }

        internal void Visit(MapRoom room)
        {
            _path.Add(room);
        }

        internal bool TrySpendInsight()
        {
            if (Insight == 0)
            {
                return false;
            }

            Insight--;
            return true;
        }

        internal void ChangeFunds(int delta)
        {
            Funds = (int)Math.Clamp((long)Funds + delta, 0, int.MaxValue);
        }
    }
}
