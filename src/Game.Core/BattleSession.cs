using System;
using System.Collections.Generic;
using System.Linq;
using Game.Contracts;

namespace Game.Core
{
    /// <summary>
    /// 一场挑战的规则入口。会话持有手牌、队列、抽牌堆和弃牌堆，调用方通过命令修改状态。
    /// </summary>
    public sealed class BattleSession : IBattleSession
    {
        private readonly ChallengeDefinition _definition;
        private readonly PlayerDeck _deck;
        private readonly RunRandom _random;
        private readonly List<CardInstance> _hand = new List<CardInstance>();
        private readonly List<CardInstance> _drawPile = new List<CardInstance>();
        private readonly List<CardInstance> _discardPile = new List<CardInstance>();
        private readonly CardInstance[] _queue = new CardInstance[BattleSnapshot.QueueCapacity];
        private readonly List<CardResolution> _lastResolutions = new List<CardResolution>();
        private readonly List<CardResolution> _immediateResolutions = new List<CardResolution>();
        private BattlePhase _phase = BattlePhase.Planning;
        private int _turn = 1;
        private int _lastResolvedTurn;
        private long _benefit;

        /// <summary>使用起始牌组模板建立一场独立挑战。</summary>
        public BattleSession(ChallengeDefinition definition, int seed)
            : this(definition, seed, new PlayerDeck((definition ?? throw new ArgumentNullException(nameof(definition))).StartingDeck))
        {
        }

        /// <summary>从已有局外牌组开启挑战，馈赠会影响后续使用同一牌组开启的挑战。</summary>
        public BattleSession(ChallengeDefinition definition, int seed, PlayerDeck deck)
            : this(definition, new RunRandom(seed), deck)
        {
        }

        /// <summary>使用开局随机数序列完成本次挑战的初始洗牌和之后的重洗。</summary>
        public BattleSession(ChallengeDefinition definition, RunRandom random, PlayerDeck deck)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _deck = deck ?? throw new ArgumentNullException(nameof(deck));
            _random = random ?? throw new ArgumentNullException(nameof(random));

            _drawPile.AddRange(deck.Cards);
            _random.Shuffle(_drawPile);
            DrawCards(definition.CardsPerTurn);
            Snapshot = CreateSnapshot(0);
        }

        /// <inheritdoc />
        public BattleSnapshot Snapshot { get; private set; }

        /// <inheritdoc />
        public BattleResult Execute(BattleCommand command, int expectedRevision)
        {
            // 界面提交绘制时看到的版本。重复提交或基于旧手牌的拖拽会在修改状态前被拒绝。
            if (expectedRevision != Snapshot.Revision)
            {
                return new BattleResult(BattleError.StaleSnapshot, Snapshot);
            }

            if (_phase != BattlePhase.Planning)
            {
                return new BattleResult(BattleError.ChallengeEnded, Snapshot);
            }

            BattleError error = command switch
            {
                PlaceCard place => Place(place.InstanceId, place.SlotIndex),
                ReturnCard takeBack => ReturnToHand(takeBack.InstanceId),
                EndTurn _ => ResolveTurn(),
                FinishChallenge _ => FinishEarly(),
                _ => throw new ArgumentException("A battle command is required.", nameof(command))
            };

            // 一条成功命令只发布一次快照。结束回合后的快照包含完整的逐卡结算记录。
            if (error == BattleError.None)
            {
                Snapshot = CreateSnapshot(Snapshot.Revision + 1);
            }

            return new BattleResult(error, Snapshot);
        }

        private BattleError Place(int instanceId, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _queue.Length)
            {
                return BattleError.InvalidSlot;
            }

            if (_queue[slotIndex] != null)
            {
                return BattleError.SlotOccupied;
            }

            int handIndex = _hand.FindIndex(card => card.InstanceId == instanceId);
            if (handIndex >= 0)
            {
                var card = _hand[handIndex];
                if (card.Definition.HasKeyword(CardKeyword.Emergency) && _turn < _definition.TurnLimit)
                {
                    return BattleError.CardRequiresFinalTurn;
                }

                _hand.RemoveAt(handIndex);
                if (card.Definition.HasKeyword(CardKeyword.Gift))
                {
                    ResolveGift(card, slotIndex);
                }
                else
                {
                    _queue[slotIndex] = card;
                }

                return BattleError.None;
            }

            int sourceSlot = FindQueuedSlot(instanceId);
            if (sourceSlot < 0)
            {
                return BattleError.CardUnavailable;
            }

