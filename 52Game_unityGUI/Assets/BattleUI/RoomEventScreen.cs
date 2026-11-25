using System;
using System.Globalization;
using System.Linq;
using Game.Contracts;
using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UnityUI
{
    /// <summary>独立显示房间的选项与结算结果，使用地图会话的同一份运营状态。</summary>
    [DisallowMultipleComponent]
    public sealed class RoomEventScreen : MonoBehaviour
    {
        private RunSession _run;
        private Action _onReturn;
        private RoomChoiceResult _result;
        private string _choiceNotice;
        private VisualElement _root;
        private ScrollView _options;
        private PanelSettings _ownedPanel;
        private UiTypography _typography;

        public static bool Supports(RunPhase phase) =>
            phase is RunPhase.ChooseStarter or RunPhase.ChooseEvent or RunPhase.ChooseReward;

        /// <summary>地图以 Additive 模式载入本场景后传入原会话。继续时由调用方卸载本场景。</summary>
        public void Bind(RunSession run, Action onReturn)
        {
            if (run == null)
            {
                throw new ArgumentNullException(nameof(run));
            }

            if (!Supports(run.Phase))
            {
                throw new InvalidOperationException($"The room interface cannot display phase {run.Phase}.");
            }

            _onReturn = onReturn ?? throw new ArgumentNullException(nameof(onReturn));
            _run = run;
            Render(animate: true);
        }

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
                _ownedPanel.sortingOrder = 20;
                document.panelSettings = _ownedPanel;
            }

            var documentRoot = document.rootVisualElement;
            if (documentRoot == null)
            {
                Debug.LogError("RoomEventScreen requires a runtime UI Toolkit panel.", this);
                return;
            }

            documentRoot.style.flexGrow = 1;
            documentRoot.pickingMode = PickingMode.Ignore;
            _root = Element("room-event-screen", "room-event-screen");
            _root.style.display = DisplayStyle.None;
            documentRoot.Add(_root);
            var sheet = Resources.Load<StyleSheet>("RoomEvent");
            if (sheet != null)
            {
                _root.styleSheets.Add(sheet);
            }

            _typography ??= new UiTypography();
            _typography.ApplyBody(_root);
            Render(animate: true);
        }

        private void OnDisable()
        {
            _root?.RemoveFromHierarchy();
            _root = null;
            _options = null;
        }

        private void OnDestroy()
        {
            if (_ownedPanel != null)
            {
                Destroy(_ownedPanel);
            }
        }

        private void Render(bool animate = false)
        {
            if (_root == null || _run == null)
            {
                return;
            }

            _root.style.display = DisplayStyle.Flex;
            _root.Clear();
            _options = null;

            var shell = Element("room-event-shell");
            _root.Add(shell);
            shell.Add(CreateHeader());

            var composition = Element("room-event-composition");
            shell.Add(composition);
            if (animate)
            {
                // 从下一次样式更新开始淡入。查看情报时不再播放入场，避免列表跳动。
                composition.AddToClassList("room-appearing");
                composition.schedule.Execute(() => composition.RemoveFromClassList("room-appearing")).StartingIn(16);
            }

            var context = Element("room-event-context");
            composition.Add(context);
            var stage = new ScrollView(ScrollViewMode.Vertical) { name = "room-event-options" };
            stage.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            stage.AddToClassList("room-event-stage");
            composition.Add(stage);
            _options = stage;

            if (_result != null)
            {
                RenderResult(context, stage);
                return;
            }

            if (_choiceNotice != null)
            {
                stage.Add(Label(_choiceNotice, "room-event-notice"));
            }

            switch (_run.Phase)
            {
                case RunPhase.ChooseStarter:
                    RenderStarter(context, stage);
                    break;
                case RunPhase.ChooseEvent:
                    RenderStory(context, stage, (StoryRoomEvent)_run.ActiveEvent);
                    break;
                case RunPhase.ChooseReward:
                    RenderRewards(context, stage, (RewardRoomEvent)_run.ActiveEvent);
                    break;
            }
        }

        private VisualElement CreateHeader()
        {
            var header = Element("room-event-header");
            header.Add(Label($"{_run.Scenario.Name}  /  第 {_run.State.Floor:00} 层", "room-event-path"));

            var readouts = Element("room-event-readouts");
            readouts.Add(Metric("资金", _run.State.Funds, "room-funds"));
            readouts.Add(Metric("Insight", _run.State.Insight, "room-insight"));
            header.Add(readouts);
            return header;
        }

        private VisualElement Metric(string caption, int amount, string name)
        {
            var metric = Element("room-event-metric");
            metric.Add(Label(caption, "room-event-caption"));
            var value = Label(amount.ToString(CultureInfo.InvariantCulture), "room-event-value", name);
            _typography.ApplyHudNumber(value);
            metric.Add(value);
            return metric;
        }

        private void RenderStarter(VisualElement context, VisualElement stage)
        {
            AddContext(context, "出发准备", "选择初始套牌", "所选卡牌将组成这次行动的牌组。");
            for (int index = 0; index < _run.StarterSets.Count; index++)
            {
                int selected = index;
                var set = _run.StarterSets[index];
                string cards = string.Join("、", set.Cards.Select(card => card.Name).Distinct());
                AddOption(stage, set.Name, $"{set.Cards.Count} 张卡牌\n{cards}", null, $"run-starter-{index}", () =>
                {
                    if (_run.ChooseStarter(selected))
                    {
                        ReturnToMap();
                    }
                });
            }
        }

        private void RenderStory(VisualElement context, VisualElement stage, StoryRoomEvent story)
        {
            AddContext(context, "事件", story.Name, story.Description);
            for (int index = 0; index < story.Choices.Count; index++)
            {
                int selected = index;
                var choice = story.Choices[index];
                bool known = _run.IsOptionRevealed(index);
                // 保留已揭示选项的资金提示。核心实际结算仍由 RunSession 处理。
                bool affordable = !known || _run.State.Funds + (long)choice.FundsDelta >= 0;
                string effect = known ? FormatStoryEffects(choice, affordable) : null;
                var footer = AddOption(stage, choice.Name, choice.Description, effect, $"run-event-{index}", () =>
                {
                    if (_run.ChooseEvent(selected, out var result))
                    {
                        PresentResult(result);
                    }
                    else if (_result == null && _onReturn != null)
                    {
                        _choiceNotice = "无法选择此项，请尝试其他选项。";
                        Render();
                    }
                }, affordable);
                AddInsightButton(footer, index, known);
            }
        }

        private void RenderRewards(VisualElement context, VisualElement stage, RewardRoomEvent room)
        {
            string title = room.IsRare ? "稀有奖励" : room.Room.Kind == MapRoomKind.Battle ? "战斗奖励" : "领取奖励";
            AddContext(context, "奖励", title, "可先消耗 Insight 查看某项奖励，也可直接选择。");
            for (int index = 0; index < room.Options.Count; index++)
            {
                int selected = index;
                var reward = room.Options[index];
                bool known = _run.IsOptionRevealed(index);
                string effect = known ? FormatAward(reward.Card, reward.Facility) : null;
                var footer = AddOption(stage, reward.Description, null, effect, $"run-reward-{index}", () =>
                {
                    if (_run.ChooseReward(selected, out var result))
                    {
                        PresentResult(result);
                    }
                });
                AddInsightButton(footer, index, known);
            }

            if (!room.IsRare)
            {
                stage.Add(Button("跳过奖励", "room-event-secondary room-event-skip", "run-skip-reward", () =>
                {
                    if (_run.SkipReward())
                    {
                        ReturnToMap();
                    }
                }));
            }
        }

        private string FormatStoryEffects(StoryChoice choice, bool affordable)
        {
            string funds = choice.FundsDelta == 0 ? null : $"资金 {choice.FundsDelta:+#;-#}";
            string award = FormatAward(choice.Card, null);
            string effect = funds == null ? award : award == null ? funds : funds + "\n" + award;
            if (!affordable)
            {
                effect += "\n资金不足";
            }

            return effect ?? "无额外变化";
        }

        private string FormatAward(CardDefinition card, FacilityDefinition facility)
        {
            if (card != null)
            {
                string effects = string.Join("\n", card.Effects.Select(effect =>
                    BattleScreenText.Effect(effect, _run.Scenario.BenefitName)));
                string keywords = BattleScreenText.Keywords(card, detailed: true);
                string details = string.IsNullOrEmpty(effects) ? string.Empty : "\n" + effects;
                if (!string.IsNullOrEmpty(keywords))
                {
                    details += "\n" + keywords;
                }

                return $"获得卡牌 · {card.Name}" + details;
            }

            return facility == null ? null : $"获得{_run.Scenario.FacilityName} · {facility.Name}";
        }

        private void PresentResult(RoomChoiceResult result)
        {
            _result = result;
            _choiceNotice = null;
            Render(animate: true);
        }

        private void RenderResult(VisualElement context, VisualElement stage)
        {
            AddContext(context, "结算结果", _result.Title, null, "run-choice-result-title");
            var report = Element("room-event-report");
            if (_result.FundsDelta != 0)
            {
                report.Add(Label($"资金 {_result.FundsDelta:+#;-#} · 现有 {_run.State.Funds}",
                    "room-event-result", "run-choice-result-funds"));
            }

            string award = FormatAward(_result.Card, _result.Facility);
            if (award != null)
            {
                report.Add(Label(award, "room-event-result",
                    _result.Card != null ? "run-choice-result-card" : "run-choice-result-facility"));
            }

            if (_result.FundsDelta == 0 && award == null)
            {
                report.Add(Label("本次选择没有改变运营状态。", "room-event-result"));
            }

            stage.Add(report);
            string label = _run.Phase is RunPhase.Lost or RunPhase.Won ? "查看挑战结果" : "继续路线";
            stage.Add(Button(label, "room-event-primary room-event-continue", "run-choice-result-continue", ReturnToMap));
        }

        private static void AddContext(
            VisualElement context, string category, string title, string description, string titleName = null)
        {
            context.Add(Label(category, "room-event-kind"));
            context.Add(Label(title, "room-event-title", titleName));
            if (!string.IsNullOrWhiteSpace(description))
            {
                context.Add(Label(description, "room-event-narrative"));
            }
        }

        private static VisualElement AddOption(
            VisualElement stage, string title, string description, string effect, string name, Action action, bool enabled = true)
        {
            var option = Element("room-event-option");
            stage.Add(option);
            option.Add(Label(title, "room-event-option-title", name + "-title"));
            if (!string.IsNullOrWhiteSpace(description))
            {
                option.Add(Label(description, "room-event-option-description"));
            }

            if (!string.IsNullOrWhiteSpace(effect))
            {
                option.Add(Label(effect, "room-event-option-effect", name + "-effect"));
            }

            var footer = Element("room-event-option-footer");
            var choose = Button("选择", "room-event-primary", name, action);
            choose.SetEnabled(enabled);
            footer.Add(choose);
            option.Add(footer);
            return footer;
        }

        private void AddInsightButton(VisualElement footer, int index, bool revealed)
        {
            var button = Button(revealed ? "已查看效果" : "查看效果 · 1 Insight", "room-event-secondary",
                $"run-insight-option-{index}", () =>
                {
                    if (_run.RevealOption(index))
                    {
                        Vector2 offset = _options.scrollOffset;
                        _choiceNotice = null;
                        Render();
                        var updated = _options;
                        updated.schedule.Execute(() => updated.scrollOffset = offset);
                    }
                });
            button.SetEnabled(!revealed && _run.State.Insight > 0);
            footer.Insert(0, button);
        }

        private void ReturnToMap()
        {
            // 选择产生的检查点已由会话发出。继续只交还画面控制权，避免重复写入。
            var callback = _onReturn;
            _onReturn = null;
            _root.SetEnabled(false);
            callback?.Invoke();
        }

        private static VisualElement Element(string className, string name = null)
        {
            var element = new VisualElement { name = name };
            element.AddToClassList(className);
            return element;
        }

        private static Label Label(string text, string className, string name = null)
        {
            var label = new Label(text) { name = name };
            label.AddToClassList(className);
            return label;
        }

        private static Button Button(string text, string classes, string name, Action action)
        {
            var button = new Button(action) { text = text, name = name };
            foreach (string className in classes.Split(' '))
            {
                button.AddToClassList(className);
            }

            return button;
        }
    }
}
