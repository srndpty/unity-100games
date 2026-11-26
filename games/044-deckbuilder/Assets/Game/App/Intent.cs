namespace Game.App
{
    public enum IntentKind
    {
        /// <summary>決定（Enter）。タイトルは開始、操作説明は戻る、戦闘は選んだ対象で確定、報酬は先へ進む、結果はタイトルへ。</summary>
        Confirm,

        /// <summary>取り消し（Esc・右クリック）。戦闘で選択中なら選択を消す、何も選んでいなければ一時停止。一時停止・操作説明は戻る。</summary>
        Cancel,

        /// <summary>操作説明を開く（タイトル）。</summary>
        HowTo,

        /// <summary>終了（タイトル）。アプリの終了は Unity 層が行う。</summary>
        Quit,

        /// <summary>タイトルへ戻る（一時停止中・結果）。</summary>
        ToTitle,

        /// <summary>数字キー。<see cref="Intent.Value"/> = 0〜9（1 キーが 0）。戦闘は手札の位置、ルート・報酬・休憩は選択肢。</summary>
        Number,

        /// <summary>手札のカードをクリックした。Value = 実体 ID。</summary>
        SelectCard,

        /// <summary>敵をクリックした。Value = 敵の番号。</summary>
        ClickEnemy,

        /// <summary>対象のカーソルを動かす（←→・Tab）。Value = +1 か -1。</summary>
        CycleTarget,

        EndTurn,

        /// <summary>選択肢をクリックした（ルートの遭遇・報酬のカード・休憩）。Value = 番号。</summary>
        Option,

        /// <summary>報酬の画面でデッキのカードを除去の候補に選ぶ。Value = 実体 ID（同じカードでもう一度なら取り消し）。</summary>
        PickRemoval,

        /// <summary>除去の候補を除去する。</summary>
        ConfirmRemoval,

        /// <summary>報酬を終えて先へ進む。</summary>
        Leave,
    }

    /// <summary>プレイヤーの意図 1 つ。マウスとキーは同じ列に並べ、届いた順に処理する（012）。</summary>
    public readonly struct Intent
    {
        public Intent(IntentKind kind, int value = 0)
        {
            Kind = kind;
            Value = value;
        }

        public IntentKind Kind { get; }

        public int Value { get; }

        public override string ToString() => $"{Kind}({Value})";
    }
}
