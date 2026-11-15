using System.Globalization;
using Game.App;
using Game.Core;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>UI Toolkit のスコア表示と、タイトル・結果のオーバーレイ。</summary>
    public sealed class PongHud
    {
        private const string TitleMessage =
            "First to {0} points wins\n\n" +
            "W / S  or  Up / Down : move\n" +
            "Enter / Space : start\n" +
            "Esc : quit";

        private const string ResultMessage =
            "{0}  -  {1}\n\n" +
            "Enter / Space : rematch\n" +
            "Esc : title";

        private readonly Label _leftScore;
        private readonly Label _rightScore;
        private readonly VisualElement _overlay;
        private readonly Label _headline;
        private readonly Label _message;
        private readonly int _winScore;

        public PongHud(VisualElement root, int winScore)
        {
            _leftScore = root.Q<Label>("left-score");
            _rightScore = root.Q<Label>("right-score");
            _overlay = root.Q<VisualElement>("overlay");
            _headline = root.Q<Label>("headline");
            _message = root.Q<Label>("message");
            _winScore = winScore;
        }

        public void Render(PongFlow flow)
        {
            var match = flow.Match;
            SetText(_leftScore, Format(match?.LeftScore ?? 0));
            SetText(_rightScore, Format(match?.RightScore ?? 0));

            switch (flow.Screen)
            {
                case AppScreen.Title:
                    ShowOverlay("PONG", string.Format(CultureInfo.InvariantCulture, TitleMessage, _winScore));
                    break;
                case AppScreen.Playing:
                    _overlay.style.display = DisplayStyle.None;
                    break;
                case AppScreen.Result:
                    var headline = match!.Winner == Side.Left ? "YOU WIN" : "CPU WINS";
                    ShowOverlay(headline, string.Format(CultureInfo.InvariantCulture, ResultMessage, match.LeftScore, match.RightScore));
                    break;
            }
        }

        private void ShowOverlay(string headline, string message)
        {
            _overlay.style.display = DisplayStyle.Flex;
            SetText(_headline, headline);
            SetText(_message, message);
        }

        private static string Format(int score) => score.ToString(CultureInfo.InvariantCulture);

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
