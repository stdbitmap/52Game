using System;
using System.Collections.Generic;
using Game.Contracts;

namespace Game.Core
{
    /// <summary>进入房间后需要处理的事件。</summary>
    public abstract class RoomEvent
    {
        protected RoomEvent(MapRoom room)
        {
            Room = room ?? throw new ArgumentNullException(nameof(room));
        }

        public MapRoom Room { get; }
    }

    public sealed class BattleRoomEvent : RoomEvent
    {
        internal BattleRoomEvent(MapRoom room, IBattleSession battle) : base(room)
        {
            Battle = battle;
        }

        public IBattleSession Battle { get; }
    }

    public sealed class RewardRoomEvent : RoomEvent
    {
        internal RewardRoomEvent(MapRoom room, IEnumerable<RunReward> options, bool isRare) : base(room)
        {
            Options = new List<RunReward>(options).AsReadOnly();
            IsRare = isRare;
        }

        public IReadOnlyList<RunReward> Options { get; }
        public bool IsRare { get; }
    }

    public sealed class StoryRoomEvent : RoomEvent
    {
        internal StoryRoomEvent(MapRoom room, string name, string description, params StoryChoice[] choices) : base(room)
        {
            Name = name;
            Description = description;
            Choices = new List<StoryChoice>(choices).AsReadOnly();
        }

        public string Name { get; }
        public string Description { get; }
        public IReadOnlyList<StoryChoice> Choices { get; }
    }

    /// <summary>记录一次房间选择真正生效的结果。未选择的选项不会产生结算结果。</summary>
    public sealed class RoomChoiceResult
    {
        internal RoomChoiceResult(string title, int fundsDelta, CardDefinition card, FacilityDefinition facility)
        {
            Title = title;
            FundsDelta = fundsDelta;
            Card = card;
            Facility = facility;
        }

        public string Title { get; }

        /// <summary>应用资金上下限后实际增减的金额。</summary>
        public int FundsDelta { get; }
        public CardDefinition Card { get; }
        public FacilityDefinition Facility { get; }
    }

    /// <summary>可以调整资金并发给一张牌的剧情选择。</summary>
    public sealed class StoryChoice
    {
        public StoryChoice(string name, string description, int fundsDelta, CardDefinition card = null)
        {
            Name = name;
            Description = description;
            FundsDelta = fundsDelta;
            Card = card;
        }

        public string Name { get; }
        public string Description { get; }
        public int FundsDelta { get; }
        public CardDefinition Card { get; }
    }
}
