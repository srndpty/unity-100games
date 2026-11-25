using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>区画・出現の待ち行列・画面の送り。</summary>
    public sealed partial class Brawl
    {
        private readonly SpawnSlot[] _queue;
        private int _queueCount;

        public int SectionIndex { get; private set; }

        public Section CurrentSection => Stage.Sections[SectionIndex];

        /// <summary>区画で画面が止まっているか。false の間は「GO →」で先へ進める。</summary>
        public bool Locked { get; private set; }

        public int SectionTick { get; private set; }

        /// <summary>今の区画でまだ出ていない敵の数。</summary>
        public int Pending
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _queueCount; i++)
                {
                    if (!_queue[i].Spawned) { count++; }
                }

                return count;
            }
        }

        public bool BossSpawned { get; private set; }

        private void EnterSection(int index)
        {
            SectionIndex = index;
            var section = Stage.Sections[index];
            CameraX = section.CameraX;
            Locked = true;
            SectionTick = 0;
            _queueCount = section.Waves.Count;
            for (int i = 0; i < _queueCount; i++) { _queue[i] = new SpawnSlot(false); }
        }

        private void SpawnFromQueue()
        {
            if (!Locked) { return; }
            SectionTick++;
            var waves = CurrentSection.Waves;
            for (int i = 0; i < _queueCount; i++)
            {
                if (_queue[i].Spawned) { continue; }
                var wave = waves[i];
                if (wave.Delay > SectionTick) { break; }
                if (ActiveEnemies >= _config.MaxActiveEnemies) { break; }
                int half = _config.SpawnMargin / 2;
                int x = wave.Side == SpawnSide.Left ? CameraX - half : CameraX + _config.ViewWidth + half;
                Spawn(wave.Kind, x, wave.Z, wave.Side == SpawnSide.Left ? 1 : -1, wave.Food);
                if (wave.Kind == ActorKind.Boss) { BossSpawned = true; }
                _queue[i] = new SpawnSlot(true);
            }
        }

        private void Settle()
        {
            if (Outcome != Outcome.Running || Stage.IsArena) { return; }
            if (Locked)
            {
                if (CurrentSection.HasBoss)
                {
                    if (BossSpawned && !BossPresent()) { Outcome = Outcome.Cleared; }
                    return;
                }

                if (Pending == 0 && ActiveEnemies == 0) { Locked = false; }
                return;
            }

            var next = Stage.Sections[SectionIndex + 1];
            int target = Math.Min(Player.X - (_config.ViewWidth / 2), next.CameraX);
            if (target > CameraX) { CameraX += Math.Min(target - CameraX, _config.ScrollSpeed); }
            if (CameraX >= next.CameraX) { EnterSection(SectionIndex + 1); }
        }

        private bool BossPresent()
        {
            for (int i = 1; i < _actors.Length; i++)
            {
                if (_actors[i].Active && _actors[i].Kind == ActorKind.Boss) { return true; }
            }

            return false;
        }

        private void MixStage(ref StateHash hash)
        {
            hash.Mix(SectionIndex);
            hash.Mix(Locked);
            hash.Mix(SectionTick);
            hash.Mix(BossSpawned);
            hash.Mix(_queueCount);
            for (int i = 0; i < _queueCount; i++) { hash.Mix(_queue[i].Spawned); }
        }

        private readonly struct SpawnSlot
        {
            public SpawnSlot(bool spawned) => Spawned = spawned;

            public bool Spawned { get; }
        }
    }
}
