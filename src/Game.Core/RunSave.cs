using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Contracts;

namespace Game.Core
{
    // 数值属于存档格式的一部分，已发布的动作编号不能重排。
    internal enum RunActionKind : byte
    {
        EnterRoom = 1,
        ChooseStarter = 2,
        CompleteEncounter = 3,
        ChooseReward = 4,
        SkipReward = 5,
        ChooseEvent = 6,
        PlaceCard = 7,
        ReturnCard = 8,
        EndTurn = 9,
        FinishChallenge = 10,
        RevealOption = 11,
        InspectDrawPile = 12,
        OfferBossReward = 13
    }

    internal readonly struct RunAction
    {
        internal RunAction(RunActionKind kind, int value, int slotIndex)
        {
            Kind = kind;
            Value = value;
            SlotIndex = slotIndex;
        }

        internal RunActionKind Kind { get; }
        internal int Value { get; }
        internal int SlotIndex { get; }
    }

    /// <summary>一次房间进出时点的存档。后续操作不会改动已取得的存档。</summary>
    public sealed class RunSaveData
    {
        internal RunSaveData(string scenarioId, long seed, IEnumerable<RunAction> actions)
        {
            ScenarioId = scenarioId;
            Seed = seed;
            Actions = new List<RunAction>(actions).AsReadOnly();
        }

        public string ScenarioId { get; }
        public long Seed { get; }
        public int ActionCount => Actions.Count;

        internal IReadOnlyList<RunAction> Actions { get; }
    }

    /// <summary>读写单份房间检查点。流的生命周期由调用方管理。</summary>
    public static class RunSaveFile
    {
        private const uint Magic = 0x53323547; // G52S in little-endian byte order.
        private const ushort Version = 1;
        private static readonly Encoding FileEncoding = new UTF8Encoding(false, true);

        public static void Write(Stream stream, RunSaveData checkpoint)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (checkpoint == null)
            {
                throw new ArgumentNullException(nameof(checkpoint));
            }

            using var writer = new BinaryWriter(stream, FileEncoding, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(checkpoint.ScenarioId);
            writer.Write(checkpoint.Seed);
            writer.Write(checkpoint.ActionCount);
            foreach (var action in checkpoint.Actions)
            {
                writer.Write((byte)action.Kind);
                writer.Write(action.Value);
                writer.Write(action.SlotIndex);
            }
        }

        public static RunSaveData Read(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using var reader = new BinaryReader(stream, FileEncoding, leaveOpen: true);
            try
            {
                return ReadCheckpoint(reader);
            }
            catch (EndOfStreamException error)
            {
                throw new InvalidDataException("The run save is incomplete.", error);
            }
            catch (DecoderFallbackException error)
            {
                throw new InvalidDataException("The run save has an invalid scenario ID.", error);
            }
            catch (FormatException error)
            {
                throw new InvalidDataException("The run save has an invalid scenario ID.", error);
            }
        }

        private static RunSaveData ReadCheckpoint(BinaryReader reader)
        {
            if (reader.ReadUInt32() != Magic)
            {
                throw new InvalidDataException("This file is not a run save.");
            }

            if (reader.ReadUInt16() != Version)
            {
                throw new InvalidDataException("The run save version is unsupported.");
            }

            string scenarioId = reader.ReadString();
            long seed = reader.ReadInt64();
            int count = reader.ReadInt32();
            if (string.IsNullOrWhiteSpace(scenarioId) || count <= 0)
            {
                throw new InvalidDataException("The run save is missing its scenario or room checkpoint.");
            }

            var actions = new List<RunAction>();
            for (int index = 0; index < count; index++)
            {
                var kind = (RunActionKind)reader.ReadByte();
                if (!Enum.IsDefined(typeof(RunActionKind), kind))
                {
                    throw new InvalidDataException("The run save contains an unknown action.");
                }

                actions.Add(new RunAction(kind, reader.ReadInt32(), reader.ReadInt32()));
            }

            if (reader.BaseStream.ReadByte() != -1)
            {
                throw new InvalidDataException("Unexpected data follows the run save.");
            }

            return new RunSaveData(scenarioId, seed, actions);
        }
    }

    public sealed partial class RunSession
    {
        private readonly List<RunAction> _journal = new List<RunAction>();
        private bool _isRestoring;
        private int _lastRoomBoundary;

        /// <summary>最近一次进出房间的检查点。首次进入房间前为空。</summary>
        public RunSaveData Checkpoint { get; private set; }

