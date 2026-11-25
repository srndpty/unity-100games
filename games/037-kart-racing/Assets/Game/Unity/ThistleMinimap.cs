using Game.App;
using Game.Core;
using IntGeometry;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// コース全体と 4 台の位置。北が上で回らない。中心線を太い線で描き、CPU は丸、プレイヤーは向きの分かる三角にする。
    /// 写像は <see cref="MinimapFrame"/>（App）。要素の大きさは USS で固定し、その大きさで写像を作る。
    /// </summary>
    public sealed class ThistleMinimap : VisualElement
    {
        public const float MapWidth = 500f;

        public const float MapHeight = 190f;

        private readonly Vector2[] _path;
        private readonly Vector2 _startA;
        private readonly Vector2 _startB;
        private readonly float _road;
        private readonly MinimapFrame _frame;
        private Race _race;

        public ThistleMinimap(Track track)
        {
            name = "minimap";
            AddToClassList("minimap");
            pickingMode = PickingMode.Ignore;
            _frame = new MinimapFrame(track, MapWidth, MapHeight, 22f);
            _path = new Vector2[track.StationCount];
            for (var i = 0; i < track.StationCount; i++) { _path[i] = ToUi(track.StationPosition(i)); }
            var start = track.CheckpointStation(0);
            _startA = ToUi(track.Left(start));
            _startB = ToUi(track.Right(start));
            _road = Mathf.Max(4f, track.HalfWidth(0) * 2f * _frame.Scale);
            generateVisualContent += Draw;
        }

        /// <summary>描く車の位置（UI の座標、テスト用）。</summary>
        public Vector2 KartPoint(int index) => _race == null ? Vector2.zero : ToUi(_race.KartAt(index).Position);

        public MinimapFrame Frame => _frame;

        public void Show(Race race)
        {
            _race = race;
            MarkDirtyRepaint();
        }

        private Vector2 ToUi(Vec2i point)
        {
            var mapped = _frame.Map(point);
            return new Vector2(mapped.X, mapped.Y);
        }

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;

            painter.strokeColor = new Color(0.02f, 0.03f, 0.04f, 0.9f);
            painter.lineWidth = _road + 6f;
            Stroke(painter);
            painter.strokeColor = new Color(0.62f, 0.65f, 0.70f, 1f);
            painter.lineWidth = _road;
            Stroke(painter);

            painter.strokeColor = Color.white;
            painter.lineWidth = 3f;
            painter.BeginPath();
            painter.MoveTo(_startA);
            painter.LineTo(_startB);
            painter.Stroke();

            if (_race == null) { return; }
            for (var i = KartConfig.KartCount - 1; i >= 1; i--) { Dot(painter, i); }
            Arrow(painter);
        }

        private void Stroke(Painter2D painter)
        {
            painter.BeginPath();
            painter.MoveTo(_path[0]);
            for (var i = 1; i < _path.Length; i++) { painter.LineTo(_path[i]); }
            painter.ClosePath();
            painter.Stroke();
        }

        private void Dot(Painter2D painter, int index)
        {
            var kart = _race.KartAt(index);
            var color = ThistlePalette.Kart(index);
            if (kart.Finished) { color.a = 0.5f; }
            painter.fillColor = color;
            painter.strokeColor = new Color(0f, 0f, 0f, 0.8f);
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.Arc(ToUi(kart.Position), 6.5f, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }

        private void Arrow(Painter2D painter)
        {
            var kart = _race.KartAt(0);
            var center = ToUi(kart.Position);
            var direction = new Vector2(Trig.Cos(kart.Heading), -Trig.Sin(kart.Heading)) / Trig.One;
            var side = new Vector2(-direction.y, direction.x);
            painter.fillColor = ThistlePalette.Kart(0);
            painter.strokeColor = Color.white;
            painter.lineWidth = 2.5f;
            painter.BeginPath();
            painter.MoveTo(center + (direction * 12f));
            painter.LineTo(center - (direction * 7f) + (side * 8f));
            painter.LineTo(center - (direction * 3f));
            painter.LineTo(center - (direction * 7f) - (side * 8f));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }
    }
}
