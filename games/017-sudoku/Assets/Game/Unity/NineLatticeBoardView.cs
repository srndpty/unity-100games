using System;
using Game.Core;
using UnityEngine;
using Grid = Game.Core.Grid;

namespace Game.Unity
{
    /// <summary>マスの塗りの種類。優先度の高い順に判定する。</summary>
    public enum CellTint
    {
        Plain,
        Peer,
        SameDigit,
        Selected,
        Hint,
        Conflict,
    }

    /// <summary>
    /// 盤の描画。Prefab に配置済みの SpriteRenderer を使い回し、Core の盤を読むだけで書き戻さない。
    /// マスごとに、塗り・大きい数字・3×3 の小さい候補の数字を持つ。ブロックの境目は少し広く空ける。
    /// </summary>
    public sealed class NineLatticeBoardView : MonoBehaviour
    {
        /// <summary>ブロックの境目で、マスの間をさらに空ける幅。</summary>
        public const float BoxGap = .08f;

        public const float CellScale = .94f;
        private const float DigitScale = .78f;
        private const float NoteScale = .27f;
        private const float NotePitch = .29f;

        private static readonly Color _plain = new(.97f, .95f, .9f);
        private static readonly Color _peer = new(.86f, .9f, .95f);
        private static readonly Color _sameDigit = new(.73f, .83f, .96f);
        private static readonly Color _selected = new(1f, .86f, .52f);
        private static readonly Color _hint = new(.68f, .9f, .7f);
        private static readonly Color _conflict = new(.98f, .66f, .64f);
        private static readonly Color _givenInk = new(.11f, .14f, .26f);
        private static readonly Color _playerInk = new(.14f, .36f, .74f);
        private static readonly Color _noteInk = new(.38f, .4f, .5f);

        [SerializeField] private SpriteRenderer[] _cells;
        [SerializeField] private SpriteRenderer[] _digits;
        [SerializeField] private SpriteRenderer[] _notes;
        [SerializeField] private Sprite[] _digitSprites;
        [SerializeField] private SpriteRenderer _selection;

        private readonly CellTint[] _tints = new CellTint[Board.CellCount];

        public bool IsSelectionVisible => _selection.enabled;
        public Vector3 SelectionPosition => _selection.transform.position;

        public static Color GivenInk => _givenInk;
        public static Color PlayerInk => _playerInk;

        /// <summary>そのマスに表示中の大きい数字。なければ 0。</summary>
        public int DigitAt(int cell) => DigitOf(_digits[cell]);

        public Color DigitColorAt(int cell) => _digits[cell].color;

        /// <summary>そのマスに表示中の候補の集合（<see cref="Digits"/>）。</summary>
        public int NotesAt(int cell)
        {
            var mask = 0;
            for (var digit = 1; digit <= Board.Size; digit++)
            {
                if (_notes[NoteIndex(cell, digit)].enabled) { mask |= Digits.Bit(digit); }
            }

            return mask;
        }

        public CellTint TintAt(int cell) => _tints[cell];

        /// <summary>マスの中心のワールド座標。</summary>
        public Vector3 CellWorld(int cell) => transform.TransformPoint(CellLocal(Board.Row(cell), Board.Column(cell)));

        /// <summary>
        /// ワールド座標を盤の座標（左上の角が (0, 0)、1 マスが 1、右と下が正）に直す。
        /// マスの間の線とブロックの境目は隣のマスに含める。盤の外は範囲外の値で返す。
        /// </summary>
        public Vector2 ToBoard(Vector3 world)
        {
            var local = transform.InverseTransformPoint(world);
            return new Vector2(Axis(local.x), Axis(-local.y));
        }

        /// <summary>マスと数字をそれぞれの位置に並べる。シーン生成時に呼ぶ。</summary>
        public void Arrange()
        {
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                var center = CellLocal(Board.Row(cell), Board.Column(cell));
                _cells[cell].transform.localPosition = center;
                _cells[cell].transform.localScale = new Vector3(CellScale, CellScale, 1);
                _digits[cell].transform.localPosition = center;
                _digits[cell].transform.localScale = new Vector3(DigitScale, DigitScale, 1);
                for (var digit = 1; digit <= Board.Size; digit++)
                {
                    var note = _notes[NoteIndex(cell, digit)];
                    var offset = new Vector3((((digit - 1) % 3) - 1) * NotePitch, (1 - ((digit - 1) / 3)) * NotePitch, 0);
                    note.transform.localPosition = center + offset;
                    note.transform.localScale = new Vector3(NoteScale, NoteScale, 1);
                    note.sprite = _digitSprites[digit - 1];
                    note.color = _noteInk;
                }
            }
        }