        /// <summary>只在已进入房间或已完成离开房间后通知调用方写盘。</summary>
        public event Action<RunSaveData> CheckpointCreated;

        partial void Record(RunActionKind kind, int value, int slotIndex)
        {
            _journal.Add(new RunAction(kind, value, slotIndex));
        }

        private void MarkRoomBoundary()
        {
            _lastRoomBoundary = _journal.Count;
            if (!_isRestoring)
            {
                Checkpoint = new RunSaveData(Scenario.Id, Seed, _journal);
                CheckpointCreated?.Invoke(Checkpoint);
            }
        }

        private IBattleSession TrackBattle(IBattleSession battle) => new TrackedBattle(this, battle);

        /// <summary>从开局种子逐条回放存档操作，恢复到实际的房间检查点。</summary>
        public static RunSession Restore(RunDefinition definition, RunSaveData checkpoint)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (checkpoint == null)
            {
                throw new ArgumentNullException(nameof(checkpoint));
            }

            if (!string.Equals(definition.Scenario.Id, checkpoint.ScenarioId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The run save belongs to a different scenario.");
            }

            var session = new RunSession(definition, new RunRandom(checkpoint.Seed))
            {
                _isRestoring = true
            };
            for (int index = 0; index < checkpoint.Actions.Count; index++)
            {
                if (!session.Replay(checkpoint.Actions[index]))
                {
                    throw new InvalidDataException($"Run save action {index + 1} cannot be replayed.");
                }
            }

            if (checkpoint.ActionCount == 0 || session._lastRoomBoundary != checkpoint.ActionCount)
            {
                throw new InvalidDataException("The saved progress does not end at a room checkpoint.");
            }

            session._isRestoring = false;
            session.Checkpoint = checkpoint;
            return session;
        }

        private bool Replay(RunAction action) => action.Kind switch
        {
            RunActionKind.EnterRoom => EnterRoom(action.Value),
            RunActionKind.ChooseStarter => ChooseStarter(action.Value),
            RunActionKind.CompleteEncounter => CompleteEncounter(offerBossReward: false),
            RunActionKind.OfferBossReward => CurrentRoom?.Kind == MapRoomKind.Boss
                && Battle?.Snapshot.Phase == BattlePhase.Won && CompleteEncounter(),
            RunActionKind.ChooseReward => ChooseReward(action.Value),
            RunActionKind.SkipReward => SkipReward(),
            RunActionKind.ChooseEvent => ChooseEvent(action.Value),
            RunActionKind.RevealOption => RevealOption(action.Value),
            RunActionKind.InspectDrawPile => TryInspectDrawPile(out _),
            RunActionKind.PlaceCard => ReplayBattle(new PlaceCard(action.Value, action.SlotIndex)),
            RunActionKind.ReturnCard => ReplayBattle(new ReturnCard(action.Value)),
            RunActionKind.EndTurn => ReplayBattle(new EndTurn()),
            RunActionKind.FinishChallenge => ReplayBattle(new FinishChallenge()),
            _ => false
        };

        private bool ReplayBattle(BattleCommand command)
        {
            var battle = Battle;
            return battle != null && battle.Execute(command, battle.Snapshot.Revision).Accepted;
        }

        private sealed class TrackedBattle : IBattleSession
        {
            private readonly RunSession _run;
            private readonly IBattleSession _battle;

            internal TrackedBattle(RunSession run, IBattleSession battle)
            {
                _run = run;
                _battle = battle;
            }

            public BattleSnapshot Snapshot => _battle.Snapshot;

            public BattleResult Execute(BattleCommand command, int expectedRevision)
            {
                var result = _battle.Execute(command, expectedRevision);
                if (!result.Accepted)
                {
                    return result;
                }

                // 战斗内只积累已接受的操作，等离开房间时与其他状态一起写入检查点。
                switch (command)
                {
                    case PlaceCard place:
                        _run.Record(RunActionKind.PlaceCard, place.InstanceId, place.SlotIndex);
                        break;
                    case ReturnCard takeBack:
                        _run.Record(RunActionKind.ReturnCard, takeBack.InstanceId, 0);
                        break;
                    case EndTurn _:
                        _run.Record(RunActionKind.EndTurn, 0, 0);
                        break;
                    case FinishChallenge _:
                        _run.Record(RunActionKind.FinishChallenge, 0, 0);
                        break;
                }

                return result;
            }
        }
    }
}
