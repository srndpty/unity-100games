using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity
{
    /// <summary>
    /// 壁・ブロック・パドル・ボール・カプセルを白い矩形のスプライトに色を付けて描く。
    /// 作った GameObject は parent の子なので一緒に破棄されるが、テクスチャとスプライトはこのクラスが所有し <see cref="Dispose"/> で破棄する。
    /// </summary>
    public sealed class FieldView : IDisposable
    {
        private const float WallThickness = 0.2f;

        // ブロックの見た目の隙間（衝突判定はマス目いっぱい）。
        private const float BrickGap = 0.06f;

        private static readonly Color WallColor = new(0.45f, 0.5f, 0.6f);
        private static readonly Color PaddleColor = new(0.9f, 0.9f, 0.95f);
        private static readonly Color BallColor = Color.white;
        private static readonly Color PierceBallColor = new(1f, 0.55f, 0.2f);
        private static readonly Color HardColor = new(0.6f, 0.62f, 0.68f);
        private static readonly Color DamagedHardColor = new(0.38f, 0.4f, 0.45f);

        // 通常ブロックは段ごとに色を変える。
        private static readonly Color[] RowColors =
        {
            new(0.35f, 0.75f, 0.95f),
            new(0.4f, 0.85f, 0.55f),
            new(0.95f, 0.85f, 0.35f),
            new(0.95f, 0.6f, 0.3f),
            new(0.75f, 0.5f, 0.95f),
        };

        private readonly BreakoutConfig _config;
        private readonly Transform _parent;
        private readonly Texture2D _texture;
        private readonly Sprite _sprite;
        private readonly Transform _paddle;
        private readonly SpriteRenderer _ball;
        private readonly List<SpriteRenderer> _bricks = new();
        private readonly List<SpriteRenderer> _capsules = new();
        private BrickField _shownField;

        public FieldView(BreakoutConfig config, Transform parent)
        {
            _config = config;
            _parent = parent;
            _texture = CreateWhiteTexture();
            _sprite = Sprite.Create(_texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), pixelsPerUnit: 1f);

            var halfWidth = config.FieldWidth / 2f;
            var halfHeight = config.FieldHeight / 2f;
            var sideHeight = config.FieldHeight + WallThickness;
            var sideY = WallThickness / 2f;
            CreateBlock("Left Wall", new Vector2(WallThickness, sideHeight), new Vector2(-halfWidth - (WallThickness / 2f), sideY), WallColor);
            CreateBlock("Right Wall", new Vector2(WallThickness, sideHeight), new Vector2(halfWidth + (WallThickness / 2f), sideY), WallColor);
            CreateBlock("Top Wall", new Vector2(config.FieldWidth + (WallThickness * 2f), WallThickness), new Vector2(0f, halfHeight + (WallThickness / 2f)), WallColor);

            _paddle = CreateBlock("Paddle", new Vector2(config.PaddleWidth, config.PaddleHeight), new Vector2(0f, config.PaddleY), PaddleColor).transform;
            _ball = CreateBlock("Ball", new Vector2(config.BallSize, config.BallSize), Vector2.zero, BallColor);
        }

        /// <summary>
        /// ゲームの状態を描く。動く物は <paramref name="snapshot"/>（補間済み）、ブロックとカプセルは最新の Tick を描く。
        /// カプセルは等速で落ちるので、<paramref name="alpha"/> の分だけ 1 Tick 前の位置へ戻して滑らかにする。
        /// ゲームがなければ（タイトル画面）壁だけを描く。
        /// </summary>
        public void Render(BreakoutState game, BreakoutSnapshot snapshot, float alpha)
        {
            var showMoving = game is { Phase: GamePhase.Serving or GamePhase.Playing };
            _paddle.gameObject.SetActive(game != null);
            _ball.gameObject.SetActive(showMoving);
            RenderBricks(game?.Field);
            RenderCapsules(game, alpha);
            if (game == null)
            {
                return;
            }

            _paddle.localPosition = new Vector3(snapshot.PaddleX, _config.PaddleY, 0f);
            _paddle.localScale = new Vector3(snapshot.PaddleHalfWidth * 2f, _config.PaddleHeight, 1f);
            _ball.transform.localPosition = new Vector3(snapshot.Ball.X, snapshot.Ball.Y, 0f);
            _ball.color = game.Effects.IsActive(EffectKind.Pierce) ? PierceBallColor : BallColor;
        }

        public void Dispose()
        {
            Object.Destroy(_sprite);
            Object.Destroy(_texture);
        }

        public static Color ColorOf(EffectKind effect) => effect == EffectKind.Pierce ? new Color(0.95f, 0.3f, 0.3f) : new Color(0.3f, 0.5f, 1f);

        private void RenderBricks(BrickField field)
        {
            if (!ReferenceEquals(field, _shownField))
            {
                RebuildBricks(field);
            }

            if (field == null)
            {
                return;
            }

            for (var i = 0; i < _bricks.Count; i++)
            {
                var brick = field.Bricks[i];
                var renderer = _bricks[i];
                renderer.gameObject.SetActive(brick.IsAlive);
                if (brick.Kind == BrickKind.Hard)
                {
                    renderer.color = brick.HitsRemaining < brick.Kind.MaxHits() ? DamagedHardColor : HardColor;
                }
            }
        }

        // 面が変わったら（Field が別のインスタンスになったら）作り直す。
        private void RebuildBricks(BrickField field)
        {
            foreach (var brick in _bricks)
            {
                Object.Destroy(brick.gameObject);
            }

            _bricks.Clear();
            _shownField = field;
            if (field == null)
            {
                return;
            }

            var size = new Vector2(_config.BrickWidth - BrickGap, _config.BrickHeight - BrickGap);
            foreach (var brick in field.Bricks)
            {
                var center = brick.Center;
                _bricks.Add(CreateBlock($"Brick {brick.Index}", size, new Vector2(center.X, center.Y), BaseColorOf(brick)));
            }
        }

        private void RenderCapsules(BreakoutState game, float alpha)
        {
            var count = game?.Capsules.Count ?? 0;
            while (_capsules.Count < count)
            {
                _capsules.Add(CreateBlock("Capsule", new Vector2(_config.CapsuleWidth, _config.CapsuleHeight), Vector2.zero, Color.white));
            }

            var rewind = _config.CapsuleSpeed * _config.TickDuration * (1f - alpha);
            for (var i = 0; i < _capsules.Count; i++)
            {
                var renderer = _capsules[i];
                renderer.gameObject.SetActive(i < count);
                if (i < count)
                {
                    var capsule = game.Capsules[i];
                    renderer.transform.localPosition = new Vector3(capsule.Position.X, capsule.Position.Y + rewind, 0f);
                    renderer.color = ColorOf(capsule.Effect);
                }
            }
        }

        private static Color BaseColorOf(Brick brick) => brick.Kind switch
        {
            BrickKind.Hard => HardColor,
            BrickKind.PierceItem => ColorOf(EffectKind.Pierce),
            BrickKind.ExpandItem => ColorOf(EffectKind.Expand),
            _ => RowColors[brick.Row % RowColors.Length],
        };

        private static Texture2D CreateWhiteTexture()
        {
            var texture = new Texture2D(1, 1) { filterMode = FilterMode.Point, name = "White" };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return texture;
        }

        private SpriteRenderer CreateBlock(string name, Vector2 size, Vector2 position, Color color)
        {
            var block = new GameObject(name);
            var renderer = block.AddComponent<SpriteRenderer>();
            renderer.sprite = _sprite;
            renderer.color = color;
            var transform = block.transform;
            transform.SetParent(_parent, worldPositionStays: false);
            transform.localPosition = position;
            transform.localScale = new Vector3(size.x, size.y, 1f);
            return renderer;
        }
    }
}
