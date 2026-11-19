using System;
using Game.Core;

namespace Game.App
{
    /// <summary>ポインタの操作から作った、盤への意図の種類。</summary>
    public enum PointerActionKind
    {
        None,

        /// <summary>山札をクリックした（めくる・戻す）。</summary>
        ClickStock,

        /// <summary>札を動かす。</summary>
        Move,

        /// <summary>札をダブルクリックした（組札へ送る）。</summary>
        SendToFoundation,
    }

    /// <summary>ポインタの操作から作った、盤への意図。合法かどうかはゲームが決める。</summary>
    public readonly struct PointerAction : IEquatable<PointerAction>
    {
        private PointerAction(PointerActionKind kind, Command command, PileId pile)
        {
            Kind = kind;
            Command = command;
            Pile = pile;
        }

        public static PointerAction None => default;

        public static PointerAction ClickStock => new(PointerActionKind.ClickStock, default, PileId.Stock);

        public PointerActionKind Kind { get; }

        /// <summary>Move のコマンド。</summary>
        public Command Command { get; }

        /// <summary>SendToFoundation で送る札の山。</summary>
        public PileId Pile { get; }

        public static PointerAction Move(Command command) => new(PointerActionKind.Move, command, command.From);

        public static PointerAction SendToFoundation(PileId pile) => new(PointerActionKind.SendToFoundation, default, pile);

        public static bool operator ==(PointerAction left, PointerAction right) => left.Equals(right);

        public static bool operator !=(PointerAction left, PointerAction right) => !left.Equals(right);

        public bool Equals(PointerAction other) => Kind == other.Kind && Command == other.Command && Pile == other.Pile;

        public override bool Equals(object? obj) => obj is PointerAction other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Command, Pile);

        public override string ToString() => Kind switch
        {
            PointerActionKind.Move => $"Move {Command}",
            PointerActionKind.SendToFoundation => $"SendToFoundation {Pile}",
            _ => Kind.ToString(),
        };
    }

    /// <summary>掴んだ・選んだ札の並び：山と、上から数えた枚数。</summary>
    public readonly struct PileRun : IEquatable<PileRun>
    {
        public PileRun(PileId pile, int count)
        {
            Pile = pile;
            Count = count;
        }

        public PileId Pile { get; }

        public int Count { get; }

        public static bool operator ==(PileRun left, PileRun right) => left.Equals(right);

        public static bool operator !=(PileRun left, PileRun right) => !left.Equals(right);

        public bool Equals(PileRun other) => Pile == other.Pile && Count == other.Count;

        public override bool Equals(object? obj) => obj is PileRun other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Pile, Count);
    }

    /// <summary>
    /// ポインタの押下・移動・離しを盤への意図に変える。ドラッグ中の見た目（掴んだ札の位置）もここが持ち、盤は確定まで変わらない。
    /// 盤の規則は知らず、表の札を掴めるか（伏せ札・山の途中でないか）だけを見る。
    /// <list type="bullet">
    /// <item>ドラッグ＆ドロップ：押した札から上を掴み、離した位置で重なりの最も大きい山へ動かす。重ならなければ元に戻る。</item>
    /// <item>クリック→クリック：札をクリックすると選び、別の山をクリックするとそこへ動かす。同じ札をもう一度クリックすると外す。</item>
    /// <item>ダブルクリック：一番上の札を組札へ送る。</item>
    /// <item>山札のクリック：めくる（空なら戻す）。</item>
    /// </list>
    /// </summary>
    public sealed class CardDragInput
    {
        /// <summary>押した位置からこれだけ動かすとドラッグになる（札の幅に対する比）。</summary>
        public const double DragThreshold = .15;

        private readonly double _doubleClickSeconds;
        private Press? _press;
        private (TableHit Hit, double Time)? _lastClick;

        public CardDragInput(double doubleClickSeconds)
        {
            if (!double.IsFinite(doubleClickSeconds) || doubleClickSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(doubleClickSeconds), doubleClickSeconds, "0 以上の有限の値で指定してください。");
            }

            _doubleClickSeconds = doubleClickSeconds;
        }

        /// <summary>クリックで選んでいる札。</summary>
        public PileRun? Selected { get; private set; }

        /// <summary>ドラッグ中の札。</summary>
        public PileRun? Dragging => _press is { IsDragging: true } press ? press.Run : null;

        /// <summary>ドラッグ中の、底の札の中心。</summary>
        public TablePoint DragBaseCenter => _press is Press press ? press.Pointer - press.Grab : default;

        /// <summary>押している途中か（ドラッグ前を含む）。</summary>
        public bool IsPressed => _press != null;

        public PointerAction PointerDown(IReadOnlyTable table, TablePoint point, double time)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            _press = null;
            if (TableLayout.HitTest(table, point) is not TableHit hit)
            {
                Selected = null;
                return PointerAction.None;
            }

            if (hit.Pile == PileId.Stock)
            {
                Selected = null;
                _lastClick = null;
                return PointerAction.ClickStock;
            }

            if (Selected is PileRun selected && hit.Pile != selected.Pile)
            {
                Selected = null;
                _lastClick = null;
                return PointerAction.Move(Command.Move(selected.Pile, hit.Pile, selected.Count));
            }

            var count = table.CountOf(hit.Pile);
            if (hit.Index < 0 || hit.Index < table.FaceDownCountOf(hit.Pile))
            {
                Selected = null;
                return PointerAction.None;
            }

            if (_lastClick is (TableHit last, double lastTime) && last == hit && hit.Index == count - 1 && time - lastTime <= _doubleClickSeconds)
            {
                Selected = null;
                _lastClick = null;
                return PointerAction.SendToFoundation(hit.Pile);
            }

            var center = TableLayout.CardCenter(table, hit.Pile, hit.Index);
            _press = new Press(hit, new PileRun(hit.Pile, count - hit.Index), point - center, point);
            return PointerAction.None;
        }

        public void PointerMove(TablePoint point)
        {
            if (_press is not Press press || !point.IsFinite) { return; }
            press.Pointer = point;
            if (!press.IsDragging && point.DistanceTo(press.Start) >= DragThreshold * TableLayout.CardWidth)
            {
                press.IsDragging = true;
                Selected = null;
                _lastClick = null;
            }
        }

        public PointerAction PointerUp(IReadOnlyTable table, TablePoint point, double time)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (_press is not Press press) { return PointerAction.None; }
            PointerMove(point);
            _press = null;
            if (press.IsDragging)
            {
                return TableLayout.DropTarget(table, press.Run.Pile, press.Pointer - press.Grab) is PileId target
                    ? PointerAction.Move(Command.Move(press.Run.Pile, target, press.Run.Count))
                    : PointerAction.None;
            }

            Selected = Selected == press.Run ? null : press.Run;
            _lastClick = (press.Hit, time);
            return PointerAction.None;
        }

        /// <summary>押下・ドラッグ・選択を捨てる（フォーカスを失った、画面が変わった、盤が変わった）。</summary>
        public void Cancel()
        {
            _press = null;
            Selected = null;
            _lastClick = null;
        }

        private sealed class Press
        {
            public Press(TableHit hit, PileRun run, TablePoint grab, TablePoint start)
            {
                Hit = hit;
                Run = run;
                Grab = grab;
                Start = start;
                Pointer = start;
            }

            public TableHit Hit { get; }

            public PileRun Run { get; }

            /// <summary>押した位置の、底の札の中心からのずれ。</summary>
            public TablePoint Grab { get; }

            public TablePoint Start { get; }

            public TablePoint Pointer { get; set; }

            public bool IsDragging { get; set; }
        }
    }
}
