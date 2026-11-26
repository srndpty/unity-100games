namespace Game.Core
{
    /// <summary>
    /// 解決キューの 1 件。カードや敵の行動の効果（<see cref="EffectDef"/>）を、出した者と選んだ対象を付けて展開したもの。
    /// 全体・無作為の対象は積んだ時ではなく実行の時点で決める。
    /// </summary>
    internal readonly struct Command
    {
        public Command(EffectDef effect, int source, int chosen)
        {
            Effect = effect;
            Source = source;
            Chosen = chosen;
        }

        public EffectDef Effect { get; }

        /// <summary>出した者の番号。</summary>
        public int Source { get; }

        /// <summary>選んだ敵の番号（<see cref="TargetMode.Chosen"/> のとき）。なければ -1。</summary>
        public int Chosen { get; }
    }

    public enum RejectReason
    {
        None,

        /// <summary>戦闘が終わっている。</summary>
        BattleOver,

        /// <summary>手札にないカード。</summary>
        NotInHand,

        /// <summary>エネルギーが足りない。</summary>
        NotEnoughEnergy,

        /// <summary>対象を選ぶカードに対象がない。</summary>
        TargetRequired,

        /// <summary>対象を選ばないカードに対象を付けた。</summary>
        TargetNotAllowed,

        /// <summary>対象が存在しないか倒れている。</summary>
        TargetInvalid,

        /// <summary>今の段階ではできない操作（報酬の画面で戦うなど）。</summary>
        WrongStage,

        /// <summary>選択肢の番号・カードが範囲外か存在しない。</summary>
        InvalidChoice,

        /// <summary>一度だけの操作（報酬のカード・除去）を済ませている。</summary>
        AlreadyDone,

        /// <summary>除去するとデッキが下限より少なくなる。</summary>
        DeckTooSmall,
    }

    /// <summary>操作の結果。拒否したときは状態を何も変えていない。</summary>
    public readonly struct PlayResult
    {
        private PlayResult(RejectReason reason) => Reason = reason;

        public static PlayResult Ok => new(RejectReason.None);

        public RejectReason Reason { get; }

        public bool Accepted => Reason == RejectReason.None;

        public static PlayResult Reject(RejectReason reason) => new(reason);

        public override string ToString() => Accepted ? "Ok" : $"Rejected({Reason})";
    }
}
