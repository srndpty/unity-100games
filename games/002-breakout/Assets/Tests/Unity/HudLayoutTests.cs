using System.Collections;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Game.Unity.Tests
{
    /// <summary>
    /// 実シーンの HUD を各画面比率の配置で描き、得点などと効果の表示が <see cref="FieldFraming"/> の空けた領域の中に収まることを確かめる。
    /// フィールドがその領域の外にあることは <see cref="FieldFramingTests"/> で確かめているので、合わせて HUD とフィールドが重ならない。
    /// </summary>
    public sealed class HudLayoutTests
    {
        private const float Tolerance = 1f; // パネル座標の丸め

        private BreakoutGame _game;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("Breakout");
            _game = Object.FindAnyObjectByType<BreakoutGame>();
            yield return null;

            // 毎フレーム実際の画面比率で配置し直さないよう止め、テストから配置を渡す。
            _game.enabled = false;
        }

        [UnityTest]
        public IEnumerator Panels_StayInsideHudArea([Values(21f / 9f, 16f / 9f, 4f / 3f, 1f, 9f / 16f)] float aspect)
        {
            _game.Flow.Submit(); // プレイ中の表示（得点・残機・面）
            _game.Flow.Game!.Effects.Activate(EffectKind.Pierce);
            _game.Flow.Game.Effects.Activate(EffectKind.Expand);
            var framing = FieldFraming.Compute(_game.Settings.Game, aspect);

            _game.Hud.Render(_game.Flow, framing);
            yield return null;
            yield return null;

            var root = _game.HudRoot.Q<VisualElement>("hud").worldBound;
            var info = _game.HudRoot.Q<VisualElement>("info").worldBound;
            var effects = _game.HudRoot.Q<VisualElement>("effects").worldBound;
            if (framing.Layout == HudLayout.Sides)
            {
                var side = root.width * framing.HudFraction;
                Assert.That(info.xMax, Is.LessThanOrEqualTo(root.xMin + side + Tolerance), "左の表示がフィールド側へはみ出す");
                Assert.That(effects.xMin, Is.GreaterThanOrEqualTo(root.xMax - side - Tolerance), "右の表示がフィールド側へはみ出す");
            }
            else
            {
                var band = root.height * framing.HudFraction;
                Assert.That(info.yMax, Is.LessThanOrEqualTo(root.yMin + band + Tolerance), "上の表示がフィールド側へはみ出す");
                Assert.That(effects.yMax, Is.LessThanOrEqualTo(root.yMin + band + Tolerance), "上の表示がフィールド側へはみ出す");
                Assert.That(_game.HudRoot.Q<VisualElement>("hud").ClassListContains("hud--top"), Is.True);
            }

            Assert.That(info.width, Is.GreaterThan(0f));
            Assert.That(effects.width, Is.GreaterThan(0f));
        }
    }
}
