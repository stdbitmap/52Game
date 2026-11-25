using System;
using System.Collections.Generic;
using System.Linq;
using Game.Contracts;

namespace Game.Core
{
    public enum RunPhase
    {
        Map,
        ChooseStarter,
        Encounter,
        ChooseReward,
        Won,
        Lost,
        ChooseEvent
    }

    public sealed class StarterSet
    {
        public StarterSet(string name, IEnumerable<CardDefinition> cards)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A starter set needs a name.", nameof(name));
            }

            if (cards == null)
            {
                throw new ArgumentNullException(nameof(cards));
            }

            var definitions = cards.ToArray();
            if (definitions.Length == 0 || Array.Exists(definitions, card => card == null))
            {
                throw new ArgumentException("A starter set needs at least one valid card.", nameof(cards));
            }

            Name = name;
            Cards = Array.AsReadOnly(definitions);
        }

        public string Name { get; }
        public IReadOnlyList<CardDefinition> Cards { get; }
    }

    public sealed class FacilityDefinition
    {
        public FacilityDefinition(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A facility needs an ID.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A facility needs a name.", nameof(name));
            }

            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
    }

    /// <summary>一次奖励选项，由一张牌或一项设施组成。</summary>
    public sealed class RunReward
    {
        internal RunReward(CardDefinition card, string description)
        {
            Card = card ?? throw new ArgumentNullException(nameof(card));
            Description = description;
        }

        internal RunReward(FacilityDefinition facility, string description)
        {
            Facility = facility ?? throw new ArgumentNullException(nameof(facility));
            Description = description;
        }

        public CardDefinition Card { get; }
        public FacilityDefinition Facility { get; }
        public string Description { get; }
    }

    /// <summary>剧本套装、奖励池和关卡参数，供多次开局共用。</summary>
    public sealed class RunDefinition
    {
        public RunDefinition(
            ScenarioTerms scenario,
            IEnumerable<StarterSet> starterSets,
            IEnumerable<CardDefinition> commonCards,
            IEnumerable<CardDefinition> rareCards,
            IEnumerable<FacilityDefinition> commonFacilities,
            IEnumerable<FacilityDefinition> rareFacilities,
            int battleTurnLimit = 2,
            long battleBenefitTarget = 50,
            int bossTurnLimit = 4,
            long bossBenefitTarget = 120,
            int cardsPerTurn = 5,
            int initialFunds = 100,
            int battleFailureCost = 20,
            int initialInsight = 3)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            StarterSets = Copy(starterSets, nameof(starterSets));
            CommonCards = Copy(commonCards, nameof(commonCards));
            RareCards = Copy(rareCards, nameof(rareCards));
            CommonFacilities = Copy(commonFacilities, nameof(commonFacilities));
            RareFacilities = Copy(rareFacilities, nameof(rareFacilities));

            if (StarterSets.Count == 0)
            {
                throw new ArgumentException("A run needs at least one starter set.", nameof(starterSets));
            }

            if (CommonCards.Count == 0 && CommonFacilities.Count == 0)
            {
                throw new ArgumentException("The common reward pool is empty.", nameof(commonCards));
            }

            if (RareCards.Count == 0 && RareFacilities.Count == 0)
            {
                throw new ArgumentException("The rare reward pool is empty.", nameof(rareCards));
            }

            BattleTurnLimit = Positive(battleTurnLimit, nameof(battleTurnLimit));
            BattleBenefitTarget = Positive(battleBenefitTarget, nameof(battleBenefitTarget));
            BossTurnLimit = Positive(bossTurnLimit, nameof(bossTurnLimit));
            BossBenefitTarget = Positive(bossBenefitTarget, nameof(bossBenefitTarget));
            CardsPerTurn = Positive(cardsPerTurn, nameof(cardsPerTurn));
            InitialFunds = Positive(initialFunds, nameof(initialFunds));
            BattleFailureCost = Positive(battleFailureCost, nameof(battleFailureCost));
            if (initialInsight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialInsight));
            }

            InitialInsight = initialInsight;
        }

        public ScenarioTerms Scenario { get; }
        public IReadOnlyList<StarterSet> StarterSets { get; }
        public IReadOnlyList<CardDefinition> CommonCards { get; }
        public IReadOnlyList<CardDefinition> RareCards { get; }
        public IReadOnlyList<FacilityDefinition> CommonFacilities { get; }
        public IReadOnlyList<FacilityDefinition> RareFacilities { get; }
        public int BattleTurnLimit { get; }
        public long BattleBenefitTarget { get; }
        public int BossTurnLimit { get; }
        public long BossBenefitTarget { get; }
        public int CardsPerTurn { get; }
        public int InitialFunds { get; }
        public int BattleFailureCost { get; }
        public int InitialInsight { get; }

        internal ChallengeDefinition CreateEncounter(StarterSet starter, bool boss)
        {
            return new ChallengeDefinition(
                Scenario,
                boss ? BossTurnLimit : BattleTurnLimit,
                boss ? BossBenefitTarget : BattleBenefitTarget,
                CardsPerTurn,
                starter.Cards);
        }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> items, string parameter) where T : class
        {
            if (items == null)
            {
                throw new ArgumentNullException(parameter);
            }

            var values = items.ToArray();
            if (Array.Exists(values, value => value == null))
            {
                throw new ArgumentException("The pool contains an invalid entry.", parameter);
            }

            return Array.AsReadOnly(values);
        }

        private static int Positive(int value, string parameter)
        {
            return (int)Positive((long)value, parameter);
        }

        private static long Positive(long value, string parameter)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameter);
            }

            return value;
        }
    }

    /// <summary>一局内共用地图、随机数和运营状态的顺序流程。</summary>
    public sealed partial class RunSession
    {
        private readonly RunDefinition _definition;
        private readonly RunRandom _random;
        private readonly List<FacilityDefinition> _facilities = new List<FacilityDefinition>();
        private readonly HashSet<int> _revealedOptions = new HashSet<int>();
        private StarterSet _chosenStarter;
        private BattleSession _rawBattle;

        public RunSession(RunDefinition definition, RunRandom random)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            State = new OperationState(RunMap.Generate(_random), definition.InitialFunds, definition.InitialInsight);
            Facilities = _facilities.AsReadOnly();
        }

        public long Seed => _random.Seed;
        public OperationState State { get; }
        public RunMap Map => State.Map;
        public RunPhase Phase { get; private set; } = RunPhase.Map;
        public MapRoom CurrentRoom => State.CurrentRoom;
        public RoomEvent ActiveEvent { get; private set; }
        public IReadOnlyList<StarterSet> StarterSets => _definition.StarterSets;
        public IReadOnlyList<RunReward> Rewards => (ActiveEvent as RewardRoomEvent)?.Options ?? Array.Empty<RunReward>();
        public IReadOnlyList<FacilityDefinition> Facilities { get; }
        public PlayerDeck Deck => State.Deck;
        public IBattleSession Battle => (ActiveEvent as BattleRoomEvent)?.Battle;
        public ScenarioTerms Scenario => _definition.Scenario;

        partial void Record(RunActionKind kind, int value, int slotIndex);

        public bool CanEnter(int roomId)
        {
            return Phase == RunPhase.Map
                && (CurrentRoom == null
                    ? roomId == Map.Layers[0][0].Id
                    : CurrentRoom.NextRoomIds.Contains(roomId));
        }

        public bool EnterRoom(int roomId)
        {
            if (!CanEnter(roomId))
            {
                return false;
            }

            var room = Map.GetRoom(roomId);
            switch (room.Kind)
            {
                case MapRoomKind.Start:
                    Phase = RunPhase.ChooseStarter;
                    break;
                case MapRoomKind.Reward:
                case MapRoomKind.RareReward:
                    OfferRewards(room, room.Kind == MapRoomKind.RareReward);
                    break;
                case MapRoomKind.Event:
                    ActiveEvent = CreateStory(room);
                    Phase = RunPhase.ChooseEvent;
                    break;
                case MapRoomKind.Battle:
                case MapRoomKind.Boss:
                    var challenge = _definition.CreateEncounter(_chosenStarter, room.Kind == MapRoomKind.Boss);
                    _rawBattle = new BattleSession(challenge, _random, Deck);
                    ActiveEvent = new BattleRoomEvent(room, TrackBattle(_rawBattle));
                    Phase = RunPhase.Encounter;
                    break;
                default:
                    throw new InvalidOperationException("The room type is invalid.");
            }

            State.Visit(room);
            Record(RunActionKind.EnterRoom, roomId, 0);
            MarkRoomBoundary();
            return true;
        }

        public bool ChooseStarter(int index)
        {
            if (Phase != RunPhase.ChooseStarter || index < 0 || index >= StarterSets.Count)
            {
                return false;
            }

            _chosenStarter = StarterSets[index];
            State.Deck = new PlayerDeck(_chosenStarter.Cards);
            CloseRoom(RunActionKind.ChooseStarter, index);
            return true;
        }

        public bool CompleteEncounter() => CompleteEncounter(offerBossReward: true);

        private bool CompleteEncounter(bool offerBossReward)
        {
            if (Phase != RunPhase.Encounter || Battle.Snapshot.Phase == BattlePhase.Planning)
            {
                return false;
            }

            var result = Battle.Snapshot.Phase;
            _rawBattle = null;
            bool isBoss = CurrentRoom.Kind == MapRoomKind.Boss;
            if (result == BattlePhase.Lost)
            {
                State.ChangeFunds(isBoss ? -State.Funds : -_definition.BattleFailureCost);
                CloseRoom(RunActionKind.CompleteEncounter, 0);
            }
            else if (isBoss && !offerBossReward)
            {
                // 旧存档的 Boss 胜利会立即退出房间，回放时不消耗额外随机数。
                CloseRoom(RunActionKind.CompleteEncounter, 0, RunPhase.Won);
            }
            else
            {
                // 胜利后仍在战斗房间选择奖励。普通奖励可跳过，Boss 的稀有奖励领取后才算通关。
                OfferRewards(CurrentRoom, rare: isBoss);
                Record(isBoss ? RunActionKind.OfferBossReward : RunActionKind.CompleteEncounter, 0, 0);
            }

            return true;
        }

        public bool ChooseReward(int index) => ChooseReward(index, out _);

        /// <summary>领取一项奖励并完成房间，同时返回实际领取内容。</summary>
        public bool ChooseReward(int index, out RoomChoiceResult result)
        {
            result = null;
            if (Phase != RunPhase.ChooseReward || index < 0 || index >= Rewards.Count)
            {
                return false;
            }

            var reward = Rewards[index];
            if (reward.Card != null)
            {
                Deck.Add(reward.Card);
            }
            else
            {
                _facilities.Add(reward.Facility);
            }

            result = new RoomChoiceResult(reward.Card?.Name ?? reward.Facility.Name, 0, reward.Card, reward.Facility);
            var nextPhase = CurrentRoom.Kind == MapRoomKind.Boss ? RunPhase.Won : RunPhase.Map;
            CloseRoom(RunActionKind.ChooseReward, index, nextPhase);
            return true;
        }

        public bool SkipReward()
        {
            if (!(ActiveEvent is RewardRoomEvent reward) || reward.IsRare)
            {
                return false;
            }

            CloseRoom(RunActionKind.SkipReward, 0);
            return true;
        }

        public bool ChooseEvent(int index) => ChooseEvent(index, out _);

        /// <summary>应用一项事件选择并完成房间，同时返回实际发放内容和资金增减。</summary>
        public bool ChooseEvent(int index, out RoomChoiceResult result)
        {
            result = null;
            if (!(ActiveEvent is StoryRoomEvent story) || index < 0 || index >= story.Choices.Count)
            {
                return false;
            }

            var choice = story.Choices[index];
            int fundsBefore = State.Funds;
            State.ChangeFunds(choice.FundsDelta);
            if (choice.Card != null)
            {
                Deck.Add(choice.Card);
            }

            result = new RoomChoiceResult(choice.Name, State.Funds - fundsBefore, choice.Card, null);
            CloseRoom(RunActionKind.ChooseEvent, index);
            return true;
        }

        public bool IsOptionRevealed(int index) => _revealedOptions.Contains(index);

        public bool RevealOption(int index)
        {
            int optionCount = ActiveEvent switch
            {
                RewardRoomEvent reward => reward.Options.Count,
                StoryRoomEvent story => story.Choices.Count,
                _ => 0
            };
            if (index < 0 || index >= optionCount || _revealedOptions.Contains(index) || !State.TrySpendInsight())
            {
                return false;
            }

            _revealedOptions.Add(index);
            Record(RunActionKind.RevealOption, index, 0);
            return true;
        }

        public bool TryInspectDrawPile(out IReadOnlyList<CardDefinition> drawOrder)
        {
            drawOrder = Array.Empty<CardDefinition>();
            if (Phase != RunPhase.Encounter || Battle.Snapshot.Phase != BattlePhase.Planning
                || Battle.Snapshot.DrawCount == 0 || !State.TrySpendInsight())
            {
                return false;
            }

            drawOrder = _rawBattle.GetDrawOrder();
            Record(RunActionKind.InspectDrawPile, 0, 0);
            return true;
        }

        private void OfferRewards(MapRoom room, bool rare)
        {
            var cards = new List<CardDefinition>(rare ? _definition.RareCards : _definition.CommonCards);
            var facilities = rare ? _definition.RareFacilities : _definition.CommonFacilities;
            var offered = new List<RunReward>(3);

            // 两张卡从同一个奖励池中无放回抽取，再从对应设施池抽取一项。
            for (int i = 0; i < 2 && cards.Count > 0; i++)
            {
                int chosen = _random.Next(cards.Count);
                string hint = i == 0
                    ? "一份夹在档案中的行动草案。"
                    : "一张折起来的行动便笺。";
                offered.Add(new RunReward(cards[chosen], hint));
                cards.RemoveAt(chosen);
            }

            if (facilities.Count > 0)
            {
                offered.Add(new RunReward(facilities[_random.Next(facilities.Count)], "一份装在信封里的建设申请。"));
            }

            ActiveEvent = new RewardRoomEvent(room, offered, rare);
            Phase = RunPhase.ChooseReward;
        }

        private StoryRoomEvent CreateStory(MapRoom room)
        {
            var cards = _definition.CommonCards;
            int story = _random.Next(2);
            var card = cards.Count == 0 ? null : cards[_random.Next(cards.Count)];
            if (story == 0)
            {
                return new StoryRoomEvent(
                    room, "临时补给", "一批备用物资送到了行动站。",
                    new StoryChoice("转售物资", "跟在站外等候的回收商谈一谈。", 12),
                    CardChoice("保留补给", "把未拆封的箱子交给行动小队。", 0, card));
            }

            return new StoryRoomEvent(
                room, "合作邀约", "邻近的团队愿意提供报酬，也出售行动资料。",
                new StoryChoice("接受短期委托", "前往约定地点与联络员碰面。", 8),
                CardChoice("购买行动资料", "查阅对方手中的研究记录。", -10, card),
                new StoryChoice("离开", "继续原定行动。", 0));
        }

        private static StoryChoice CardChoice(string name, string description, int fundsDelta, CardDefinition card)
        {
            if (card == null)
            {
                return new StoryChoice("继续行动", "沿原定路线前进。", 0);
            }

            return new StoryChoice(name, description, fundsDelta, card);
        }

        private void CloseRoom(RunActionKind action, int value, RunPhase nextPhase = RunPhase.Map)
        {
            ActiveEvent = null;
            _revealedOptions.Clear();
            Phase = State.Funds == 0 ? RunPhase.Lost : nextPhase;
            Record(action, value, 0);
            MarkRoomBoundary();
        }
    }
}
