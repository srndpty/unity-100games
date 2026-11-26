using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>技の効果の種類。効果は書いた順に解決する。</summary>
    public enum EffectKind
    {
        /// <summary>技の威力でダメージを与える。</summary>
        Damage = 1,

        /// <summary>状態異常を確率で付与する。</summary>
        Inflict = 2,

        /// <summary>自分の最大 HP の割合だけ回復する。</summary>
        Heal = 3,

        /// <summary>能力の段階を上げ下げする（戦闘中のみ、交代で戻る）。</summary>
        Stat = 4,
    }

    public enum EffectTarget
    {
        Foe = 1,
        Self = 2,
    }

    public enum StatKind
    {
        Attack = 0,
        Defense = 1,
        Speed = 2,
    }

    public enum Direction
    {
        Up = 0,
        Right = 1,
        Down = 2,
        Left = 3,
    }

    public sealed class TypeDef
    {
        internal TypeDef(string id, string name, int index)
        {
            Id = id;
            Name = name;
            Index = index;
        }

        public string Id { get; }

        public string Name { get; }

        public int Index { get; }
    }

    /// <summary>状態異常（Statuses.txt）。持続は付与した時点で決め、解除予定のターン番号として持つ。</summary>
    public sealed class StatusDef
    {
        internal StatusDef(string id, string name, int index, bool blocksAction, int dotDivisor, int minTurns, int maxTurns, int catchPercent)
        {
            Id = id;
            Name = name;
            Index = index;
            BlocksAction = blocksAction;
            DotDivisor = dotDivisor;
            MinTurns = minTurns;
            MaxTurns = maxTurns;
            CatchPercent = catchPercent;
        }

        public string Id { get; }

        public string Name { get; }

        public int Index { get; }

        /// <summary>行動できない（眠り）。</summary>
        public bool BlocksAction { get; }

        /// <summary>ターンの終わりに最大 HP のこの分の 1 を失う（0 なら失わない、最低 1）。</summary>
        public int DotDivisor { get; }

        public int MinTurns { get; }

        public int MaxTurns { get; }

        /// <summary>捕獲値に掛ける百分率。</summary>
        public int CatchPercent { get; }
    }

    public sealed class EffectDef
    {
        internal EffectDef(EffectKind kind, EffectTarget target, StatusDef? status, int percent, StatKind stat, int stages)
        {
            Kind = kind;
            Target = target;
            Status = status;
            Percent = percent;
            Stat = stat;
            Stages = stages;
        }

        public EffectKind Kind { get; }

        public EffectTarget Target { get; }

        /// <summary>付与する状態異常（Inflict のみ）。</summary>
        public StatusDef? Status { get; }

        /// <summary>Inflict の確率、Heal の割合（百分率）。</summary>
        public int Percent { get; }

        public StatKind Stat { get; }

        public int Stages { get; }
    }

    /// <summary>技（Moves.txt）。属性が null なら属性なし（相性・一致補正なし）。PP 0 は「わるあがき」用で減らない。</summary>
    public sealed class MoveDef
    {
        internal MoveDef(string id, string name, int index, TypeDef? type, int power, int accuracy, int pp, int priority, EffectDef[] effects)
        {
            Id = id;
            Name = name;
            Index = index;
            Type = type;
            Power = power;
            Accuracy = accuracy;
            Pp = pp;
            Priority = priority;
            Effects = Array.AsReadOnly(effects);
        }

        public string Id { get; }

        public string Name { get; }

        public int Index { get; }

        public TypeDef? Type { get; }

        public int Power { get; }

        /// <summary>命中（1〜100）。<c>Next(100) &lt; 命中</c> で当たる。</summary>
        public int Accuracy { get; }

        public int Pp { get; }

        public int Priority { get; }

        public IReadOnlyList<EffectDef> Effects { get; }
    }

    public sealed class LearnDef
    {
        internal LearnDef(int level, MoveDef move)
        {
            Level = level;
            Move = move;
        }

        public int Level { get; }

        public MoveDef Move { get; }
    }

    /// <summary>種族（Species.txt）。</summary>
    public sealed class SpeciesDef
    {
        internal SpeciesDef(
            string id,
            string name,
            int index,
            TypeDef type,
            int hp,
            int attack,
            int defense,
            int speed,
            int catchRate,
            int expYield,
            LearnDef[] learnset)
        {
            Id = id;
            Name = name;
            Index = index;
            Type = type;
            BaseHp = hp;
            BaseAttack = attack;
            BaseDefense = defense;
            BaseSpeed = speed;
            CatchRate = catchRate;
            ExpYield = expYield;
            Learnset = Array.AsReadOnly(learnset);
        }

        public string Id { get; }

        public string Name { get; }

        public int Index { get; }

        public TypeDef Type { get; }

        public int BaseHp { get; }

        public int BaseAttack { get; }

        public int BaseDefense { get; }

        public int BaseSpeed { get; }

        /// <summary>捕獲率（1〜255）。</summary>
        public int CatchRate { get; }

        public int ExpYield { get; }

        /// <summary>覚える技（レベルの昇順、同じレベルは書いた順）。</summary>
        public IReadOnlyList<LearnDef> Learnset { get; }
    }

    public sealed class WildDef
    {
        internal WildDef(SpeciesDef species, int minLevel, int maxLevel, int weight)
        {
            Species = species;
            MinLevel = minLevel;
            MaxLevel = maxLevel;
            Weight = weight;
        }

        public SpeciesDef Species { get; }

        public int MinLevel { get; }

        public int MaxLevel { get; }

        public int Weight { get; }
    }

    /// <summary>草むらの遭遇表。地図と草むらの文字ごとに 1 つ。</summary>
    public sealed class GrassDef
    {
        private readonly List<WildDef> _wild = new();

        internal GrassDef(char tile, int ratePercent)
        {
            Tile = tile;
            RatePercent = ratePercent;
            Wild = _wild.AsReadOnly();
        }

        public char Tile { get; }

        public int RatePercent { get; }

        public IReadOnlyList<WildDef> Wild { get; }

        internal void Add(WildDef wild) => _wild.Add(wild);

        internal int[] Weights()
        {
            var w = new int[_wild.Count];
            for (int i = 0; i < w.Length; i++) { w[i] = _wild[i].Weight; }
            return w;
        }
    }

    public sealed class TrainerMonDef
    {
        internal TrainerMonDef(SpeciesDef species, int level)
        {
            Species = species;
            Level = level;
        }

        public SpeciesDef Species { get; }

        public int Level { get; }
    }

    /// <summary>トレーナー（World.txt）。道を塞ぐマスに立ち、勝つと去る。</summary>
    public sealed class TrainerDef
    {
        private readonly List<TrainerMonDef> _party = new();

        internal TrainerDef(string id, string name, int index, string mapId, int x, int y, Direction facing)
        {
            Id = id;
            Name = name;
            Index = index;
            MapId = mapId;
            X = x;
            Y = y;
            Facing = facing;
            Party = _party.AsReadOnly();
        }

        public string Id { get; }

        public string Name { get; }

        public int Index { get; }

        public string MapId { get; }

        public int X { get; }

        public int Y { get; }

        public Direction Facing { get; }

        public string Intro { get; internal set; } = string.Empty;

        public string Defeat { get; internal set; } = string.Empty;

        public IReadOnlyList<TrainerMonDef> Party { get; }

        internal void Add(TrainerMonDef mon) => _party.Add(mon);
    }

    public sealed class PortalDef
    {
        internal PortalDef(int x, int y, string targetMapId, int targetX, int targetY, Direction facing)
        {
            X = x;
            Y = y;
            TargetMapId = targetMapId;
            TargetX = targetX;
            TargetY = targetY;
            Facing = facing;
        }

        public int X { get; }

        public int Y { get; }

        public string TargetMapId { get; }

        public int TargetX { get; }

        public int TargetY { get; }

        public Direction Facing { get; }
    }

    /// <summary>地図のマスの種類。</summary>
    public static class Tiles
    {
        public const char Wall = '#';
        public const char Ground = '.';
        public const char Water = '~';
        public const char House = 'H';
        public const char Counter = 'C';
        public const char Road = '=';
        public const char Flower = '*';

        /// <summary>草むらに使える文字（遭遇表は文字ごと）。</summary>
        public const string GrassChars = "\";:";

        public static bool IsGrass(char c) => GrassChars.Contains(c, StringComparison.Ordinal);

        public static bool IsWalkableTerrain(char c) => c == Ground || c == Road || c == Flower || IsGrass(c);

        public static bool IsKnown(char c) => c == Wall || c == Water || c == House || c == Counter || IsWalkableTerrain(c);
    }

    /// <summary>地図（World.txt の map 〜 end）。</summary>
    public sealed class MapDef
    {
        private readonly string[] _rows;
        private readonly List<PortalDef> _portals = new();
        private readonly List<GrassDef> _grass = new();

        internal MapDef(string id, string name, int index, string[] rows)
        {
            Id = id;
            Name = name;
            Index = index;
            _rows = rows;
            Height = rows.Length;
            Width = rows.Length == 0 ? 0 : rows[0].Length;
            Portals = _portals.AsReadOnly();
            Grass = _grass.AsReadOnly();
        }

        public string Id { get; }

        public string Name { get; }

        public int Index { get; }

        public int Width { get; }

        public int Height { get; }

        public IReadOnlyList<PortalDef> Portals { get; }

        public IReadOnlyList<GrassDef> Grass { get; }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        /// <summary>範囲外は壁として返す。</summary>
        public char TileAt(int x, int y) => InBounds(x, y) ? _rows[y][x] : Tiles.Wall;

        public PortalDef? PortalAt(int x, int y)
        {
            foreach (var p in _portals)
            {
                if (p.X == x && p.Y == y) { return p; }
            }

            return null;
        }

        public GrassDef? GrassFor(char tile)
        {
            foreach (var g in _grass)
            {
                if (g.Tile == tile) { return g; }
            }

            return null;
        }

        internal void AddPortal(PortalDef portal) => _portals.Add(portal);

        internal void AddGrass(GrassDef grass) => _grass.Add(grass);
    }

    /// <summary>規則の値（World.txt の rule 行）。</summary>
    public sealed class Rules
    {
        private readonly List<SpeciesDef> _starters = new();

        public Rules()
        {
            Starters = _starters.AsReadOnly();
        }

        public int StepTicks { get; internal set; } = 8;

        public int GraceSteps { get; internal set; } = 3;

        public int PartyMax { get; internal set; } = 6;

        public int BoxMax { get; internal set; } = 30;

        public int LevelCap { get; internal set; } = 20;

        public int StarterLevel { get; internal set; } = 5;

        /// <summary>はじめから・回復所の補充で持つ捕獲玉の数（少なければこの数まで補う）。</summary>
        public int Balls { get; internal set; } = 5;

        /// <summary>はじめから・回復所の補充で持つ傷薬の数。</summary>
        public int Potions { get; internal set; } = 3;

        public int PotionHeal { get; internal set; } = 20;

        /// <summary>経験値表：レベル L に必要な累計経験値 = L^3 × 分子 / 分母。</summary>
        public int ExpCurveNumerator { get; internal set; } = 1;

        public int ExpCurveDenominator { get; internal set; } = 2;

        /// <summary>得る経験値 = 経験値の量 × 相手のレベル / この値（出た生存者で等分）。</summary>
        public int ExpYieldDivisor { get; internal set; } = 5;

        /// <summary>トレーナー戦の経験値の倍率（百分率）。</summary>
        public int TrainerExpPercent { get; internal set; } = 150;

        public string StartMapId { get; internal set; } = string.Empty;

        public int StartX { get; internal set; }

        public int StartY { get; internal set; }

        /// <summary>回復所のカウンターに話しかける位置（全滅したらここへ戻る）。</summary>
        public string CenterMapId { get; internal set; } = string.Empty;

        public int CenterX { get; internal set; }

        public int CenterY { get; internal set; }

        /// <summary>技の PP が全て 0 のときに使う技（PP を減らさない）。</summary>
        public MoveDef? Struggle { get; internal set; }

        /// <summary>最初に選べる 3 匹（属性ごとに 1 匹）。</summary>
        public IReadOnlyList<SpeciesDef> Starters { get; }

        internal void AddStarter(SpeciesDef species) => _starters.Add(species);
    }
}
