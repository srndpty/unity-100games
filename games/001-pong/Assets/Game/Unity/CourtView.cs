using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>コート・パドル・ボールを白い矩形のスプライトで描く。</summary>
    public sealed class CourtView
    {
        private const float LineThickness = 0.1f;
        private const int CenterDashCount = 15;

        private readonly PongConfig _config;
        private readonly Transform _leftPaddle;
        private readonly Transform _rightPaddle;
        private readonly Transform _ball;

        public CourtView(PongConfig config, Transform parent)
        {
            _config = config;
            var sprite = CreateWhiteSprite();

            var halfHeight = config.FieldHeight / 2f;
            var lineY = halfHeight + (LineThickness / 2f);
            CreateBlock("Top Line", parent, sprite, new Vector2(config.FieldWidth, LineThickness), new Vector2(0f, lineY));
            CreateBlock("Bottom Line", parent, sprite, new Vector2(config.FieldWidth, LineThickness), new Vector2(0f, -lineY));

            var dashPitch = config.FieldHeight / CenterDashCount;
            for (var i = 0; i < CenterDashCount; i++)
            {
                var y = -halfHeight + (dashPitch * (i + 0.5f));
                CreateBlock("Center Dash", parent, sprite, new Vector2(LineThickness / 2f, dashPitch * 0.5f), new Vector2(0f, y));
            }

            var paddleSize = new Vector2(config.PaddleWidth, config.PaddleHeight);
            _leftPaddle = CreateBlock("Left Paddle", parent, sprite, paddleSize, new Vector2(config.PaddleX(Side.Left), 0f));
            _rightPaddle = CreateBlock("Right Paddle", parent, sprite, paddleSize, new Vector2(config.PaddleX(Side.Right), 0f));
            _ball = CreateBlock("Ball", parent, sprite, new Vector2(config.BallSize, config.BallSize), Vector2.zero);
        }

        public void Render(MatchSnapshot snapshot, bool showBall)
        {
            _leftPaddle.localPosition = new Vector3(_config.PaddleX(Side.Left), snapshot.LeftY, 0f);
            _rightPaddle.localPosition = new Vector3(_config.PaddleX(Side.Right), snapshot.RightY, 0f);
            _ball.gameObject.SetActive(showBall);
            _ball.localPosition = new Vector3(snapshot.Ball.X, snapshot.Ball.Y, 0f);
        }

        private static Sprite CreateWhiteSprite()
        {
            var texture = new Texture2D(1, 1) { filterMode = FilterMode.Point, name = "White" };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), pixelsPerUnit: 1f);
        }

        private static Transform CreateBlock(string name, Transform parent, Sprite sprite, Vector2 size, Vector2 position)
        {
            var block = new GameObject(name);
            block.AddComponent<SpriteRenderer>().sprite = sprite;
            var transform = block.transform;
            transform.SetParent(parent, worldPositionStays: false);
            transform.localPosition = position;
            transform.localScale = new Vector3(size.x, size.y, 1f);
            return transform;
        }
    }
}
