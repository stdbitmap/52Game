using System.Collections.Generic;
using System.Linq;
using Game.Content;
using Game.Contracts;
using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UnityUI
{
    /// <summary>将战斗快照显示在 UI Toolkit 上，并把玩家操作提交给战斗会话。</summary>
    [DisallowMultipleComponent]
    public sealed class BattleScreen : MonoBehaviour
    {
        private const int NoSlot = -1;
        private const int NoPointer = -1;
        private const float DragThreshold = 6f;
        private const string DragHint = "将手牌拖到空槽，拖回手牌区可撤回。";

        [SerializeField] private int seed = 52;
        [SerializeField] private Texture2D backgroundArtwork = null;

        private readonly VisualElement[] _slots = new VisualElement[BattleSnapshot.QueueCapacity];
        private readonly Dictionary<string, PlayerDeck> _scenarioDecks = new Dictionary<string, PlayerDeck>();
        private readonly Dictionary<string, Sprite> _artworkByAddress = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, TextAsset> _descriptionByAddress = new Dictionary<string, TextAsset>();
        private readonly System.Random _pileDisplayRandom = new System.Random();
        private IBattleSession _session;
        private PanelSettings _ownedPanel;
        private UiTypography _typography;
        private VisualElement _screen;
        private string _themeClass;
        private VisualElement _battleShell;
        private VisualElement _scenarioButtons;
        private VisualElement _queue;
        private VisualElement _hand;
        private ScrollView _handScroll;
        private VisualElement _settlement;
        private VisualElement _dragLayer;
        private Label _round;
        private VisualElement _roundSteps;
        private Label _benefitCaption;
        private Label _benefit;
        private Label _benefitTarget;
        private VisualElement _benefitFill;
        private Label _queueCount;
        private Label _handCount;
        private Button _drawPileButton;
        private Button _discardPileButton;
        private VisualElement _pileInspector;
        private ScrollView _pileScroll;
        private VisualElement _pileContents;
        private Label _pileTitle;
        private Button _closePileButton;
        private Button _inspectDrawButton;
        private Label _insightValue;
        private Label _feedback;
        private string _feedbackMessage;
        private bool _feedbackIsError;
        private Label _settlementHeading;
        private Label _settlementTotal;
        private int _lastAnimatedResolvedTurn;
        private int _lastAnimatedImmediateCount;
        private Button _endTurn;
        private Button _finishChallenge;
        private Button _restart;
        private Button _continue;
        private System.Action _onEncounterFinished;
        private string _encounterTitle;
        private Texture2D _encounterBackground;
        private RunSession _encounterRun;
        private int _scenarioIndex;

        private int _dragPointer = NoPointer;
        private int _dragRevision;
        private int _dragSourceSlot;
        private Vector2 _dragStart;
        private CardInstance _dragCard;
        private VisualElement _dragSource;
        private VisualElement _dragGhost;
        private VisualElement _highlight;

        /// <summary>在启用本组件前传入地图房间的战斗，沿用该局牌组和随机序列。</summary>
        public void BindEncounter(
            IBattleSession session, string title, System.Action onFinished,
            Texture2D encounterBackground = null, RunSession encounterRun = null)
        {
            _session = session ?? throw new System.ArgumentNullException(nameof(session));
            _encounterTitle = title;
            _onEncounterFinished = onFinished ?? throw new System.ArgumentNullException(nameof(onFinished));
            _encounterBackground = encounterBackground;
            _encounterRun = encounterRun;
        }

        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            if (!document)
            {
                document = gameObject.AddComponent<UIDocument>();
            }

            if (!document.panelSettings)
            {
                _ownedPanel = ScriptableObject.CreateInstance<PanelSettings>();
                _ownedPanel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("BattleTheme");
                _ownedPanel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                _ownedPanel.referenceResolution = new Vector2Int(1280, 720);
                _ownedPanel.screenMatchMode = PanelScreenMatchMode.Expand;
                document.panelSettings = _ownedPanel;
            }

            if (!document.visualTreeAsset)
            {
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("BattleView");
            }

            _screen = document.rootVisualElement?.Q<VisualElement>("battle-screen");
            if (_screen == null)
            {
                Debug.LogError("BattleView.uxml is missing the battle-screen element.", this);
                return;
            }

            document.rootVisualElement.style.flexGrow = 1;
            _typography ??= new UiTypography();
            _typography.ApplyBody(_screen);
            BindElements();
            var screen = _screen;
            screen.AddToClassList("is-entering");
            // 给样式系统一帧应用起始位置，下一帧开始过渡。
            screen.schedule.Execute(() => screen.RemoveFromClassList("is-entering")).ExecuteLater(35);

            // 可在场景中指定背景图。资源留空时保留界面的默认底色和遮罩。
            var artwork = _encounterBackground ? _encounterBackground : backgroundArtwork;
            if (artwork)
            {
                var backdrop = _screen.Q<VisualElement>("battle-backdrop");
                backdrop.style.backgroundImage = new StyleBackground(artwork);
            }

            if (_onEncounterFinished == null)
            {
                BuildScenarioButtons();
                StartChallenge();
                return;
            }

            _screen.Q<Label>("battle-title").text = _encounterTitle;
            if (_encounterRun != null)
            {
                var floor = _screen.Q<Label>("mission-floor");
                floor.text = $"第 {_encounterRun.State.Floor:00} 层";
                floor.style.display = DisplayStyle.Flex;
            }

            _scenarioButtons.style.display = DisplayStyle.None;
            _restart.style.display = DisplayStyle.None;
            Render(_session.Snapshot);
        }

        private void BindElements()
        {
            _battleShell = _screen.Q<VisualElement>("battle-shell");
            _screen.Q<VisualElement>("arena-grid").generateVisualContent += DrawArena;
            _scenarioButtons = _screen.Q<VisualElement>("scenario-buttons");
            _queue = _screen.Q<VisualElement>("queue");
            _hand = _screen.Q<VisualElement>("hand");
            _handScroll = _screen.Q<ScrollView>("hand-scroll");
            _settlement = _screen.Q<VisualElement>("settlement");
            _dragLayer = _screen.Q<VisualElement>("drag-layer");
            _dragLayer.pickingMode = PickingMode.Ignore;
            _round = _screen.Q<Label>("round");
            _roundSteps = _screen.Q<VisualElement>("round-steps");
            _benefitCaption = _screen.Q<Label>("benefit-caption");
            _benefit = _screen.Q<Label>("benefit");
            _benefitTarget = _screen.Q<Label>("benefit-target");
            _benefitFill = _screen.Q<VisualElement>("benefit-fill");
            _queueCount = _screen.Q<Label>("queue-count");
            _handCount = _screen.Q<Label>("hand-count");
            _drawPileButton = _screen.Q<Button>("draw-pile-button");
            _discardPileButton = _screen.Q<Button>("discard-pile-button");
            _pileInspector = _screen.Q<VisualElement>("pile-inspector");
            _pileScroll = _screen.Q<ScrollView>("pile-scroll");
            _pileContents = _screen.Q<VisualElement>("pile-contents");
            _pileTitle = _screen.Q<Label>("pile-title");
            _closePileButton = _screen.Q<Button>("close-pile-button");
            _inspectDrawButton = _screen.Q<Button>("inspect-draw-order");
            _insightValue = _screen.Q<Label>("battle-insight");
            _feedback = _screen.Q<Label>("feedback");
            _settlementHeading = _screen.Q<Label>("settlement-heading");
            _settlementTotal = _screen.Q<Label>("settlement-total");
            _endTurn = _screen.Q<Button>("end-turn");
            _finishChallenge = _screen.Q<Button>("finish-challenge");
            _restart = _screen.Q<Button>("restart");
            _continue = _screen.Q<Button>("continue-run");

            _typography.ApplyHudNumber(_benefit);
            _typography.ApplyHudNumber(_settlementTotal);

            _endTurn.clicked += EndCurrentTurn;
            _finishChallenge.clicked += FinishCurrentChallenge;
            _restart.clicked += StartChallenge;
            _continue.clicked += ContinueRun;
            _drawPileButton.clicked += OpenDrawPile;
            _discardPileButton.clicked += OpenDiscardPile;
            _closePileButton.clicked += ClosePile;
            _inspectDrawButton.clicked += InspectDrawOrder;
            _screen.RegisterCallback<GeometryChangedEvent>(UpdateLayout);
            _screen.RegisterCallback<PointerMoveEvent>(MoveDrag);
            _screen.RegisterCallback<PointerUpEvent>(EndDrag);
            _screen.RegisterCallback<PointerCancelEvent>(CancelDrag);
            _screen.RegisterCallback<PointerCaptureOutEvent>(LosePointer);
        }

        private static void DrawArena(MeshGenerationContext context)
        {
            // 用一块透视网格承载五个操作位，格线根据当前面板大小计算，所以不会挡住拖拽。
            Rect bounds = context.visualElement.contentRect;
            float width = bounds.width;
            float height = bounds.height;
            if (width <= 0f || height <= 0f)
            {
                return;
            }

            var painter = context.painter2D;
            Vector2 topLeft = new Vector2(width * 0.22f, height * 0.09f);
            Vector2 topRight = new Vector2(width * 0.78f, height * 0.09f);
            Vector2 bottomLeft = new Vector2(width * -0.15f, height);
            Vector2 bottomRight = new Vector2(width * 1.15f, height);

            painter.fillColor = new Color(0.20f, 0.29f, 0.33f, 0.78f);
            painter.BeginPath();
            painter.MoveTo(topLeft);
            painter.LineTo(topRight);
            painter.LineTo(bottomRight);
            painter.LineTo(bottomLeft);
            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = new Color(0.54f, 0.68f, 0.68f, 0.31f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            for (int column = 0; column <= 12; column++)
            {
                float position = column / 12f;
                painter.MoveTo(Vector2.Lerp(topLeft, topRight, position));
                painter.LineTo(Vector2.Lerp(bottomLeft, bottomRight, position));
            }

            for (int row = 0; row <= 8; row++)
            {
                // 远处横线更密，近处间距扩大。
                float position = row / 8f;
                position *= position;
                painter.MoveTo(Vector2.Lerp(topLeft, bottomLeft, position));
                painter.LineTo(Vector2.Lerp(topRight, bottomRight, position));
            }
            painter.Stroke();
        }

        private void UpdateLayout(GeometryChangedEvent evt)
        {
            // UI Toolkit 没有媒体查询。用实际面板尺寸保留牌坞和卡槽的操作空间。
            _screen.EnableInClassList("is-compact", evt.newRect.width <= 1100f);
            _screen.EnableInClassList("is-short", evt.newRect.height <= 640f);
        }

        private void OnDisable()
        {
            if (_screen == null)
            {
                return;
            }

            ResetDrag();
            ClosePile();
            _endTurn.clicked -= EndCurrentTurn;
            _finishChallenge.clicked -= FinishCurrentChallenge;
            _restart.clicked -= StartChallenge;
            _continue.clicked -= ContinueRun;
            _drawPileButton.clicked -= OpenDrawPile;
            _discardPileButton.clicked -= OpenDiscardPile;
            _closePileButton.clicked -= ClosePile;
            _inspectDrawButton.clicked -= InspectDrawOrder;
            _screen.Q<VisualElement>("arena-grid").generateVisualContent -= DrawArena;
            _screen.UnregisterCallback<GeometryChangedEvent>(UpdateLayout);
            _screen.UnregisterCallback<PointerMoveEvent>(MoveDrag);
            _screen.UnregisterCallback<PointerUpEvent>(EndDrag);
            _screen.UnregisterCallback<PointerCancelEvent>(CancelDrag);
            _screen.UnregisterCallback<PointerCaptureOutEvent>(LosePointer);
            _screen = null;
        }

        private void OnDestroy()
        {
            if (_ownedPanel)
            {
                Destroy(_ownedPanel);
            }
        }

        private void BuildScenarioButtons()
        {
            _scenarioButtons.Clear();
            for (int index = 0; index < BattleDemoContent.Scenarios.Count; index++)
            {
                int scenarioIndex = index;
                var scenario = BattleDemoContent.Scenarios[index];
                var button = new Button(() => SelectScenario(scenarioIndex))
                {
                    name = "scenario-" + scenario.Id,
                    text = scenario.Name
                };
                button.AddToClassList("scenario-button");
                _scenarioButtons.Add(button);
            }
        }

        private void SelectScenario(int index)
        {
            if (index != _scenarioIndex)
            {
                _scenarioIndex = index;
                StartChallenge();
            }
        }

        private void StartChallenge()
        {
            ResetDrag();
            var scenario = BattleDemoContent.Scenarios[_scenarioIndex];
            var definition = BattleDemoContent.CreateChallenge(scenario);

            // 重开只创建新的局内会话，同一剧本继续使用已有的局外牌组。
            if (!_scenarioDecks.TryGetValue(scenario.Id, out var deck))
            {
                deck = new PlayerDeck(definition.StartingDeck);
                _scenarioDecks.Add(scenario.Id, deck);
            }

            _session = new BattleSession(definition, seed, deck);

            for (int index = 0; index < _scenarioButtons.childCount; index++)
            {
                _scenarioButtons[index].EnableInClassList("selected", index == _scenarioIndex);
            }

            _handScroll.scrollOffset = Vector2.zero;
            Render(_session.Snapshot);
        }

        private void EndCurrentTurn() => Submit(new EndTurn(), _session.Snapshot.Revision);
        private void FinishCurrentChallenge() => Submit(new FinishChallenge(), _session.Snapshot.Revision);

        private void ContinueRun()
        {
            if (_session.Snapshot.Phase != BattlePhase.Planning)
            {
                var onFinished = _onEncounterFinished;
                _onEncounterFinished = null;
                onFinished?.Invoke();
            }
        }

        private void Submit(BattleCommand command, int revision)
        {
            ResetDrag();
            var result = _session.Execute(command, revision);
            if (result.Accepted)
            {
                Render(result.Snapshot);
            }
            else
            {
                // 拒绝的命令不改布局，可保留手牌滚动位置并显示具体原因。
                SetFeedback(BattleScreenText.Error(result.Error), isError: true);
            }
        }

        private void Render(BattleSnapshot snapshot)
        {
            ClosePile();
            bool planning = snapshot.Phase == BattlePhase.Planning;
            RenderHud(snapshot);
            _drawPileButton.text = $"抽牌堆 {snapshot.DrawCount}";
            _discardPileButton.text = $"弃牌堆 {snapshot.DiscardCount}";
            SetFeedback(snapshot.Phase switch
            {
                BattlePhase.Won => "挑战成功",
                BattlePhase.Lost => "挑战失败，回合已用完。",
                _ when snapshot.CanFinishEarly => "收益已达标，可提前完成挑战。",
                _ when snapshot.ImmediateResolutions.Count > 0 => "馈赠已即时结算，这张牌已移出局内和局外牌组。",
                _ => DragHint
            });

            _endTurn.SetEnabled(planning);
            bool canContinue = !planning && _onEncounterFinished != null;
            _continue.style.display = canContinue ? DisplayStyle.Flex : DisplayStyle.None;
            _endTurn.style.display = canContinue ? DisplayStyle.None : DisplayStyle.Flex;
            _finishChallenge.style.display = snapshot.CanFinishEarly ? DisplayStyle.Flex : DisplayStyle.None;

            _queue.Clear();
            int occupiedSlots = 0;
            for (int index = 0; index < snapshot.Queue.Count; index++)
            {
                var slot = new VisualElement { name = $"slot-{index}" };
                slot.AddToClassList("queue-slot");
                bool last = index == snapshot.Queue.Count - 1;
                slot.EnableInClassList("is-last", last);
                _slots[index] = slot;
                _queue.Add(slot);

                var slotIndex = CreateViewLabel($"{index + 1:00}", "slot-index");
                slotIndex.name = "slot-index";
                slot.Add(slotIndex);

                var card = snapshot.Queue[index];
                if (card == null)
                {
                    var outline = new VisualElement { name = "slot-outline", pickingMode = PickingMode.Ignore };
                    outline.AddToClassList("slot-outline");
                    outline.Add(CreateViewLabel("+", "slot-crosshair"));
                    slot.Add(outline);
                }
                else
                {
                    occupiedSlots++;
                    slot.AddToClassList("is-occupied");
                    slot.Add(CreateInteractiveCard(card, "queued-card", index, snapshot));
                }

                if (!last)
                {
                    // 箭头只表示结算方向，拖放命中仍由整张卡槽的边界判断。
                    slot.Add(CreateViewLabel("›", "slot-connector"));
                }
            }

            _queueCount.text = $"{occupiedSlots} / {snapshot.Queue.Count}";
            _handCount.text = $"{snapshot.Hand.Count} 张";
            _hand.Clear();
            foreach (var card in snapshot.Hand)
            {
                _hand.Add(CreateInteractiveCard(card, "hand-card", NoSlot, snapshot));
            }

            RenderSettlement(snapshot);
        }

        private void RenderHud(BattleSnapshot snapshot)
        {
            string themeClass = $"theme-{snapshot.Scenario.Id}";
            if (_themeClass != null && _themeClass != themeClass)
            {
                _screen.RemoveFromClassList(_themeClass);
            }

            _screen.AddToClassList(themeClass);
            _themeClass = themeClass;

            _screen.EnableInClassList("state-won", snapshot.Phase == BattlePhase.Won);
            _screen.EnableInClassList("state-lost", snapshot.Phase == BattlePhase.Lost);
            bool goalReached = snapshot.BenefitTarget > 0 && snapshot.Benefit >= snapshot.BenefitTarget;
            _screen.EnableInClassList("goal-reached", goalReached);
            _benefitFill.EnableInClassList("goal-reached", goalReached);

            UpdateInsightValue();
            _benefitCaption.text = snapshot.Scenario.BenefitName;
            _benefit.text = snapshot.Benefit.ToString();
            _benefitTarget.text = $"/ {snapshot.BenefitTarget}";
            float progress = snapshot.BenefitTarget > 0
                ? Mathf.Clamp01((float)((double)snapshot.Benefit / snapshot.BenefitTarget))
                : 0f;
            _benefitFill.style.width = Length.Percent(progress * 100f);

            _round.text = $"第 {snapshot.Turn} / {snapshot.TurnLimit} 回合";
            _roundSteps.Clear();
            for (int index = 0; index < snapshot.TurnLimit; index++)
            {
                int turn = index + 1;
                var step = new VisualElement { pickingMode = PickingMode.Ignore };
                step.AddToClassList("round-step");
                step.EnableInClassList("is-past", turn < snapshot.Turn);
                step.EnableInClassList("is-current", turn == snapshot.Turn);
                _roundSteps.Add(step);
            }
        }

        private void UpdateInsightValue()
        {
            if (_encounterRun != null)
            {
                _insightValue.style.display = DisplayStyle.Flex;
                _insightValue.text = $"Insight {_encounterRun.State.Insight}";
            }
        }

        private void OpenDrawPile()
        {
            var snapshot = _session.Snapshot;
            OpenPile("抽牌堆", snapshot.DrawCount, snapshot.DrawPileContents);
            if (_encounterRun != null && snapshot.DrawCount > 0 && snapshot.Phase == BattlePhase.Planning)
            {
                _inspectDrawButton.style.display = DisplayStyle.Flex;
                _inspectDrawButton.SetEnabled(_encounterRun.State.Insight > 0);
            }
        }

        private void InspectDrawOrder()
        {
            if (_encounterRun == null || !_encounterRun.TryInspectDrawPile(out var orderedCards))
            {
                return;
            }

            _inspectDrawButton.style.display = DisplayStyle.None;
            _pileTitle.text = "抽牌顺序";
            _pileContents.Clear();
            for (int index = 0; index < orderedCards.Count; index++)
            {
                var entry = new VisualElement();
                entry.AddToClassList("pile-card-entry");
                entry.Add(CreateViewLabel($"{index + 1:00}  {orderedCards[index].Name}", "pile-card-name"));
                if (index == 0)
                {
                    entry.Add(CreateViewLabel("下一张", "pile-card-count"));
                }

                _pileContents.Add(entry);
            }

            UpdateInsightValue();
            _pileScroll.scrollOffset = Vector2.zero;
            _closePileButton.Focus();
        }

        private void OpenDiscardPile()
        {
            var snapshot = _session.Snapshot;
            OpenPile("弃牌堆", snapshot.DiscardCount, snapshot.DiscardPileContents);
        }

        private void OpenPile(string title, int count, IReadOnlyList<PileCardSummary> cards)
        {
            ResetDrag();
            _inspectDrawButton.style.display = DisplayStyle.None;
            _pileTitle.text = $"{title} · {count} 张";
            _pileContents.Clear();

            // 只对查看用的计数副本洗牌，界面随机结果不会改变战斗牌堆。
            var shuffled = new List<PileCardSummary>(cards);
            for (int index = shuffled.Count - 1; index > 0; index--)
            {
                int other = _pileDisplayRandom.Next(index + 1);
                (shuffled[index], shuffled[other]) = (shuffled[other], shuffled[index]);
            }

            if (shuffled.Count == 0)
            {
                var empty = new Label("牌堆为空") { name = "pile-empty" };
                empty.AddToClassList("pile-empty");
                _pileContents.Add(empty);
            }
            else
            {
                foreach (var card in shuffled)
                {
                    var entry = new VisualElement();
                    entry.AddToClassList("pile-card-entry");
                    entry.Add(CreateViewLabel(card.Definition.Name, "pile-card-name"));
                    entry.Add(CreateViewLabel($"× {card.Count}", "pile-card-count"));
                    _pileContents.Add(entry);
                }
            }

            _battleShell.SetEnabled(false);
            _pileInspector.style.display = DisplayStyle.Flex;
            var inspector = _pileInspector;
            inspector.schedule.Execute(() =>
            {
                if (inspector.style.display.value == DisplayStyle.Flex)
                {
                    inspector.AddToClassList("is-visible");
                }
            }).ExecuteLater(35);
            _pileScroll.scrollOffset = Vector2.zero;
            _closePileButton.Focus();
        }

        private void ClosePile()
        {
            _pileInspector.RemoveFromClassList("is-visible");
            _pileInspector.style.display = DisplayStyle.None;
            _pileContents.Clear();
            _battleShell.SetEnabled(true);
        }

        private void SetFeedback(string message, bool isError = false)
        {
            _feedbackMessage = message;
            _feedbackIsError = isError;
            RestoreFeedback();
        }

        private void RestoreFeedback()
        {
            _feedback.text = _feedbackMessage;
            _feedback.EnableInClassList("is-error", _feedbackIsError);
        }

        private void RenderSettlement(BattleSnapshot snapshot)
        {
            _settlement.Clear();
            var currentResults = snapshot.ImmediateResolutions.Count > 0
                ? snapshot.ImmediateResolutions
                : snapshot.LastResolutions;
            _settlementTotal.text = $"+{currentResults.Sum(result => result.BenefitGained)}";
            if (currentResults.Count > 0 &&
                (_lastAnimatedResolvedTurn != snapshot.LastResolvedTurn ||
                 _lastAnimatedImmediateCount != snapshot.ImmediateResolutions.Count))
            {
                var track = _settlement;
                track.AddToClassList("is-updating");
                track.schedule.Execute(() => track.RemoveFromClassList("is-updating")).ExecuteLater(35);
            }
            _lastAnimatedResolvedTurn = snapshot.LastResolvedTurn;
            _lastAnimatedImmediateCount = snapshot.ImmediateResolutions.Count;
            string previousHeading = snapshot.LastResolvedTurn == 0
                ? "结算记录"
                : $"第 {snapshot.LastResolvedTurn} 回合结算";

            if (snapshot.ImmediateResolutions.Count > 0)
            {
                // 即时结算与上一轮记录分别显示，避免还在摆牌时丢失上一轮的信息。
                _settlementHeading.text = $"第 {snapshot.Turn} 回合即时结算";
                foreach (var result in snapshot.ImmediateResolutions)
                {
                    AddSettlementEntry(result);
                }

                if (snapshot.LastResolvedTurn == 0)
                {
                    return;
                }

                _settlement.Add(CreateViewLabel(previousHeading, "settlement-group"));
            }
            else
            {
                _settlementHeading.text = previousHeading;
            }

            if (snapshot.LastResolutions.Count == 0)
            {
                string message = snapshot.LastResolvedTurn == 0 ? "尚未结算" : "本回合未排入卡牌";
                _settlement.Add(CreateViewLabel(message, "settlement-empty"));
                return;
            }

            foreach (var result in snapshot.LastResolutions)
            {
                AddSettlementEntry(result);
            }
        }

        private void AddSettlementEntry(CardResolution result)
        {
            var entry = new VisualElement();
            entry.AddToClassList("settlement-entry");
            entry.Add(CreateViewLabel($"{result.SlotIndex + 1:00}", "settlement-index"));

            var detail = new VisualElement();
            detail.AddToClassList("settlement-detail");
            detail.Add(CreateViewLabel(result.Card.Definition.Name, "settlement-card-name"));
            string meta = BattleScreenText.Keywords(result.Card.Definition);
            if (result.CardsDrawn > 0)
            {
                string draw = $"抽 {result.CardsDrawn} 张";
                meta = string.IsNullOrEmpty(meta) ? draw : $"{meta} · {draw}";
            }

            if (!string.IsNullOrEmpty(meta))
            {
                detail.Add(CreateViewLabel(meta, "settlement-meta"));
            }

            entry.Add(detail);
            entry.Add(CreateViewLabel($"+{result.BenefitGained}", "settlement-gain"));
            _settlement.Add(entry);
        }

        private static Label CreateViewLabel(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }

        private VisualElement CreateInteractiveCard(
            CardInstance card, string className, int sourceSlot, BattleSnapshot snapshot)
        {
            var element = CreateCard(card, className, snapshot);
            if (snapshot.Phase == BattlePhase.Planning)
            {
                element.RegisterCallback<PointerDownEvent>(evt => BeginDrag(evt, card, sourceSlot));
            }

            var definition = card.Definition;
            if (definition.Keywords != CardKeyword.None || !string.IsNullOrEmpty(definition.DescriptionAddress))
            {
                element.RegisterCallback<PointerEnterEvent>(_ => ShowCardHelp(definition));
                element.RegisterCallback<PointerLeaveEvent>(_ => RestoreFeedback());
            }

            return element;
        }

        private void ShowCardHelp(CardDefinition definition)
        {
            if (_dragPointer != NoPointer)
            {
                return;
            }

            // 附加说明只在悬停时加载，卡面仍始终显示效果。
            string description = LoadCardResource(definition.DescriptionAddress, _descriptionByAddress)?.text.Trim();
            string keywords = BattleScreenText.Keywords(definition, detailed: true);
            string help = string.IsNullOrEmpty(description) ? keywords
                : string.IsNullOrEmpty(keywords) ? description : $"{keywords}；{description}";
            if (!string.IsNullOrEmpty(help))
            {
                _feedback.text = help;
                _feedback.RemoveFromClassList("is-error");
            }
        }

        private static T LoadCardResource<T>(string address, Dictionary<string, T> resources) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            if (!resources.TryGetValue(address, out var resource))
            {
                // 地址是 Resources 下不带扩展名的资源键，缺失结果也保留以免重复加载。
                resource = Resources.Load<T>(address);
                resources.Add(address, resource);
            }

            return resource;
        }

        private VisualElement CreateCard(CardInstance card, string className, BattleSnapshot snapshot)
        {
            var element = new VisualElement { name = $"card-{card.InstanceId}" };
            element.AddToClassList("battle-card");
            element.AddToClassList(className);

            var definition = card.Definition;
            bool emergency = definition.HasKeyword(CardKeyword.Emergency);
            element.EnableInClassList("keyword-gift", definition.HasKeyword(CardKeyword.Gift));
            element.EnableInClassList("keyword-exhaust", definition.HasKeyword(CardKeyword.Exhaust));
            element.EnableInClassList("keyword-emergency", emergency);
            element.EnableInClassList("is-locked", emergency && snapshot.Turn < snapshot.TurnLimit);

            var label = CreateViewLabel(definition.Name, "card-name");
            var artwork = LoadCardResource(definition.ArtworkAddress, _artworkByAddress);
            if (artwork)
            {
                var header = new VisualElement { pickingMode = PickingMode.Ignore };
                header.AddToClassList("card-header");
                var image = new Image
                {
                    name = "card-artwork",
                    sprite = artwork,
                    scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore
                };
                image.AddToClassList("card-artwork");
                header.Add(label);
                header.Add(image);
                element.Add(header);
            }
            else
            {
                element.Add(label);
            }

            if (definition.Keywords != CardKeyword.None)
            {
                var keywords = CreateViewLabel(BattleScreenText.Keywords(definition), "card-keywords");
                keywords.name = "card-keywords";
                element.Add(keywords);
            }

            foreach (var effect in definition.Effects)
            {
                element.Add(CreateViewLabel(BattleScreenText.Effect(effect, snapshot.Scenario.BenefitName), "card-effect"));
            }

            return element;
        }

        private void BeginDrag(PointerDownEvent evt, CardInstance card, int sourceSlot)
        {
            if (evt.button != 0 || _dragPointer != NoPointer || !_battleShell.enabledInHierarchy)
            {
                return;
            }

            // 保存拖拽开始时的版本，落点确认后才提交命令。
            _dragPointer = evt.pointerId;
            _dragStart = evt.position;
            _dragCard = card;
            _dragSource = evt.currentTarget as VisualElement;
            _dragSourceSlot = sourceSlot;
            _dragRevision = _session.Snapshot.Revision;
            _screen.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void MoveDrag(PointerMoveEvent evt)
        {
            if (evt.pointerId != _dragPointer)
            {
                return;
            }

            Vector2 position = evt.position;
            if (_dragGhost == null && (position - _dragStart).sqrMagnitude >= DragThreshold * DragThreshold)
            {
                _dragGhost = CreateCard(_dragCard, "drag-ghost", _session.Snapshot);
                _dragSource?.AddToClassList("is-drag-source");
                _screen.AddToClassList("is-dragging");
                _dragGhost.name = "drag-ghost";
                _dragGhost.pickingMode = PickingMode.Ignore;
                _dragLayer.Add(_dragGhost);
            }

            if (_dragGhost != null)
            {
                var local = _dragLayer.WorldToLocal(position);
                _dragGhost.style.left = local.x + 12;
                _dragGhost.style.top = local.y + 12;

                int slotIndex = SlotUnder(position);
                VisualElement target = slotIndex != NoSlot && _session.Snapshot.Queue[slotIndex] == null
                    ? _slots[slotIndex]
                    : _dragSourceSlot != NoSlot && _handScroll.worldBound.Contains(position) ? _handScroll : null;
                Highlight(target);
            }

            evt.StopPropagation();
        }

        private void EndDrag(PointerUpEvent evt)
        {
            if (evt.pointerId != _dragPointer || evt.button != 0)
            {
                return;
            }

            Vector2 position = evt.position;
            bool moved = _dragGhost != null || (position - _dragStart).sqrMagnitude >= DragThreshold * DragThreshold;
            int targetSlot = SlotUnder(position);
            BattleCommand command = null;
            if (moved && targetSlot != NoSlot && targetSlot != _dragSourceSlot)
            {
                command = new PlaceCard(_dragCard.InstanceId, targetSlot);
            }
            else if (moved && _dragSourceSlot != NoSlot && _handScroll.worldBound.Contains(position))
            {
                command = new ReturnCard(_dragCard.InstanceId);
            }

            int revision = _dragRevision;
            ResetDrag();
            if (command != null)
            {
                Submit(command, revision);
            }
            evt.StopPropagation();
        }

        private void CancelDrag(PointerCancelEvent evt)
        {
            if (evt.pointerId == _dragPointer)
            {
                ResetDrag();
            }
        }

        private void LosePointer(PointerCaptureOutEvent evt)
        {
            if (evt.pointerId == _dragPointer)
            {
                ResetDrag();
            }
        }

        private int SlotUnder(Vector2 position)
        {
            for (int index = 0; index < _slots.Length; index++)
            {
                if (_slots[index].worldBound.Contains(position))
                {
                    return index;
                }
            }

            return NoSlot;
        }

        private void Highlight(VisualElement target)
        {
            if (target == _highlight)
            {
                return;
            }

            _highlight?.RemoveFromClassList("drop-target");
            _highlight = target;
            _highlight?.AddToClassList("drop-target");
        }

        private void ResetDrag()
        {
            int pointer = _dragPointer;
            _dragPointer = NoPointer;
            _dragCard = null;
            _dragSource?.RemoveFromClassList("is-drag-source");
            _dragSource = null;
            _screen?.RemoveFromClassList("is-dragging");
            _dragGhost?.RemoveFromHierarchy();
            _dragGhost = null;
            Highlight(null);

            if (pointer != NoPointer && _screen != null && _screen.HasPointerCapture(pointer))
            {
                _screen.ReleasePointer(pointer);
            }
        }
    }
}
