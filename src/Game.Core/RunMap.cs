using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum MapRoomKind
    {
        Start,
        Battle,
        RareReward,
        Boss,
        Reward,
        Event
    }

    /// <summary>地图中的一个房间，出边只通向下一层。</summary>
    public sealed class MapRoom
    {
        internal MapRoom(int id, int floor, int column, MapRoomKind kind, List<int> nextRoomIds)
        {
            Id = id;
            Floor = floor;
            Column = column;
            Kind = kind;
            NextRoomIds = new List<int>(nextRoomIds).AsReadOnly();
        }

        public int Id { get; }
        public int Floor { get; }
        public int Column { get; }
        public MapRoomKind Kind { get; }
        public IReadOnlyList<int> NextRoomIds { get; }
    }

    /// <summary>从第一层起点通向第二十五层首领的只读地图。</summary>
    public sealed class RunMap
    {
        public const int FloorCount = 25;

        private const int CenterColumn = 2;
        private readonly Dictionary<int, MapRoom> _rooms = new Dictionary<int, MapRoom>();

        private RunMap(IReadOnlyList<MapRoom>[] layers)
        {
            Layers = Array.AsReadOnly(layers);
            foreach (var layer in Layers)
            {
                foreach (var room in layer)
                {
                    _rooms.Add(room.Id, room);
                }
            }
        }

        /// <summary>按楼层从起点到首领排列。每层按纵列从左到右排列。</summary>
        public IReadOnlyList<IReadOnlyList<MapRoom>> Layers { get; }

        /// <summary>按房间 ID 查询。不存在时返回 null。</summary>
        public MapRoom GetRoom(int roomId) => _rooms.TryGetValue(roomId, out var room) ? room : null;

        public static RunMap Generate(RunRandom random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            var columnsByLayer = new int[FloorCount][];
            var firstIds = new int[FloorCount];
            int nextId = 1;
            for (int index = 0; index < FloorCount; index++)
            {
                columnsByLayer[index] = CreateColumns(RoomKindAt(index + 1), random);
                firstIds[index] = nextId;
                nextId += columnsByLayer[index].Length;
            }

            var layers = new IReadOnlyList<MapRoom>[FloorCount];
            for (int index = 0; index < FloorCount; index++)
            {
                int floor = index + 1;
                int[] columns = columnsByLayer[index];
                var exits = new List<int>[columns.Length];
                for (int position = 0; position < exits.Length; position++)
                {
                    exits[position] = new List<int>();
                }

                if (floor < FloorCount)
                {
                    ConnectLayers(exits, columnsByLayer[index + 1].Length, firstIds[index + 1], random);
                }

                var rooms = new MapRoom[columns.Length];
                for (int position = 0; position < rooms.Length; position++)
                {
                    rooms[position] = new MapRoom(
                        firstIds[index] + position, floor, columns[position], RoomKindAt(floor, position, random), exits[position]);
                }
                layers[index] = Array.AsReadOnly(rooms);
            }

            return new RunMap(layers);
        }

        private static MapRoomKind RoomKindAt(int floor) => floor switch
        {
            1 => MapRoomKind.Start,
            11 => MapRoomKind.RareReward,
            FloorCount => MapRoomKind.Boss,
            _ => MapRoomKind.Battle
        };

        private static MapRoomKind RoomKindAt(int floor, int position, RunRandom random)
        {
            var fixedKind = RoomKindAt(floor);
            if (fixedKind != MapRoomKind.Battle || position == 0)
            {
                return fixedKind;
            }

            return random.Next(8) switch
            {
                0 => MapRoomKind.Reward,
                1 or 2 => MapRoomKind.Event,
                _ => MapRoomKind.Battle
            };
        }

        private static int[] CreateColumns(MapRoomKind kind, RunRandom random)
        {
            if (kind != MapRoomKind.Battle)
            {
                return new[] { CenterColumn };
            }

            int roomCount = 2 + random.Next(3);
            var columns = new[] { 0, 1, 2, 3, 4 };
            random.Shuffle(columns);
            Array.Resize(ref columns, roomCount);
            Array.Sort(columns);
            return columns;
        }

        private static void ConnectLayers(List<int>[] exits, int nextRoomCount, int firstNextId, RunRandom random)
        {
            int source = 0;
            int target = 0;
            int lastSource = exits.Length - 1;
            int lastTarget = nextRoomCount - 1;

            // 左右顺序只向前移动。每个房间至少参与一条连线，分岔与合流不会形成交叉线。
            while (true)
            {
                exits[source].Add(firstNextId + target);
                if (source == lastSource && target == lastTarget)
                {
                    return;
                }

                if (source == lastSource)
                {
                    target++;
                }
                else if (target == lastTarget)
                {
                    source++;
                }
                else
                {
                    // 只移动下一层索引为分岔，只移动当前层索引为合流，同时移动为直连。
                    int step = random.Next(3);
                    if (step != 0)
                    {
                        source++;
                    }
                    if (step != 1)
                    {
                        target++;
                    }
                }
            }
        }
    }
}
