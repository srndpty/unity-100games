using System;

namespace Game.Core.Play
{
    public enum Refusal
    {
        None,

        /// <summary>脱出した後。</summary>
        Escaped,

        /// <summary>会話中に会話以外の操作をした。</summary>
        Talking,

        /// <summary>会話中でないのに話題を選んだ・会話を終えた。</summary>
        NotTalking,

        /// <summary>今の部屋にない・隠れている物を対象にした。</summary>
        NotHere,

        /// <summary>持っていない持ち物を使った・対象にした。</summary>
        NotHeld,

        /// <summary>動詞と対象の組がない（持ち物を取る、持ち物どうしの「見る」に道具など）。</summary>
        NotAllowed,

        /// <summary>番号や位置が範囲外。</summary>
        OutOfRange,

        /// <summary>今は選べない話題。</summary>
        Unavailable,
    }

    /// <summary>操作の結果。拒否なら世界の状態は何も変わっていない。</summary>
    public readonly struct Outcome
    {
        private Outcome(Refusal reason) => Reason = reason;

        public Refusal Reason { get; }
        public bool Accepted => Reason == Refusal.None;

        public static Outcome Ok => default;

        public static Outcome Refuse(Refusal reason) => new(reason);
    }

    /// <summary>
    /// ルール表の検証で起きないことを保証した違反（持ち物を二度手に入れる、持っていない物を失うなど）。
    /// 試合中に起きたら検証の穴なので止める（fail-closed）。
    /// </summary>
    public sealed class RuleViolationException : Exception
    {
        public RuleViolationException()
        {
        }

        public RuleViolationException(string message)
            : base(message)
        {
        }

        public RuleViolationException(string message, Exception inner)
            : base(message, inner)
        {
        }

        public RuleViolationException(int line, string message)
            : base(message)
        {
            Line = line;
        }

        public int Line { get; }
    }
}