        /// <summary>与えだけの盤を描く。タイトルの飾りに使う。</summary>
        public void RenderStill(Grid givens)
        {
            if (givens == null) { throw new ArgumentNullException(nameof(givens)); }
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                SetDigit(cell, givens[cell], _givenInk);
                SetNotes(cell, 0);
                SetTint(cell, CellTint.Plain);
            }

            _selection.enabled = false;
        }

        /// <summary>
        /// 試合の状態を描く。selected は選んでいるマス（null なら出さない）、conflict は重複した相手のマス、hint はヒントで指したマス（なければ -1）。
        /// </summary>
        public void Render(SudokuGame game, int? selected, int conflict, int hint)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            var selectedDigit = selected is int s ? game.ValueAt(s) : 0;
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                var value = game.ValueAt(cell);
                SetDigit(cell, value, game.IsGiven(cell) ? _givenInk : _playerInk);
                SetNotes(cell, game.NotesAt(cell));

                var tint = CellTint.Plain;
                if (cell == conflict) { tint = CellTint.Conflict; }
                else if (cell == hint) { tint = CellTint.Hint; }
                else if (cell == selected) { tint = CellTint.Selected; }
                else if (selectedDigit != 0 && value == selectedDigit) { tint = CellTint.SameDigit; }
                else if (selected is int at && Board.AreAligned(cell, at)) { tint = CellTint.Peer; }
                SetTint(cell, tint);
            }

            _selection.enabled = selected.HasValue;
            if (selected is int position) { _selection.transform.localPosition = CellLocal(Board.Row(position), Board.Column(position)); }
        }

        /// <summary>行・列の番号から中心の位置を求める（盤の中央が原点、y は上向き）。</summary>
        public static Vector3 CellLocal(int row, int column) => new(Offset(column), -Offset(row), 0);

        private static float Offset(int index) => index - ((Board.Size - 1) / 2f) + (((index / Board.BoxSize) - 1) * BoxGap);

        private static float Axis(float local)
        {
            for (var index = 0; index < Board.Size; index++)
            {
                var center = Offset(index);
                var low = index % Board.BoxSize == 0 && index > 0 ? .5f + (BoxGap / 2f) : .5f;
                var high = index % Board.BoxSize == Board.BoxSize - 1 && index < Board.Size - 1 ? .5f + (BoxGap / 2f) : .5f;
                if (local >= center - low && local < center + high)
                {
                    // マスの中を 0〜1 に直す。境目の広い部分もそのマスに含める。
                    return index + Mathf.Clamp((local - (center - low)) / (low + high), 0f, .999f);
                }
            }

            return local < 0 ? -1f : Board.Size + 1f;
        }

        private static int NoteIndex(int cell, int digit) => (cell * Board.Size) + digit - 1;

        private int DigitOf(SpriteRenderer renderer)
        {
            if (!renderer.enabled) { return 0; }
            return Array.IndexOf(_digitSprites, renderer.sprite) + 1;
        }

        private void SetDigit(int cell, int digit, Color ink)
        {
            var renderer = _digits[cell];
            renderer.enabled = digit != 0;
            if (digit == 0) { return; }
            renderer.sprite = _digitSprites[digit - 1];
            renderer.color = ink;
        }

        private void SetNotes(int cell, int mask)
        {
            for (var digit = 1; digit <= Board.Size; digit++) { _notes[NoteIndex(cell, digit)].enabled = Digits.Has(mask, digit); }
        }

        private void SetTint(int cell, CellTint tint)
        {
            _tints[cell] = tint;
            _cells[cell].color = tint switch
            {
                CellTint.Peer => _peer,
                CellTint.SameDigit => _sameDigit,
                CellTint.Selected => _selected,
                CellTint.Hint => _hint,
                CellTint.Conflict => _conflict,
                _ => _plain,
            };
        }
    }
}
