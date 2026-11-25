using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Game.Content;
using Game.Contracts;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.UnityUI
{
    /// <summary>显示路线和牌组，在同一局的地图、战斗和独立房间界面之间切换。</summary>
    [DisallowMultipleComponent]
    public sealed class RunScreen : MonoBehaviour
    {
        private const string RoomScenePath = "Assets/Scenes/RoomEvent.unity";
        private const long EnterAnimationDelay = 20;
        private const long DeckCloseDuration = 130;
        private const float FloorSpacing = 65f;
        private const float BoardInset = 38f;
        private const float FirstLanePercent = 18f;
        private const float LaneSpacingPercent = 16.5f;

        [SerializeField] private Texture2D battleBackground = null;

        private VisualElement _root;
        private PanelSettings _ownedPanel;
        private UiTypography _typography;
        private RunSession _run;
        private VisualElement _mapShell;
        private VisualElement _deckOverlay;
        private Scene _roomScene;
        private AsyncOperation _roomOperation;
        private GameObject _battleObject;
        private int _scenarioIndex;
        private string _saveNotice;
        private string _roomNotice;

        private void OnEnable()
        {
            var document = GetComponent<UIDocument>() ?? gameObject.AddComponent<UIDocument>();
            if (document.panelSettings == null)
            {
                _ownedPanel = ScriptableObject.CreateInstance<PanelSettings>();
                _ownedPanel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("BattleTheme");
                _ownedPanel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                _ownedPanel.referenceResolution = new Vector2Int(1280, 720);
                _ownedPanel.screenMatchMode = PanelScreenMatchMode.Expand;
                document.panelSettings = _ownedPanel;
            }

            var documentRoot = document.rootVisualElement;
            if (documentRoot == null)
            {
                Debug.LogError("The map UI requires a runtime UI Toolkit panel.", this);
                return;
            }

            documentRoot.style.flexGrow = 1;
            documentRoot.pickingMode = PickingMode.Ignore;
            _root = Element("run-screen", "run-screen");
            documentRoot.Add(_root);

            var style = Resources.Load<StyleSheet>("RunMap");
            if (style != null)
            {
                _root.styleSheets.Add(style);
            }

            _typography ??= new UiTypography();
            _typography.ApplyBody(_root);

            SetRun(_run);
            Render(animate: true);
            OpenCurrentRoom();
        }

        private void OnDisable()
        {
            if (_battleObject != null)
            {
                _battleObject.SetActive(false);
                Destroy(_battleObject);
                _battleObject = null;
            }

            if (_run != null)
            {
                _run.CheckpointCreated -= SaveCheckpoint;
            }

            _root?.RemoveFromHierarchy();
            _root = null;
            _mapShell = null;
            _deckOverlay = null;
            // 加载中的场景无法取消。完成回调发现地图已经销毁时会负责卸载。
            if (_roomOperation == null && _roomScene.IsValid())
            {
                CloseRoomScene();
            }
        }

        private void OnDestroy()
        {
            if (_ownedPanel != null)
            {
                Destroy(_ownedPanel);
            }
        }

        private static string SavePath => Path.Combine(Application.persistentDataPath, "52GameRun.save");

        private void StartGame()
        {
            var scenario = BattleDemoContent.Scenarios[_scenarioIndex];
            SetRun(new RunSession(RunDemoContent.CreateRun(scenario), RunRandom.FromSystemTime()));
            _saveNotice = null;
            _roomNotice = null;
            Render(animate: true);
        }

        private void ContinueGame()
        {
            try
            {
                RunSaveData save;
                using (var stream = File.OpenRead(SavePath))
                {
                    save = RunSaveFile.Read(stream);
                }

                var scenarios = BattleDemoContent.Scenarios;
                int index = 0;
                while (index < scenarios.Count && scenarios[index].Id != save.ScenarioId)
                {
                    index++;
                }

                if (index == scenarios.Count)
                {
                    _saveNotice = "无法读取存档，剧本不存在。";
                    Render();
                    return;
                }

                var run = RunSession.Restore(RunDemoContent.CreateRun(scenarios[index]), save);
                _scenarioIndex = index;
                SetRun(run);
                _saveNotice = null;
                _roomNotice = null;
                Render(animate: true);
                OpenCurrentRoom();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is InvalidDataException || exception is ArgumentException
                || exception is InvalidOperationException)
            {
                _saveNotice = "无法读取存档，请检查文件权限或存档版本。";
                Debug.LogWarning(exception.Message, this);
                Render();
            }
        }

        private void SetRun(RunSession run)
        {
            if (_run != null)
            {
                _run.CheckpointCreated -= SaveCheckpoint;
            }

            _run = run;
            if (_run != null)
            {
                _run.CheckpointCreated += SaveCheckpoint;
            }
        }

        private void SaveCheckpoint(RunSaveData save)
        {
            try
            {
                string path = SavePath;
                string temporary = path + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    RunSaveFile.Write(stream, save);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }

                _saveNotice = null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is NotSupportedException || exception is ArgumentException)
            {
                _saveNotice = "自动存档失败，未能更新本次房间进度。";
                Debug.LogWarning(exception.Message, this);
            }
        }

        private void Render(bool animate = false)
        {
            _root.Clear();
            _deckOverlay = null;
            _mapShell = Element("run-shell");
            _root.Add(_mapShell);

            var header = Element("run-header");
            _mapShell.Add(header);
            if (_run == null)
            {
                header.Add(Label("52Game", "run-title"));
                RenderMenu(_mapShell);
            }
            else
            {
                RenderMapHeader(header);
                RenderMap(_mapShell);
                RenderMapStatus(_mapShell);
            }

            if (animate)
            {
                AnimateIn(_mapShell);
            }
        }

        private void RenderMapHeader(VisualElement header)
        {
            var identity = Element("run-header-side");
            header.Add(identity);
            identity.Add(Button("退出", "run-button run-back", "run-exit", ReturnToMenu));

            var title = Element("run-header-title");
            title.Add(Label("挑战路线", "run-title"));
            var current = _run.State.CurrentRoom;
            string floor = current == null ? "入口" : $"第 {current.Floor:00} 层";
            title.Add(Label($"{_run.Scenario.Name} · {floor}", "run-route-context", "run-current-room"));
            identity.Add(title);

            var actions = Element("run-header-side run-header-actions");
            header.Add(actions);
            var hud = Element("run-hud");
            actions.Add(hud);
            hud.Add(CreateHudReadout("资金", _run.State.Funds, "run-funds"));
            hud.Add(CreateHudReadout("Insight", _run.State.Insight, "run-insight"));
            int cardCount = _run.State.Deck?.Cards.Count ?? 0;
            var deck = Button($"牌组 {cardCount}", "run-button run-deck-button", "run-open-deck", OpenDeck);
            actions.Add(deck);
        }

        private void RenderMapStatus(VisualElement shell)
        {
            if (_saveNotice != null)
            {
                shell.Add(Label(_saveNotice, "run-save-notice", "run-save-notice"));
            }

            if (_roomNotice != null)
            {
                var actions = Element("run-status");
                actions.Add(Label(_roomNotice, "run-save-notice", "run-room-notice"));
                actions.Add(Button("重试进入", "run-button", "run-retry-room", OpenCurrentRoom));
                shell.Add(actions);
            }
            else if (_run.Phase == RunPhase.Won || _run.Phase == RunPhase.Lost)
            {
                var end = Element("run-status");
                string result = _run.Phase == RunPhase.Won ? "路线通关" : "资金已用尽";
                end.Add(Label(result, "run-heading", "run-result"));
                end.Add(Button("再玩一局", "run-button run-primary", "run-restart", StartGame));
                shell.Add(end);
            }
        }

        private void OpenDeck()
        {
            if (_deckOverlay != null || _root == null)
            {
                return;
            }

            var overlay = Element("run-deck-overlay", "run-deck-overlay");
            _deckOverlay = overlay;
            _root.Add(overlay);
            _mapShell.SetEnabled(false);

            var drawer = Element("run-deck-drawer", "run-deck-drawer");
            overlay.Add(drawer);
            var header = Element("run-deck-header");
            drawer.Add(header);
            int total = _run.State.Deck?.Cards.Count ?? 0;
            header.Add(Label($"当前牌组 · {total} 张", "run-deck-title"));

            var dismiss = Button("关闭", "run-button run-deck-close", "run-close-deck",
                () => CloseDeck(overlay, drawer));
            header.Add(dismiss);
            overlay.RegisterCallback<ClickEvent>(evt =>
            {
                if (ReferenceEquals(evt.target, overlay))
                {
                    CloseDeck(overlay, drawer);
                }
            });
            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopPropagation();
                    CloseDeck(overlay, drawer);
                }
            });

            if (_run.Facilities.Count > 0)
            {
                string facilities = string.Join("、", _run.Facilities.Select(item => item.Name));
                drawer.Add(Label($"{_run.Scenario.FacilityName} · {facilities}", "run-deck-info"));
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "run-deck-scroll" };
            scroll.AddToClassList("run-deck-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            drawer.Add(scroll);
            if (total == 0)
            {
                scroll.Add(Label("当前牌组为空。", "run-deck-info"));
            }
            else
            {
                // 只读局外牌组，不使用任何抽牌堆实例或洗牌顺序。
                var groups = _run.State.Deck.Cards.GroupBy(card => card.Definition.Id, StringComparer.Ordinal)
                    .OrderBy(group => group.First().Definition.Name, StringComparer.CurrentCulture);
                foreach (var group in groups)
                {
                    AddDeckCard(scroll, group.First().Definition, group.Count());
                }
            }

            AnimateIn(drawer);
            dismiss.Focus();
        }

        private void AddDeckCard(VisualElement container, CardDefinition card, int copies)
        {
            var entry = Element("run-deck-card");
            container.Add(entry);
            var header = Element("run-deck-row");
            entry.Add(header);
            header.Add(Label(card.Name, "run-deck-name"));
            header.Add(Label($"× {copies}", "run-deck-count"));
            string keywords = BattleScreenText.Keywords(card, detailed: true);
            if (!string.IsNullOrEmpty(keywords))
            {
                entry.Add(Label(keywords, "run-deck-keywords"));
            }

            foreach (var effect in card.Effects)
            {
                entry.Add(Label(BattleScreenText.Effect(effect, _run.Scenario.BenefitName), "run-deck-effect"));
            }
        }

        private void CloseDeck(VisualElement overlay, VisualElement drawer)
        {
            if (_deckOverlay != overlay || drawer.ClassListContains("run-leaving"))
            {
                return;
            }

            drawer.AddToClassList("run-leaving");
            drawer.SetEnabled(false);
            overlay.schedule.Execute(() =>
            {
                overlay.RemoveFromHierarchy();
                _deckOverlay = null;
                _mapShell.SetEnabled(true);
                _mapShell.Q<Button>("run-open-deck")?.Focus();
            }).StartingIn(DeckCloseDuration);
        }

        private static void AnimateIn(VisualElement element)
        {
            element.AddToClassList("run-entering");
            element.schedule.Execute(() => element.RemoveFromClassList("run-entering"))
                .StartingIn(EnterAnimationDelay);
        }

        private VisualElement CreateHudReadout(string caption, int value, string name)
        {
            var readout = Element($"run-readout {name}");
            readout.Add(Label(caption, "run-hud-caption"));
            var number = Label(value.ToString(CultureInfo.InvariantCulture), "run-hud-number", name);
            _typography.ApplyHudNumber(number);
            readout.Add(number);
            return readout;
        }

        private void RenderMenu(VisualElement shell)
        {
            var menuArea = Element("run-menu-area");
            shell.Add(menuArea);
            var menu = Element("run-panel run-menu");
            menuArea.Add(menu);
            menu.Add(Label("选择剧本", "run-heading"));

            var scenarios = Element("run-scenarios");
            menu.Add(scenarios);
            for (int index = 0; index < BattleDemoContent.Scenarios.Count; index++)
            {
                int selectedIndex = index;
                var scenario = BattleDemoContent.Scenarios[index];
                var button = Button(scenario.Name, "run-button run-scenario", $"scenario-{scenario.Id}", () =>
                {
                    _scenarioIndex = selectedIndex;
                    Render();
                });
                button.EnableInClassList("selected", index == _scenarioIndex);
                scenarios.Add(button);
            }

            menu.Add(Button("开始游戏", "run-button run-primary", "start-game", StartGame));
            bool saved = File.Exists(SavePath);
            var continueButton = Button(saved ? "继续游戏" : "暂无存档", "run-button run-secondary run-continue",
                "continue-game", ContinueGame);
            continueButton.SetEnabled(saved);
            menu.Add(continueButton);
            if (_saveNotice != null)
            {
                menu.Add(Label(_saveNotice, "run-save-notice", "run-save-notice"));
            }
        }

        private void RenderMap(VisualElement content)
        {
            var mapPanel = Element("run-panel run-map-panel");
            content.Add(mapPanel);

            var heading = Element("run-map-heading");
            mapPanel.Add(heading);
            heading.Add(Label("25 层路线图", "run-heading"));
            var legend = Element("run-legend");
            heading.Add(legend);
            legend.Add(Label("可进入", "run-legend-available"));
            legend.Add(Label("已到达", "run-legend-visited"));
            var seed = Label($"种子 {_run.Seed.ToString(CultureInfo.InvariantCulture)}", "run-seed", "run-seed");
            seed.tooltip = "用于重现本局地图和随机结果";
            legend.Add(seed);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "run-map-scroll" };
            scroll.AddToClassList("run-map-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            mapPanel.Add(scroll);

            var graph = Element("run-map-graph", "run-map-graph");
            graph.style.height = BoardInset * 2 + (RunMap.FloorCount - 1) * FloorSpacing;
            graph.generateVisualContent += DrawRoutes;
            scroll.Add(graph);

            var visited = new HashSet<int>(_run.State.Path.Select(room => room.Id));
            foreach (var floor in _run.Map.Layers)
            {
                if (floor.Count == 0)
                {
                    continue;
                }

                int level = floor[0].Floor;
                var floorNumber = Label(level.ToString("D2", CultureInfo.InvariantCulture), "run-floor-number");
                floorNumber.style.top = FloorY(level);
                floorNumber.pickingMode = PickingMode.Ignore;
                bool isAnchorFloor = level == 1 || level == 11 || level == RunMap.FloorCount;
                floorNumber.EnableInClassList("anchor-floor", isAnchorFloor);
                if (isAnchorFloor)
                {
                    _typography.ApplyHudNumber(floorNumber);
                }

                graph.Add(floorNumber);

                foreach (var room in floor)
                {
                    var roomButton = Button(string.Empty, "run-button run-room", $"run-room-{room.Id}",
                        () => EnterRoom(room));
                    var marker = Element("run-room-marker");
                    marker.pickingMode = PickingMode.Ignore;
                    marker.generateVisualContent += context => DrawRoomMarker(context, room.Kind);
                    roomButton.Add(marker);
                    var roomLabel = Label(RoomName(room.Kind), "run-room-label");
                    roomLabel.pickingMode = PickingMode.Ignore;
                    roomButton.Add(roomLabel);
                    roomButton.tooltip = $"第 {room.Floor} 层 · {RoomName(room.Kind)}";
                    roomButton.style.left = Length.Percent(LanePercent(room.Column));
                    roomButton.style.top = FloorY(room.Floor);
                    string kindClass = room.Kind.ToString().ToLowerInvariant();
                    roomButton.AddToClassList($"run-room-{kindClass}");
                    bool available = _run.CanEnter(room.Id);
                    bool current = _run.State.CurrentRoom?.Id == room.Id;
                    roomButton.EnableInClassList("available", available);
                    roomButton.EnableInClassList("visited", visited.Contains(room.Id));
                    roomButton.EnableInClassList("current", current);
                    roomButton.SetEnabled(available);
                    graph.Add(roomButton);
                }
            }

            int focusFloor = _run.Phase == RunPhase.Map
                ? Math.Min(RunMap.FloorCount, _run.State.Floor + 1)
                : Math.Max(1, _run.State.Floor);
            scroll.schedule.Execute(() =>
            {
                float viewport = scroll.contentViewport.layout.height;
                if (viewport > 0)
                {
                    scroll.scrollOffset = new Vector2(0f, Mathf.Max(0f, FloorY(focusFloor) - viewport * 0.68f));
                }
            }).StartingIn(40);
        }

        private void DrawRoutes(MeshGenerationContext context)
        {
            var bounds = context.visualElement.contentRect;
            if (bounds.width <= 0f || _run == null)
            {
                return;
            }

            var painter = context.painter2D;
            DrawCartography(painter, bounds);
            var visited = new HashSet<int>(_run.State.Path.Select(room => room.Id));

            // 次要路径先画，让正要进入的路线保持最清晰。
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var floor in _run.Map.Layers)
                {
                    foreach (var room in floor)
                    {
                        foreach (int nextId in room.NextRoomIds)
                        {
                            var next = _run.Map.GetRoom(nextId);
                            bool traveled = visited.Contains(room.Id) && visited.Contains(nextId);
                            bool nextStep = _run.State.CurrentRoom?.Id == room.Id && _run.CanEnter(nextId);
                            bool highlighted = traveled || nextStep;
                            if (highlighted != (pass == 1))
                            {
                                continue;
                            }

                            var from = GraphPoint(room, bounds.width);
                            var to = GraphPoint(next, bounds.width);
                            painter.lineWidth = highlighted ? 2.2f : 1.1f;
                            painter.strokeColor = nextStep ? new Color32(218, 230, 93, 255)
                                : traveled ? new Color32(184, 181, 159, 255)
                                : new Color32(106, 121, 114, 170);
                            if (highlighted)
                            {
                                DrawLine(painter, from, to);
                            }
                            else
                            {
                                var direction = (to - from).normalized;
                                float distance = Vector2.Distance(from, to);
                                painter.BeginPath();
                                for (float step = 0f; step < distance; step += 12f)
                                {
                                    painter.MoveTo(from + step * direction);
                                    painter.LineTo(from + Mathf.Min(step + 6f, distance) * direction);
                                }

                                painter.Stroke();
                            }
                        }
                    }
                }
            }
        }

        private static void DrawCartography(Painter2D painter, Rect bounds)
        {
            painter.lineWidth = 1f;
            painter.strokeColor = new Color32(141, 155, 148, 20);
            for (int floor = 1; floor <= RunMap.FloorCount; floor++)
            {
                float y = FloorY(floor);
                DrawLine(painter, new Vector2(45f, y), new Vector2(bounds.width - 12f, y));
            }

            painter.strokeColor = new Color32(149, 160, 145, 36);
            DrawContours(painter, new Vector2(bounds.width * 0.28f, bounds.height * 0.22f), 0.24f);
            DrawContours(painter, new Vector2(bounds.width * 0.72f, bounds.height * 0.54f), 1.17f);
            DrawContours(painter, new Vector2(bounds.width * 0.37f, bounds.height * 0.80f), 2.45f);
        }

        private static void DrawContours(Painter2D painter, Vector2 center, float phase)
        {
            const int segments = 36;
            for (int ring = 1; ring <= 7; ring++)
            {
                float radius = 23f + ring * 17f;
                painter.BeginPath();
                for (int index = 0; index < segments; index++)
                {
                    float angle = index * Mathf.PI * 2f / segments;
                    float drift = 1f + 0.11f * Mathf.Cos(angle * 3f + phase)
                        + 0.07f * Mathf.Sin(angle * 5f - phase);
                    var point = center + new Vector2(Mathf.Cos(angle + phase) * 1.25f, Mathf.Sin(angle)) * radius * drift;
                    if (index == 0)
                    {
                        painter.MoveTo(point);
                    }
                    else
                    {
                        painter.LineTo(point);
                    }
                }

                painter.ClosePath();
                painter.Stroke();
            }
        }

        private static void DrawRoomMarker(MeshGenerationContext context, MapRoomKind kind)
        {
            var painter = context.painter2D;
            var center = context.visualElement.contentRect.center;
            Color32 tint = kind switch
            {
                MapRoomKind.Start => new Color32(201, 222, 179, 255),
                MapRoomKind.Event => new Color32(215, 211, 175, 255),
                MapRoomKind.Reward => new Color32(224, 190, 128, 255),
                MapRoomKind.RareReward => new Color32(239, 206, 147, 255),
                MapRoomKind.Boss => new Color32(221, 157, 135, 255),
                _ => new Color32(180, 200, 178, 255)
            };

            painter.lineWidth = 1.25f;
            painter.strokeColor = tint;
            switch (kind)
            {
                case MapRoomKind.Start:
                    DrawPolygon(painter, center, 3, 7f);
                    break;
                case MapRoomKind.Event:
                    DrawPolygon(painter, center, 8, 6f);
                    DrawLine(painter, center - new Vector2(0f, 3f), center + new Vector2(0f, 3f));
                    break;
                case MapRoomKind.Reward:
                    DrawBox(painter, center, 5.5f);
                    DrawLine(painter, center - new Vector2(5.5f, 0f), center + new Vector2(5.5f, 0f));
                    break;
                case MapRoomKind.RareReward:
                    DrawPolygon(painter, center, 4, 7f);
                    DrawPolygon(painter, center, 4, 3.8f);
                    break;
                case MapRoomKind.Boss:
                    DrawPolygon(painter, center, 6, 7f);
                    DrawLine(painter, center - new Vector2(2.5f, 0f), center + new Vector2(2.5f, 0f));
                    break;
                default:
                    DrawPolygon(painter, center, 4, 6f);
                    break;
            }
        }

        private static void DrawPolygon(Painter2D painter, Vector2 center, int sides, float radius)
        {
            painter.BeginPath();
            painter.MoveTo(center - new Vector2(0f, radius));
            for (int side = 1; side < sides; side++)
            {
                float angle = side * Mathf.PI * 2f / sides;
                painter.LineTo(center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * radius);
            }

            painter.ClosePath();
            painter.Stroke();
        }

        private static void DrawBox(Painter2D painter, Vector2 center, float halfSize)
        {
            painter.BeginPath();
            painter.MoveTo(center + new Vector2(-halfSize, -halfSize));
            painter.LineTo(center + new Vector2(halfSize, -halfSize));
            painter.LineTo(center + new Vector2(halfSize, halfSize));
            painter.LineTo(center + new Vector2(-halfSize, halfSize));
            painter.ClosePath();
            painter.Stroke();
        }

        private static void DrawLine(Painter2D painter, Vector2 from, Vector2 to)
        {
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        private static Vector2 GraphPoint(MapRoom room, float width)
        {
            return new Vector2(width * LanePercent(room.Column) / 100f, FloorY(room.Floor));
        }

        private void EnterRoom(MapRoom room)
        {
            if (_roomOperation != null || _run == null || !_run.EnterRoom(room.Id))
            {
                return;
            }

            OpenCurrentRoom();
        }

        private void OpenCurrentRoom()
        {
            if (_root == null || _run == null || _battleObject != null ||
                _roomOperation != null || _roomScene.IsValid())
            {
                return;
            }

            _roomNotice = null;
            if (_run.Phase == RunPhase.Encounter)
            {
                OpenEncounter();
            }
            else if (RoomEventScreen.Supports(_run.Phase))
            {
                OpenRoomScene();
            }
        }

        private void OpenRoomScene()
        {
            int sceneIndex = SceneUtility.GetBuildIndexByScenePath(RoomScenePath);
            if (sceneIndex < 0)
            {
                ShowRoomFailure();
                return;
            }

            // 地图保留 RunSession 和存档订阅，房间场景只借用本局状态显示内容。
            // 加载期间禁止对地图的重复操作，待房间能显示时再隐藏地图根元素。
            var requestedRun = _run;
            _mapShell.SetEnabled(false);
            _mapShell.AddToClassList("run-loading");
            try
            {
                _roomOperation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Additive);
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning(exception.Message, this);
            }

            if (_roomOperation == null)
            {
                ShowRoomFailure();
                return;
            }

            _roomOperation.completed += _ =>
            {
                _roomOperation = null;
                _roomScene = SceneManager.GetSceneByPath(RoomScenePath);
                if (!_roomScene.IsValid())
                {
                    if (_root != null)
                    {
                        ShowRoomFailure();
                    }
                    return;
                }

                if (_root == null || !ReferenceEquals(_run, requestedRun))
                {
                    CloseRoomScene();
                    return;
                }

                var screen = _roomScene.GetRootGameObjects()
                    .Select(root => root.GetComponentInChildren<RoomEventScreen>(true))
                    .FirstOrDefault(component => component != null);
                if (screen == null)
                {
                    _roomNotice = "房间界面加载失败。";
                    CloseRoomScene();
                    return;
                }

                _root.style.display = DisplayStyle.None;
                screen.Bind(requestedRun, CloseRoomScene);
            };
        }

        private void CloseRoomScene()
        {
            if (_roomOperation != null)
            {
                return;
            }

            if (!_roomScene.IsValid() || !_roomScene.isLoaded)
            {
                _roomScene = default;
                RestoreMap();
                return;
            }

            _roomOperation = SceneManager.UnloadSceneAsync(_roomScene);
            if (_roomOperation == null)
            {
                _roomScene = default;
                RestoreMap();
                return;
            }

            _roomOperation.completed += _ =>
            {
                _roomOperation = null;
                _roomScene = default;
                RestoreMap();
            };
        }

        private void RestoreMap()
        {
            if (_root == null)
            {
                return;
            }

            _root.style.display = DisplayStyle.Flex;
            Render(animate: true);
        }

        private void ShowRoomFailure()
        {
            _roomNotice = "无法打开房间界面，请重试。";
            RestoreMap();
        }

        private void OpenEncounter()
        {
            var room = _run.State.CurrentRoom;
            _root.style.display = DisplayStyle.None;
            _battleObject = new GameObject("Run Battle");
            _battleObject.SetActive(false);
            var battle = _battleObject.AddComponent<BattleScreen>();
            battle.BindEncounter(_run.Battle, RoomName(room.Kind), FinishEncounter, battleBackground, _run);
            _battleObject.SetActive(true);
        }

        private void FinishEncounter()
        {
            if (!_run.CompleteEncounter())
            {
                return;
            }

            _battleObject.SetActive(false);
            Destroy(_battleObject);
            _battleObject = null;
            RestoreMap();
            OpenCurrentRoom();
        }

        private void ReturnToMenu()
        {
            if (_roomOperation != null)
            {
                return;
            }

            // 退出不会创建新的保存节点。继续游戏重新读取最近的进出房间检查点。
            SetRun(null);
            _roomNotice = null;
            Render(animate: true);
        }

        private static string RoomName(MapRoomKind kind)
        {
            return kind switch
            {
                MapRoomKind.Start => "初始组",
                MapRoomKind.Reward => "奖励",
                MapRoomKind.Event => "事件",
                MapRoomKind.RareReward => "稀有奖励",
                MapRoomKind.Boss => "首领关卡",
                _ => "挑战"
            };
        }

        private static float FloorY(int floor) => BoardInset + (RunMap.FloorCount - floor) * FloorSpacing;

        private static float LanePercent(int lane) => FirstLanePercent + lane * LaneSpacingPercent;

        private static VisualElement Element(string classes, string name = null)
        {
            var element = new VisualElement { name = name };
            SetClasses(element, classes);
            return element;
        }

        private static Label Label(string text, string classes, string name = null)
        {
            var element = new Label(text) { name = name };
            SetClasses(element, classes);
            return element;
        }

        private static Button Button(string text, string classes, string name, Action action)
        {
            var button = new Button(action) { text = text, name = name };
            SetClasses(button, classes);
            return button;
        }

        private static void SetClasses(VisualElement element, string classes)
        {
            foreach (string className in classes.Split(' '))
            {
                element.AddToClassList(className);
            }
        }
    }
}
