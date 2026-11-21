using System;
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

        private readonly VisualElement[] _slots = new VisualElement[BattleSnapshot.QueueCapacity];
        private IBattleSession _session;
        private PanelSettings _ownedPanel;
        private Font _font;
        private VisualElement _screen;
        private VisualElement _scenarioButtons;
        private VisualElement _queue;
        private VisualElement _hand;
        private ScrollView _handScroll;
        private VisualElement _settlement;
        private VisualElement _dragLayer;
        private Label _round;
        private Label _benefit;
        private Label _pileCounts;
        private Label _feedback;
        private Label _settlementHeading;
        private Button _endTurn;
        private Button _finishChallenge;
        private Button _restart;
        private int _scenarioIndex;

        private int _dragPointer = NoPointer;
        private int _dragRevision;
        private int _dragSourceSlot;
        private Vector2 _dragStart;
        private CardInstance _dragCard;
        private VisualElement _dragGhost;
        private VisualElement _highlight;

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
            SetChineseFont();
            BindElements();
            BuildScenarioButtons();
            StartChallenge();
        }

        private void BindElements()
        {
            _scenarioButtons = _screen.Q<VisualElement>("scenario-buttons");
            _queue = _screen.Q<VisualElement>("queue");
            _hand = _screen.Q<VisualElement>("hand");
            _handScroll = _screen.Q<ScrollView>("hand-scroll");
            _settlement = _screen.Q<VisualElement>("settlement");
            _dragLayer = _screen.Q<VisualElement>("drag-layer");
            _dragLayer.pickingMode = PickingMode.Ignore;
            _round = _screen.Q<Label>("round");
            _benefit = _screen.Q<Label>("benefit");
            _pileCounts = _screen.Q<Label>("pile-counts");
            _feedback = _screen.Q<Label>("feedback");
            _settlementHeading = _screen.Q<Label>("settlement-heading");
            _endTurn = _screen.Q<Button>("end-turn");
            _finishChallenge = _screen.Q<Button>("finish-challenge");
            _restart = _screen.Q<Button>("restart");

            _endTurn.clicked += EndCurrentTurn;
            _finishChallenge.clicked += FinishCurrentChallenge;
            _restart.clicked += StartChallenge;
            _screen.RegisterCallback<PointerMoveEvent>(MoveDrag);
            _screen.RegisterCallback<PointerUpEvent>(EndDrag);
            _screen.RegisterCallback<PointerCancelEvent>(CancelDrag);
            _screen.RegisterCallback<PointerCaptureOutEvent>(LosePointer);
        }

        private void SetChineseFont()
        {
            if (!_font)
            {
                _font = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Microsoft YaHei", "Noto Sans SC", "Noto Sans CJK SC", "PingFang SC"
                }, 18);
            }

            if (_font)
            {
                _screen.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(_font));
            }
        }

        private void OnDisable()
        {
            if (_screen == null)
            {
                return;
            }

            ResetDrag();
            _endTurn.clicked -= EndCurrentTurn;
            _finishChallenge.clicked -= FinishCurrentChallenge;
            _restart.clicked -= StartChallenge;
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

            if (_font)
            {
                Destroy(_font);
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
            _session = new BattleSession(BattleDemoContent.CreateChallenge(scenario), seed);

            for (int index = 0; index < _scenarioButtons.childCount; index++)
            {
                _scenarioButtons[index].EnableInClassList("selected", index == _scenarioIndex);
            }

            _handScroll.scrollOffset = Vector2.zero;
            Render(_session.Snapshot);
        }

        private void EndCurrentTurn() => Submit(new EndTurn(), _session.Snapshot.Revision);
        private void FinishCurrentChallenge() => Submit(new FinishChallenge(), _session.Snapshot.Revision);

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
                _feedback.text = BattleScreenText.Error(result.Error);
            }
        }

        private void Render(BattleSnapshot snapshot)
        {
            bool planning = snapshot.Phase == BattlePhase.Planning;
            string benefitName = snapshot.Scenario.BenefitName;
            _round.text = $"第 {snapshot.Turn} / {snapshot.TurnLimit} 回合";
            _benefit.text = $"{benefitName}  {snapshot.Benefit} / {snapshot.BenefitTarget}";
            _pileCounts.text = $"抽牌堆 {snapshot.DrawCount}    弃牌堆 {snapshot.DiscardCount}";
            _feedback.text = snapshot.Phase switch
            {
                BattlePhase.Won => "挑战成功",
                BattlePhase.Lost => "挑战失败，回合已用完。",
                _ when snapshot.CanFinishEarly => "收益已达标，可提前完成挑战。",
                _ => DragHint
            };

            _endTurn.SetEnabled(planning);
            _finishChallenge.style.display = snapshot.CanFinishEarly ? DisplayStyle.Flex : DisplayStyle.None;

            _queue.Clear();
            for (int index = 0; index < snapshot.Queue.Count; index++)
            {
                var slot = new VisualElement { name = $"slot-{index}" };
                slot.AddToClassList("queue-slot");
                _slots[index] = slot;
                _queue.Add(slot);

                var card = snapshot.Queue[index];
                if (card == null)
                {
                    var placeholder = new Label($"槽位 {index + 1}") { pickingMode = PickingMode.Ignore };
                    placeholder.AddToClassList("slot-placeholder");
                    slot.Add(placeholder);
                }
                else
                {
                    slot.Add(CreateInteractiveCard(card, "queued-card", benefitName, index, planning));
                }
            }

            _hand.Clear();
            foreach (var card in snapshot.Hand)
            {
                _hand.Add(CreateInteractiveCard(card, "hand-card", benefitName, NoSlot, planning));
            }

            RenderSettlement(snapshot);
        }

        private void RenderSettlement(BattleSnapshot snapshot)
        {
            _settlement.Clear();
            _settlementHeading.text = snapshot.LastResolvedTurn == 0
                ? "结算记录"
                : $"第 {snapshot.LastResolvedTurn} 回合结算";

            if (snapshot.LastResolutions.Count == 0)
            {
                AddSettlementEntry(snapshot.LastResolvedTurn == 0 ? "尚未结算" : "本回合未排入卡牌");
                return;
            }

            foreach (var result in snapshot.LastResolutions)
            {
                string extra = result.CardsDrawn > 0 ? $" · 抽 {result.CardsDrawn} 张" : string.Empty;
                AddSettlementEntry($"{result.SlotIndex + 1}  {result.Card.Definition.Name}   +{result.BenefitGained}{extra}");
            }
        }

        private void AddSettlementEntry(string text)
        {
            var entry = new Label(text);
            entry.AddToClassList("settlement-entry");
            _settlement.Add(entry);
        }

        private VisualElement CreateInteractiveCard(
            CardInstance card, string className, string benefitName, int sourceSlot, bool planning)
        {
            var element = CreateCard(card, className, benefitName);
            if (planning)
            {
                element.RegisterCallback<PointerDownEvent>(evt => BeginDrag(evt, card, sourceSlot));
            }

            return element;
        }

        private static VisualElement CreateCard(CardInstance card, string className, string benefitName)
        {
            var element = new VisualElement { name = $"card-{card.InstanceId}" };
            element.AddToClassList("battle-card");
            element.AddToClassList(className);

            var label = new Label(card.Definition.Name) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("card-name");
            element.Add(label);

            foreach (var effect in card.Definition.Effects)
            {
                var line = new Label(BattleScreenText.Effect(effect, benefitName)) { pickingMode = PickingMode.Ignore };
                line.AddToClassList("card-effect");
                element.Add(line);
            }

            return element;
        }

        private void BeginDrag(PointerDownEvent evt, CardInstance card, int sourceSlot)
        {
            if (evt.button != 0 || _dragPointer != NoPointer)
            {
                return;
            }

            // 保存拖拽开始时的版本，落点确认后才提交命令。
            _dragPointer = evt.pointerId;
            _dragStart = evt.position;
            _dragCard = card;
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
                _dragGhost = CreateCard(_dragCard, "drag-ghost", _session.Snapshot.Scenario.BenefitName);
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
