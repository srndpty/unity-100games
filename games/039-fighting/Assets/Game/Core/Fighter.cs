namespace Game.Core
{
    /// <summary>1 人の状態。対戦が毎 Tick 書き換える。</summary>
    public sealed class Fighter
    {
        public Fighter(int index, Character character)
        {
            Index = index;
            Character = character;
            MaxHealth = character.Health;
            Health = character.Health;
            Facing = index == 0 ? 1 : -1;
            Pose = BodyPose.Stand;
        }

        public int Index { get; }

        public Character Character { get; }

        public int X { get; set; }

        public int Y { get; set; }

        public int Vx { get; set; }

        public int Vy { get; set; }

        public int Facing { get; set; }

        public int Health { get; set; }

        public int MaxHealth { get; }

        public FighterState State { get; set; }

        public BodyPose Pose { get; set; }

        public int Remaining { get; set; }

        public Move? Move { get; set; }

        public int Frame { get; set; }

        public int Activation { get; set; }

        public int LandedActivation { get; set; } = -1;

        public bool CancelOpen { get; set; }

        public bool AlreadyHit => LandedActivation == Activation;

        public Fighter Copy()
        {
            var copy = new Fighter(Index, Character)
            {
                X = X,
                Y = Y,
                Vx = Vx,
                Vy = Vy,
                Facing = Facing,
                Health = Health,
                State = State,
                Pose = Pose,
                Remaining = Remaining,
                Move = Move,
                Frame = Frame,
                Activation = Activation,
                LandedActivation = LandedActivation,
                CancelOpen = CancelOpen,
            };
            return copy;
        }
    }
}
