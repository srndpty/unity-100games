namespace Game.Core
{
    /// <summary>敵の考えと攻撃権。セッション2で作る（今は立ったまま）。</summary>
    public sealed partial class Brawl
    {
        private static void UpdateTokens()
        {
        }

        private static void EnemyNeutral(Actor a)
        {
            SetState(a, ActorState.Idle);
        }

        private static void FinishEnemyAttack(Actor a)
        {
            a.HasToken = false;
        }

        private static void ReleaseToken(Actor a, bool cooldown)
        {
            if (!a.HasToken) { return; }
            a.HasToken = false;
            if (cooldown) { a.Cooldown = 1; }
        }
    }
}
