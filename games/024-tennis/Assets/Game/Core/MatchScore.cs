using System;

namespace Game.Core
{
    /// <summary>得点が入ったときに起きたこと。</summary>
    [Flags]
    public enum ScoreChange
    {
        None = 0,
        Game = 1,
        Set = 2,
        TiebreakStarted = 4,
    }

    /// <summary>
    /// 得点・ゲーム・セット（1 セットで試合終了）とサーブ順の状態機械。ラリーとは独立で、ポイントの勝者を入れると次の状態になる。
    /// <list type="bullet">
    /// <item>通常のゲーム：0・15・30・40。4 点以上かつ 2 点差でゲーム（40-40 からはデュース・アドバンテージ）。</item>
    /// <item>セット：<see cref="TiebreakAtGames"/> = T のとき、T ゲーム以上を 2 差で取れば勝ち（T=6 なら 6-4・7-5）。T-T でタイブレーク。</item>
    /// <item>タイブレーク：<see cref="TiebreakPoints"/> 点以上かつ 2 点差で取ればセット。T=0 なら最初からタイブレーク（既定の 7 ポイント先取の 1 ゲーム）。</item>
    /// <item>サーブ：通常のゲームはゲームごとに交代。タイブレークは最初の 1 点をそのゲームの番の人、以後 2 点ごとに交代（A・BB・AA・…）。</item>
    /// <item>サーブのコート：そのゲームのポイント数の和が偶数ならデュースサイド、奇数ならアドサイド。</item>
    /// </list>
    /// </summary>
    public sealed class MatchScore
    {
        public const int MaxTiebreakAtGames = 6;
        public const int MaxTiebreakPoints = 15;

        private int _nearGames;
        private int _farGames;
        private int _nearPoints;
        private int _farPoints;

        public MatchScore(int tiebreakAtGames, int tiebreakPoints, Side firstServer)
        {
            if (tiebreakAtGames < 0 || tiebreakAtGames > MaxTiebreakAtGames)
            {
                throw new ArgumentOutOfRangeException(nameof(tiebreakAtGames), tiebreakAtGames, $"0〜{MaxTiebreakAtGames} にしてください。");
            }

            if (tiebreakPoints < 1 || tiebreakPoints > MaxTiebreakPoints)
            {
                throw new ArgumentOutOfRangeException(nameof(tiebreakPoints), tiebreakPoints, $"1〜{MaxTiebreakPoints} にしてください。");
            }

            if (!Enum.IsDefined(typeof(Side), firstServer)) { throw new ArgumentOutOfRangeException(nameof(firstServer)); }

            TiebreakAtGames = tiebreakAtGames;
            TiebreakPoints = tiebreakPoints;
            GameServer = firstServer;
            InTiebreak = tiebreakAtGames == 0;
        }

        public int TiebreakAtGames { get; }

        public int TiebreakPoints { get; }

        public bool InTiebreak { get; private set; }

        /// <summary>今のゲーム（タイブレークなら最初の 1 点）のサーバー。</summary>
        public Side GameServer { get; private set; }

        /// <summary>セット（試合）の勝者。決まっていなければ null。</summary>
        public Side? Winner { get; private set; }

        public bool IsOver => Winner.HasValue;

        /// <summary>今のゲームで行ったポイント数。</summary>
        public int PointsPlayed => _nearPoints + _farPoints;

        /// <summary>次のポイントのサーバー。</summary>
        public Side Server
        {
            get
            {
                if (!InTiebreak) { return GameServer; }
                var k = PointsPlayed;
                return k == 0 || ((k + 1) / 2) % 2 == 0 ? GameServer : Court.Other(GameServer);
            }
        }

        /// <summary>次のポイントでサーブを打つコート。</summary>
        public ServeCourt ServeCourt => PointsPlayed % 2 == 0 ? ServeCourt.Deuce : ServeCourt.Ad;

        public int Games(Side side) => side == Side.Near ? _nearGames : _farGames;

        /// <summary>今のゲームの得点（0, 1, 2, …。通常のゲームの表示は 0・15・30・40 に読み替える）。</summary>
        public int Points(Side side) => side == Side.Near ? _nearPoints : _farPoints;

        public MatchScore Clone() => (MatchScore)MemberwiseClone();

        /// <summary>ポイントの勝者を入れる。試合が終わっていれば例外。</summary>
        public ScoreChange Award(Side winner)
        {
            if (IsOver) { throw new InvalidOperationException("試合は終わっています。"); }
            if (!Enum.IsDefined(typeof(Side), winner)) { throw new ArgumentOutOfRangeException(nameof(winner)); }

            if (winner == Side.Near) { _nearPoints++; } else { _farPoints++; }

            var mine = Points(winner);
            var theirs = Points(Court.Other(winner));
            var need = InTiebreak ? TiebreakPoints : 4;
            if (mine < need || mine - theirs < 2) { return ScoreChange.None; }

            return WinGame(winner);
        }

        private ScoreChange WinGame(Side winner)
        {
            if (winner == Side.Near) { _nearGames++; } else { _farGames++; }

            var mine = Games(winner);
            var theirs = Games(Court.Other(winner));
            if (InTiebreak || (mine >= TiebreakAtGames && mine - theirs >= 2))
            {
                // 最後のゲームの得点は結果の表示のために残す。
                Winner = winner;
                return ScoreChange.Game | ScoreChange.Set;
            }

            _nearPoints = 0;
            _farPoints = 0;

            // 次のゲームのサーバー：ゲームごとに交代。
            GameServer = Court.Other(GameServer);

            if (mine == TiebreakAtGames && theirs == TiebreakAtGames)
            {
                InTiebreak = true;
                return ScoreChange.Game | ScoreChange.TiebreakStarted;
            }

            return ScoreChange.Game;
        }
    }
}
