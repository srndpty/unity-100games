#nullable enable
using System;
using Game.App;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>戦闘画面：背景・味方（背中）・相手（正面）・捕獲玉。見せ方は <see cref="BattlePresenter"/> の姿だけから決める。</summary>
    public sealed class KeeperBattleStage : MonoBehaviour
    {
        public static readonly Vector3 PlayerPosition = new(-3.8f, -0.5f, 0f);
        public static readonly Vector3 FoePosition = new(4.4f, 2.6f, 0f);

        [SerializeField] private SpriteRenderer? _backdrop;
        [SerializeField] private SpriteRenderer? _player;
        [SerializeField] private SpriteRenderer? _foe;
        [SerializeField] private SpriteRenderer? _ball;

        /// <summary>種族の番号の順の正面・背中の絵。</summary>
        [SerializeField] private Sprite[] _fronts = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _backs = Array.Empty<Sprite>();

        public SpriteRenderer? Player => _player;

        public SpriteRenderer? Foe => _foe;

        public SpriteRenderer? Ball => _ball;

        public string? Mismatch(int speciesCount)
        {
            if (_backdrop == null || _player == null || _foe == null || _ball == null) { return "戦闘の SpriteRenderer が未設定です。"; }
            if (_fronts.Length != speciesCount || _backs.Length != speciesCount) { return $"種族の絵の数が {speciesCount} ではありません。"; }
            if (Vector3.Distance(_player.transform.localPosition, PlayerPosition) > 0.01f || Vector3.Distance(_foe.transform.localPosition, FoePosition) > 0.01f)
            {
                return "味方・相手の位置が既定と違います。";
            }

            foreach (var s in _fronts)
            {
                if (s == null) { return "正面の絵が欠けています。"; }
            }

            foreach (var s in _backs)
            {
                if (s == null) { return "背中の絵が欠けています。"; }
            }

            return null;
        }

        public void Show(BattlePresenter p)
        {
            ShowSide(_player!, p.Player, _backs, PlayerPosition);
            ShowSide(_foe!, p.Foe, _fronts, FoePosition);
            if (p.FoeCaught) { _foe!.enabled = false; }
            var ball = _ball!;
            ball.enabled = p.BallTicks > 0;
            if (ball.enabled)
            {
                // 投げてから 12 Tick で相手の位置へ、その後は揺れた回数だけ左右に傾ける。
                var t = Mathf.Clamp01(p.BallTicks / 12f);
                var arc = Mathf.Sin(t * Mathf.PI) * 2.2f;
                ball.transform.localPosition = Vector3.Lerp(PlayerPosition, FoePosition + new Vector3(0f, -0.6f, 0f), t) + new Vector3(0f, arc, 0f);
                var wiggle = 0f;
                if (p.BallTicks > 12)
                {
                    var k = (p.BallTicks - 12) / 18;
                    if (k < p.BallShakes) { wiggle = Mathf.Sin((p.BallTicks - 12) / 18f * Mathf.PI * 2f) * 25f; }
                }

                ball.transform.localRotation = Quaternion.Euler(0f, 0f, wiggle);
                if (p.BallTicks > 12 && !p.FoeCaught) { _foe!.enabled = false; }
            }
        }

        private static void ShowSide(SpriteRenderer r, SideView side, Sprite[] sprites, Vector3 home)
        {
            var visible = side.Visible && side.SpeciesIndex >= 0 && side.SpeciesIndex < sprites.Length && (side.Hp > 0 || side.ShownHp > 0);
            r.enabled = visible;
            if (!visible) { return; }
            r.sprite = sprites[side.SpeciesIndex];
            r.transform.localPosition = home;
            var flash = side.FlashTicks > 0 && side.FlashTicks % 4 < 2;
            r.color = flash ? new Color(1f, 1f, 1f, 0.25f) : Color.white;
        }
    }
}
