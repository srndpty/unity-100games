using System.Globalization;
using Game.App;
using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// UI Toolkit の得点・残機・面・効果の表示と、タイトル・結果のオーバーレイ、発射・面クリアの案内。
    /// 得点などと効果の表示は、<see cref="FieldFraming"/> が空けた領域（左右の余白、または上部の帯）にだけ置き、フィールドに重ねない。
    /// </summary>
    public sealed class BreakoutHud
    {
        private const string TopLayoutClass = "hud--top";

        private const string TitleMessage =
            "Clear all {0} stages to win  /  {1} lives\n\n" +
            "A / D  or  Left / Right : move\n" +
            "Space / Enter : start, launch\n" +
            "Esc : quit\n\n" +
            "Catch falling capsules - red: pierce, blue: wide paddle\n" +
            "Gray bricks need 2 hits";

        private const string ResultMessage =
            "SCORE  {0}\n\n" +
            "Enter / Space : play again\n" +
            "Esc : title";

        private const string LaunchHint = "Space / Enter : launch";

        private readonly VisualElement _hud;
        private readonly VisualElement _info;
        private readonly VisualElement _effects;
        private readonly Label _score;
        private readonly Label _lives;
        private readonly Label _stage;
        private readonly Label _pierce;
        private readonly Label _expand;
        private readonly Label _banner;
        private readonly VisualElement _overlay;
        private readonly Label _headline;
        private readonly Label _message;
        private readonly BreakoutConfig _config;
        private bool _placed;

        public BreakoutHud(VisualElement root, BreakoutConfig config)
        {
            _hud = root.Q<VisualElement>("hud");
            _info = root.Q<VisualElement>("info");
            _effects = root.Q<VisualElement>("effects");
            _score = root.Q<Label>("score");
            _lives = root.Q<Label>("lives");
            _stage = root.Q<Label>("stage");
            _pierce = root.Q<Label>("pierce");
            _expand = root.Q<Label>("expand");
            _banner = root.Q<Label>("banner");
            _overlay = root.Q<VisualElement>("overlay");
            _headline = root.Q<Label>("headline");
            _message = root.Q<Label>("message");
            _config = config;
        }

        /// <summary>最後に適用した配置。</summary>
        public Framing Framing { get; private set; }

        public void Render(BreakoutFlow flow, Framing framing)
        {
            ApplyLayout(framing);
            var game = flow.Game;
            SetText(_score, Format(game?.Score ?? 0));
            SetText(_lives, Format(game?.Lives ?? _config.Lives));
            SetText(_stage, $"{Format((game?.StageIndex ?? 0) + 1)} / {Format(_config.Stages.Length)}");
            RenderEffect(_pierce, "PIERCE", game, EffectKind.Pierce);
            RenderEffect(_expand, "EXPAND", game, EffectKind.Expand);
            SetText(_banner, BannerOf(game));

            switch (flow.Screen)
            {
                case AppScreen.Title:
                    ShowOverlay("BREAKOUT", string.Format(CultureInfo.InvariantCulture, TitleMessage, _config.Stages.Length, _config.Lives));
                    break;
                case AppScreen.Playing:
                    _overlay.style.display = DisplayStyle.None;
                    break;
                case AppScreen.Result:
                    var headline = game!.Phase == GamePhase.Won ? "YOU WIN" : "GAME OVER";
                    ShowOverlay(headline, string.Format(CultureInfo.InvariantCulture, ResultMessage, game.Score));
                    break;
            }
        }

        // 配置が変わったときだけ位置を書き換える（毎フレームの再レイアウトを避ける）。
        private void ApplyLayout(Framing framing)
        {
            if (_placed && framing.Layout == Framing.Layout && Mathf.Approximately(framing.HudFraction, Framing.HudFraction))
            {
                return;
            }

            _placed = true;
            Framing = framing;
            var fraction = framing.HudFraction * 100f;
            _hud.EnableInClassList(TopLayoutClass, framing.Layout == HudLayout.Top);
            if (framing.Layout == HudLayout.Sides)
            {
                Place(_info, left: 0f, width: fraction, height: 100f);
                Place(_effects, left: 100f - fraction, width: fraction, height: 100f);
            }
            else
            {
                Place(_info, left: 0f, width: 50f, height: fraction);
                Place(_effects, left: 50f, width: 50f, height: fraction);
            }
        }

        // 画面に対する割合（%）で置く。
        private static void Place(VisualElement panel, float left, float width, float height)
        {
            panel.style.left = Length.Percent(left);
            panel.style.top = Length.Percent(0f);
            panel.style.width = Length.Percent(width);
            panel.style.height = Length.Percent(height);
        }

        private static string BannerOf(BreakoutState game) => game?.Phase switch
        {
            GamePhase.Serving => $"STAGE {Format(game.StageIndex + 1)}\n{LaunchHint}",
            GamePhase.StageClear => $"STAGE {Format(game.StageIndex + 1)} CLEAR",
            _ => string.Empty,
        };

        // 有効中だけ残り秒数を出す。
        private void RenderEffect(Label label, string name, BreakoutState game, EffectKind effect)
        {
            var remaining = game?.Effects.Remaining(effect) ?? 0;
            label.style.display = remaining > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (remaining > 0)
            {
                var seconds = remaining * _config.TickDuration;
                SetText(label, $"{name}  {seconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
            }
        }

        private void ShowOverlay(string headline, string message)
        {
            _overlay.style.display = DisplayStyle.Flex;
            SetText(_headline, headline);
            SetText(_message, message);
        }

        private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

        // 毎フレームの再レイアウトを避けるため、変化したときだけ書き換える。
        private static void SetText(Label label, string text)
        {
            if (label.text != text)
            {
                label.text = text;
            }
        }
    }
}