            _queue[slotIndex] = _queue[sourceSlot];
            _queue[sourceSlot] = null;
            return BattleError.None;
        }

        private BattleError ReturnToHand(int instanceId)
        {
            int sourceSlot = FindQueuedSlot(instanceId);
            if (sourceSlot < 0)
            {
                return BattleError.CardUnavailable;
            }

            _hand.Add(_queue[sourceSlot]);
            _queue[sourceSlot] = null;
            return BattleError.None;
        }

        private int FindQueuedSlot(int instanceId)
        {
            return Array.FindIndex(_queue, card => card?.InstanceId == instanceId);
        }

        private void ResolveGift(CardInstance card, int slotIndex)
        {
            // 馈赠占用这个空槽来判断牌序条件，结算后不留在队列中。
            int queuedCards = _queue.Count(queued => queued != null);
            int laterCards = _queue.Skip(slotIndex + 1).Count(queued => queued != null);
            var context = new CardEffectContext(
                _immediateResolutions.Count, laterCards, _immediateResolutions.Sum(entry => entry.CardsDrawn),
                queuedCards + 1, isLastCard: queuedCards == 0);
            var resolution = CardEffectResolver.Resolve(card, slotIndex, context, DrawCards);
            _benefit += resolution.BenefitGained;
            _immediateResolutions.Add(resolution);
            _deck.Remove(card);
        }

        private BattleError ResolveTurn()
        {
            _lastResolvedTurn = _turn;
            _lastResolutions.Clear();
            _lastResolutions.AddRange(_immediateResolutions);
            _immediateResolutions.Clear();

            // 先弃掉未入队的手牌。结算期间抽到的新牌保留在手里，供下一回合使用。
            _discardPile.AddRange(_hand);
            _hand.Clear();

            // 槽序决定先后，空槽直接跳过。当前牌完成自身效果后才进入弃牌堆。
            // 因此抽牌能抽到已结算或未入队的牌，但抽不到当前牌或还在排队的牌。
            int queuedCards = _queue.Count(card => card != null);
            int remainingCards = queuedCards;
            int effectDraws = _lastResolutions.Sum(entry => entry.CardsDrawn);
            for (int slotIndex = 0; slotIndex < _queue.Length; slotIndex++)
            {
                var card = _queue[slotIndex];
                if (card == null)
                {
                    continue;
                }

                remainingCards--;
                var context = new CardEffectContext(
                    _lastResolutions.Count, remainingCards, effectDraws,
                    queuedCards, isLastCard: remainingCards == 0);
                var resolution = CardEffectResolver.Resolve(card, slotIndex, context, DrawCards);
                effectDraws += resolution.CardsDrawn;
                _benefit += resolution.BenefitGained;
                _lastResolutions.Add(resolution);
                _queue[slotIndex] = null;

                // 消耗牌已经用过，留在局外牌组供后续挑战使用。
                if (!card.Definition.HasKeyword(CardKeyword.Exhaust))
                {
                    _discardPile.Add(card);
                }
            }

            // 结算完整条队列后再判断胜负。提前达到收益目标也要结算剩余卡牌。
            if (_turn == _definition.TurnLimit)
            {
                _phase = _benefit >= _definition.BenefitTarget ? BattlePhase.Won : BattlePhase.Lost;
            }
            else
            {
                _turn++;
                DrawCards(_definition.CardsPerTurn);
            }

            return BattleError.None;
        }

        private BattleError FinishEarly()
        {
            if (_benefit < _definition.BenefitTarget)
            {
                return BattleError.TargetNotMet;
            }

            if (Array.Exists(_queue, card => card != null))
            {
                return BattleError.QueueNotEmpty;
            }

            _phase = BattlePhase.Won;
            return BattleError.None;
        }

        private int DrawCards(int count)
        {
            int drawn = 0;
            while (drawn < count)
            {
                // 抽牌堆用完才重洗弃牌堆。两堆都空时保留已抽到的牌并停止。
                if (_drawPile.Count == 0)
                {
                    if (_discardPile.Count == 0)
                    {
                        break;
                    }

                    _drawPile.AddRange(_discardPile);
                    _discardPile.Clear();
                    _random.Shuffle(_drawPile);
                }

                int top = _drawPile.Count - 1;
                _hand.Add(_drawPile[top]);
                _drawPile.RemoveAt(top);
                drawn++;
            }

            return drawn;
        }

        // 只有 RunSession 在确认消耗 Insight 后会读取这个快照。第一项是下一张抽到的牌。
        internal IReadOnlyList<CardDefinition> GetDrawOrder()
        {
            var nextCards = new CardDefinition[_drawPile.Count];
            for (int index = 0; index < _drawPile.Count; index++)
            {
                nextCards[index] = _drawPile[_drawPile.Count - 1 - index].Definition;
            }

            return Array.AsReadOnly(nextCards);
        }

        private static IEnumerable<PileCardSummary> SummarizePile(IEnumerable<CardInstance> pile)
        {
            // 先按定义合计，然后固定排序。同 ID 的不同定义按原牌组实例编号区分，
            // 所以快照的排列也不会保留抽牌堆或弃牌堆的实际顺序。
            return pile.GroupBy(card => card.Definition)
                .OrderBy(group => group.Key.Id, StringComparer.Ordinal)
                .ThenBy(group => group.Min(card => card.InstanceId))
                .Select(group => new PileCardSummary(group.Key, group.Count()));
        }

        private BattleSnapshot CreateSnapshot(int revision)
        {
            return new BattleSnapshot(
                revision, _definition.Scenario, _phase, _turn, _definition.TurnLimit,
                _benefit, _definition.BenefitTarget, _hand, _queue, _drawPile.Count,
                _discardPile.Count, _lastResolvedTurn, _lastResolutions, _immediateResolutions,
                SummarizePile(_drawPile), SummarizePile(_discardPile));
        }
    }
}
