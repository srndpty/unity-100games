#nullable enable
using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    public sealed class DeductionGame : MonoBehaviour
    {
        [SerializeField] private PanelRenderer _panel = null!;
        [SerializeField] private DeductionSettings _settings = null!;
        [SerializeField] private Texture2D _museum = null!;
        [SerializeField] private StyleSheet _style = null!;
        private VisualElement? _root;
        private AudioSource _audio = null!;
        private Font? _font;
        private AudioClip? _correct;
        private AudioClip? _incorrect;

        public CaseFlow Flow { get; private set; } = null!;
        public VisualElement? Root => _root;

        private void Awake()
        {
            Flow = new CaseFlow(CaseFile.Museum, _settings.Attempts);
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _correct = Tone("正解", 660);
            _incorrect = Tone("誤提示", 220);
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo", "MS Gothic" }, 32);
            _panel.RegisterUIReloadCallback(OnReload);
        }

        private void OnReload(PanelRenderer panel, VisualElement root, int version)
        {
            _root = root.Q("hud") ?? throw new InvalidOperationException("hud がありません。");
            _root.styleSheets.Add(_style);
            if (_font != null)
            {
                _root.style.unityFont = _font;
                _root.style.unityFontDefinition = FontDefinition.FromFont(_font);
            }
            Render();
        }

        private void OnApplicationFocus(bool focused) => Flow?.Focus(focused);

        private void Update()
        {
            if (!EvidenceCapture.IsRunning) { Flush(); }
        }

        private void OnDestroy()
        {
            Destroy(_font);
            Destroy(_correct);
            Destroy(_incorrect);
        }

        public void Flush()
        {
            var previous = Flow.Case;
            var attempts = previous.Attempts;
            var target = previous.Target;
            if (!Flow.Flush()) { return; }
            Render();
            if (AudioPolicy.ShouldMute || previous != Flow.Case) { return; }
            if (previous.Attempts < attempts && _incorrect != null) { _audio.PlayOneShot(_incorrect, _settings.Volume); }
            else if (previous.Target != target && _correct != null) { _audio.PlayOneShot(_correct, _settings.Volume); }
        }

        public void Render()
        {
            if (_root == null) { return; }
            _root.Clear();
            _root.style.backgroundImage = new StyleBackground(_museum);
            var layout = new VisualElement();
            layout.AddToClassList(Flow.Screen == Game.App.Screen.Case ? "case" : "landing");
            _root.Add(layout);
            switch (Flow.Screen)
            {
                case Game.App.Screen.Title:
                    Heading(layout, "閉館後の展示室");
                    Copy(layout, "展示メダルがすり替わった。");
                    Button(layout, "start", "調査を始める", Operation.Start);
                    Button(layout, "help", "遊び方", Operation.Help);
                    break;
                case Game.App.Screen.Help:
                    Heading(layout, "遊び方");
                    Copy(layout, "証言を選び、尋問で証拠を取得します。\n"
                        + "照合対象の証言と矛盾する証拠を事件資料から選び、提示してください。\n"
                        + "誤提示で残り回数が減ります。3つの矛盾を立証すると事件は解決します。");
                    Button(layout, "start", "調査を始める", Operation.Start);
                    Button(layout, "title", "タイトルへ", Operation.Title);
                    break;
                case Game.App.Screen.Case:
                    RenderCase(layout);
                    break;
                case Game.App.Screen.Result:
                    Heading(layout, Flow.Case.Outcome == Outcome.Won ? "事件解決" : "調査終了");
                    Copy(layout, Flow.Notice).AddToClassList("result-copy");
                    Button(layout, "start", "もう一度調査する", Operation.Start);
                    Button(layout, "title", "タイトルへ", Operation.Title);
                    break;
            }
        }

        private void RenderCase(VisualElement layout)
        {
            var state = Flow.Case;
            var header = Row(layout);
            Heading(header, "閉館後の展示室");
            var counter = Copy(header, $"残り {state.Attempts} 回");
            counter.name = "attempts";
            Button(header, "title", "タイトルへ", Operation.Title);
            var notice = Copy(layout, Flow.Notice);
            notice.name = "notice";
            notice.AddToClassList(Flow.LastAction == ActionResult.Incorrect ? "wrong" : "notice");

            var columns = Row(layout);
            columns.AddToClassList("columns");
            var statements = Paper(columns, "証言");
            for (var id = 0; id < state.File.Testimony.Count; id++)
            {
                var label = state.IsUnlocked(id) ? state.File.Testimony[id].Speaker : "未解放";
                if (id == state.Target) { label += "  ・照合対象"; }
                else if (state.ProofOf(id) != null) { label += "  ・立証済み"; }
                var button = Button(statements, $"statement-{id}", $"{id + 1}. {label}", Operation.SelectTestimony, id);
                button.SetEnabled(state.IsUnlocked(id));
                button.EnableInClassList("selected", Flow.Testimony == id);
            }

            var detail = Paper(columns, state.File.Testimony[Flow.Testimony].Speaker);
            detail.AddToClassList("detail");
            Copy(detail, state.File.Testimony[Flow.Testimony].Text).AddToClassList("testimony-text");
            Button(detail, "question", "尋問する", Operation.Question).SetEnabled(state.CanQuestion(Flow.Testimony));
            var evidence = state.File.Evidence[Flow.Evidence];
            var evidenceDetail = new ScrollView();
            evidenceDetail.AddToClassList("evidence-detail");
            detail.Add(evidenceDetail);
            Heading(evidenceDetail, evidence.Name);
            Copy(evidenceDetail, evidence.Detail);
            if (Flow.Evidence == 6)
            {
                var sketch = Row(evidenceDetail);
                sketch.AddToClassList("sketch");
                Copy(sketch, "検").AddToClassList("seal-left");
                Copy(sketch, "印").AddToClassList("seal-right");
                Copy(sketch, "複").AddToClassList("replica-mark");
            }

            var proof = state.ProofOf(Flow.Testimony);
            if (proof != null)
            {
                Heading(evidenceDetail, "立証記録");
                Copy(evidenceDetail, proof.Finding);
                Copy(evidenceDetail, $"立証に使った証拠：{state.File.Evidence[proof.EvidenceId].Name}");
            }

            Button(detail, "present", "選択した証拠を提示", Operation.Present).SetEnabled(Flow.CanPresent);
            var records = Paper(columns, "事件資料");
            for (var id = 0; id < state.File.Evidence.Count; id++)
            {
                if (!state.Owns(id)) { continue; }
                var button = Button(records, $"evidence-{id}", state.File.Evidence[id].Name, Operation.SelectEvidence, id);
                button.EnableInClassList("selected", Flow.Evidence == id);
            }
        }

        private Button Button(VisualElement parent, string name, string text, Operation operation, int id = -1)
        {
            var viewVersion = Flow.ViewVersion;
            var button = new Button(() =>
            {
                if (EvidenceCapture.IsRunning) { return; }
                Flow.Enqueue(operation, id, viewVersion);
            })
            {
                name = name,
                text = text,
            };
            parent.Add(button);
            return button;
        }

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("row");
            parent.Add(row);
            return row;
        }

        private static VisualElement Paper(VisualElement parent, string heading)
        {
            var panel = new VisualElement();
            panel.AddToClassList("paper");
            parent.Add(panel);
            Heading(panel, heading);
            return panel;
        }

        private static void Heading(VisualElement parent, string text) => Copy(parent, text).AddToClassList("heading");

        private static Label Copy(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.AddToClassList("copy");
            parent.Add(label);
            return label;
        }

        private static AudioClip Tone(string name, float frequency)
        {
            const int rate = 22050;
            var data = new float[rate / 8];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = Mathf.Sin(2 * Mathf.PI * frequency * i / rate) * (1f - (float)i / data.Length) * 0.15f;
            }

            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
