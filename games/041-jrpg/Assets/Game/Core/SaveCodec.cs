using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TileMotion;

namespace Game.Core
{
    public enum SaveErrorKind
    {
        None = 0,
        Empty = 1,
        Truncated = 2,
        SyntaxError = 3,
        DuplicateKey = 4,
        MissingKey = 5,
        VersionMismatch = 6,
        FingerprintMismatch = 7,
        UnknownId = 8,
        OutOfRange = 9,
        ChecksumMismatch = 10,
        NotAtChurch = 11,
    }

    public sealed class SaveError
    {
        public SaveError(SaveErrorKind kind, string message)
        {
            Kind = kind;
            Message = message ?? string.Empty;
        }

        public SaveErrorKind Kind { get; }
        public string Message { get; }

        public override string ToString() => $"{Kind}: {Message}";
    }

    /// <summary>
    /// 行指向テキスト（key=value + 末尾 checksum=XXXXXXXX\n）で <see cref="World"/> を直列化・検証付き復元する。
    /// </summary>
    public static class SaveCodec
    {
        public const int CurrentVersion = 1;
        private const string ChecksumPrefix = "checksum=";
        private const int ChecksumHexLength = 8;

        public static bool TryEncodeAtChurch(World world, out string text, out SaveError? error)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!world.CanSaveAtChurch())
            {
                text = string.Empty;
                error = new SaveError(SaveErrorKind.NotAtChurch, TownErrorText.Describe(TownActionError.NotAtChurch));
                return false;
            }

            text = EncodeUnchecked(world);
            error = null;
            return true;
        }

        public static string EncodeUnchecked(World world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var sb = new StringBuilder(512);
            AppendLine(sb, "version", CurrentVersion.ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "fingerprint", world.Data.Fingerprint.ToString("X8", CultureInfo.InvariantCulture));
            AppendLine(sb, "seed", world.MatchSeed.ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "gold", world.Gold.ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "battles", world.BattleCount.ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "ticks", world.PlayTicks.ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "map", world.Explorer.MapId);
            AppendLine(sb, "x", world.Explorer.X.ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "y", world.Explorer.Y.ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "facing", ((int)world.Explorer.Facing).ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "steps_since_enc", world.Explorer.StepsSinceEncounter.ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "total_steps", world.Explorer.TotalSteps.ToString(CultureInfo.InvariantCulture));
            AppendLine(sb, "enc_rng", world.Explorer.EncounterRng.State.ToString("X8", CultureInfo.InvariantCulture));

            for (var i = 0; i < world.Party.Count; i++)
            {
                var m = world.Party[i];
                var prefix = "m" + i.ToString(CultureInfo.InvariantCulture) + ".";
                AppendLine(sb, prefix + "class", m.Class.Id);
                AppendLine(sb, prefix + "level", m.Level.ToString(CultureInfo.InvariantCulture));
                AppendLine(sb, prefix + "exp", m.Exp.ToString(CultureInfo.InvariantCulture));
                AppendLine(sb, prefix + "hp", m.Hp.ToString(CultureInfo.InvariantCulture));
                AppendLine(sb, prefix + "mp", m.Mp.ToString(CultureInfo.InvariantCulture));
                AppendLine(sb, prefix + "weapon", m.WeaponId);
                AppendLine(sb, prefix + "armor", m.ArmorId);
            }

            var bag = world.Inventory.AllEntries();
            AppendLine(sb, "bag.count", bag.Count.ToString(CultureInfo.InvariantCulture));
            for (var i = 0; i < bag.Count; i++)
            {
                AppendLine(
                    sb,
                    "bag." + i.ToString(CultureInfo.InvariantCulture),
                    bag[i].ProductId + ":" + bag[i].Count.ToString(CultureInfo.InvariantCulture));
            }

            var activeFlags = world.Flags.ActiveFlags();
            AppendLine(sb, "flags.count", activeFlags.Count.ToString(CultureInfo.InvariantCulture));
            for (var i = 0; i < activeFlags.Count; i++)
            {
                AppendLine(sb, "flags." + i.ToString(CultureInfo.InvariantCulture), activeFlags[i]);
            }

            return SignBody(sb.ToString());
        }

        /// <summary>末尾に checksum 行を付与する（負例テストで意味エラーを作る際にも使用する）。</summary>
        public static string SignBody(string body)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (normalized.Length > 0 && normalized[normalized.Length - 1] != '\n')
            {
                normalized += "\n";
            }

            var sum = ComputeChecksum(normalized);
            return normalized + ChecksumPrefix + sum.ToString("X8", CultureInfo.InvariantCulture) + "\n";
        }

        public static bool TryDecode(GameData data, string? rawText, out World? world, out SaveError? error)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            world = null;
            if (string.IsNullOrEmpty(rawText))
            {
                error = new SaveError(SaveErrorKind.Empty, "セーブデータが空です。");
                return false;
            }

            // CRLF は正規化するが、末尾改行の欠落は途中切断として拒否する
            var text = rawText!.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (text[text.Length - 1] != '\n')
            {
                error = new SaveError(SaveErrorKind.Truncated, "セーブデータが途中で切れています。");
                return false;
            }

            // 末尾 1 行が checksum=XXXXXXXX\n であることを検査する
            var withoutFinalLf = text.Substring(0, text.Length - 1);
            var lastLf = withoutFinalLf.LastIndexOf('\n');
            if (lastLf < 0)
            {
                error = new SaveError(SaveErrorKind.Truncated, "セーブデータの本体がありません。");
                return false;
            }

            var checksumLine = withoutFinalLf.Substring(lastLf + 1);
            if (!checksumLine.StartsWith(ChecksumPrefix, StringComparison.Ordinal) ||
                checksumLine.Length != ChecksumPrefix.Length + ChecksumHexLength)
            {
                error = new SaveError(SaveErrorKind.Truncated, "チェックサム行が不完全です。");
                return false;
            }

            var hex = checksumLine.Substring(ChecksumPrefix.Length);
            if (!TryParseUpperOrLowerHexUInt(hex, out var expectedSum))
            {
                error = new SaveError(SaveErrorKind.ChecksumMismatch, "セーブデータが破損しています（チェックサム形式不正）。");
                return false;
            }

            var body = text.Substring(0, lastLf + 1);
            var actualSum = ComputeChecksum(body);
            if (actualSum != expectedSum)
            {
                error = new SaveError(SaveErrorKind.ChecksumMismatch, "セーブデータが破損しています（チェックサム不一致）。");
                return false;
            }

            // 行パースと重複キー検査
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var lines = body.Split('\n');
            for (var i = 0; i < lines.Length - 1; i++)
            {
                var line = lines[i];
                var eq = line.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0)
                {
                    error = new SaveError(SaveErrorKind.SyntaxError, $"セーブデータの {i + 1} 行目が不正です。");
                    return false;
                }

                var key = line.Substring(0, eq);
                var val = line.Substring(eq + 1);
                if (!map.TryAdd(key, val))
                {
                    error = new SaveError(SaveErrorKind.DuplicateKey, $"キー '{key}' が重複しています。");
                    return false;
                }
            }

            if (!TryReadInt(map, "version", out var version, out error))
            {
                return false;
            }

            if (version != CurrentVersion)
            {
                error = new SaveError(SaveErrorKind.VersionMismatch, $"未対応のセーブデータ版です: {version}");
                return false;
            }

            if (!TryReadString(map, "fingerprint", out var fpHex, out error))
            {
                return false;
            }

            if (fpHex.Length != 8 || !TryParseUpperOrLowerHexUInt(fpHex, out var fp) || fp != data.Fingerprint)
            {
                error = new SaveError(SaveErrorKind.FingerprintMismatch, "ゲームデータの版がセーブ時と一致しません。");
                return false;
            }

            if (!TryReadInt(map, "seed", out var matchSeed, out error) ||
                !TryReadInt(map, "gold", out var gold, out error) ||
                !TryReadInt(map, "battles", out var battles, out error) ||
                !TryReadLong(map, "ticks", out var playTicks, out error) ||
                !TryReadString(map, "map", out var mapId, out error) ||
                !TryReadInt(map, "x", out var x, out error) ||
                !TryReadInt(map, "y", out var y, out error) ||
                !TryReadInt(map, "facing", out var facingInt, out error) ||
                !TryReadInt(map, "steps_since_enc", out var stepsSinceEnc, out error) ||
                !TryReadInt(map, "total_steps", out var totalSteps, out error) ||
                !TryReadString(map, "enc_rng", out var encRngHex, out error))
            {
                return false;
            }

            if (gold < 0 || gold > data.Rules.MaxGold ||
                battles < 0 ||
                playTicks < 0 ||
                stepsSinceEnc < 0 ||
                totalSteps < 0 ||
                !DirectionOps.IsDefined((Direction)facingInt))
            {
                error = new SaveError(SaveErrorKind.OutOfRange, "基本数値が許容範囲外です。");
                return false;
            }

            if (encRngHex.Length != 8 || !TryParseUpperOrLowerHexUInt(encRngHex, out var encRngState) || encRngState == 0)
            {
                error = new SaveError(SaveErrorKind.OutOfRange, "乱数状態が不正です。");
                return false;
            }

            if (!data.TryGetMap(mapId, out var fieldMap))
            {
                error = new SaveError(SaveErrorKind.UnknownId, $"未知のマップ ID です: {mapId}");
                return false;
            }

            // パーティ復元
            var members = new PartyMember[data.Rules.PartyClassIds.Count];
            for (var i = 0; i < members.Length; i++)
            {
                var p = "m" + i.ToString(CultureInfo.InvariantCulture) + ".";
                if (!TryReadString(map, p + "class", out var classId, out error) ||
                    !TryReadInt(map, p + "level", out var level, out error) ||
                    !TryReadInt(map, p + "exp", out var exp, out error) ||
                    !TryReadInt(map, p + "hp", out var hp, out error) ||
                    !TryReadInt(map, p + "mp", out var mp, out error) ||
                    !TryReadString(map, p + "weapon", out var weaponId, out error) ||
                    !TryReadString(map, p + "armor", out var armorId, out error))
                {
                    return false;
                }

                if (!string.Equals(classId, data.Rules.PartyClassIds[i], StringComparison.Ordinal) ||
                    !data.TryGetClass(classId, out var classInfo))
                {
                    error = new SaveError(SaveErrorKind.UnknownId, $"メンバー {i} の職業 ID が不正です。");
                    return false;
                }

                var maxExp = data.ExpTable.ExpForLevel(data.ExpTable.MaxLevel);
                if (level < 1 || level > data.ExpTable.MaxLevel ||
                    exp < data.ExpTable.ExpForLevel(level) || exp > maxExp ||
                    data.ExpTable.LevelForExp(exp) != level)
                {
                    error = new SaveError(SaveErrorKind.OutOfRange, $"メンバー {i} のレベルまたは経験値が不正です。");
                    return false;
                }

                var maxHp = classInfo!.MaxHpAt(level);
                var maxMp = classInfo.MaxMpAt(level);
                if (hp < 0 || hp > maxHp || mp < 0 || mp > maxMp)
                {
                    error = new SaveError(SaveErrorKind.OutOfRange, $"メンバー {i} の HP または MP が範囲外です。");
                    return false;
                }

                if (weaponId.Length > 0)
                {
                    if (!data.TryGetEquip(weaponId, out var w))
                    {
                        error = new SaveError(SaveErrorKind.UnknownId, $"未知の武器 ID です: {weaponId}");
                        return false;
                    }

                    if (w!.Slot != EquipSlot.Weapon || !w.CanEquip(classInfo.Id))
                    {
                        error = new SaveError(SaveErrorKind.OutOfRange, $"不正な武器装備です: {weaponId}");
                        return false;
                    }
                }

                if (armorId.Length > 0)
                {
                    if (!data.TryGetEquip(armorId, out var a))
                    {
                        error = new SaveError(SaveErrorKind.UnknownId, $"未知の防具 ID です: {armorId}");
                        return false;
                    }

                    if (a!.Slot != EquipSlot.Armor || !a.CanEquip(classInfo.Id))
                    {
                        error = new SaveError(SaveErrorKind.OutOfRange, $"不正な防具装備です: {armorId}");
                        return false;
                    }
                }

                members[i] = PartyMember.Restore(data, classInfo, level, exp, hp, mp, weaponId, armorId);
            }

            var party = new Party(members);
            if (party.IsWipedOut)
            {
                error = new SaveError(SaveErrorKind.OutOfRange, "全滅状態のセーブデータは不正です。");
                return false;
            }

            // 所持袋復元
            if (!TryReadInt(map, "bag.count", out var bagCount, out error))
            {
                return false;
            }

            if (bagCount < 0 || bagCount > data.Items.Count + data.Equips.Count)
            {
                error = new SaveError(SaveErrorKind.OutOfRange, "所持品数が範囲外です。");
                return false;
            }

            var inventory = new Inventory(data);
            var seenBagIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < bagCount; i++)
            {
                if (!TryReadString(map, "bag." + i.ToString(CultureInfo.InvariantCulture), out var entryText, out error))
                {
                    return false;
                }

                var colon = entryText.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0)
                {
                    error = new SaveError(SaveErrorKind.SyntaxError, $"所持品エントリ {i} の形式が不正です。");
                    return false;
                }

                var prodId = entryText.Substring(0, colon);
                var countStr = entryText.Substring(colon + 1);
                if (!data.IsKnownProduct(prodId))
                {
                    error = new SaveError(SaveErrorKind.UnknownId, $"未知の所持品 ID です: {prodId}");
                    return false;
                }

                if (!seenBagIds.Add(prodId))
                {
                    error = new SaveError(SaveErrorKind.DuplicateKey, $"所持品 ID '{prodId}' が重複しています。");
                    return false;
                }

                if (!int.TryParse(countStr, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
                    count <= 0 ||
                    !inventory.CanAdd(party, prodId, count))
                {
                    error = new SaveError(SaveErrorKind.OutOfRange, $"所持品 '{prodId}' の個数が範囲外です。");
                    return false;
                }

                inventory.Add(prodId, count);
            }

            // フラグ復元
            if (!TryReadInt(map, "flags.count", out var flagCount, out error))
            {
                return false;
            }

            if (flagCount < 0 || flagCount > data.KnownFlags.Count)
            {
                error = new SaveError(SaveErrorKind.OutOfRange, "フラグ数が範囲外です。");
                return false;
            }

            var flags = new FlagSet(data);
            var seenFlags = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < flagCount; i++)
            {
                if (!TryReadString(map, "flags." + i.ToString(CultureInfo.InvariantCulture), out var flagId, out error))
                {
                    return false;
                }

                if (!data.TryGetFlagIndex(flagId, out _))
                {
                    error = new SaveError(SaveErrorKind.UnknownId, $"未知のフラグ ID です: {flagId}");
                    return false;
                }

                if (!seenFlags.Add(flagId))
                {
                    error = new SaveError(SaveErrorKind.DuplicateKey, $"フラグ '{flagId}' が重複しています。");
                    return false;
                }

                flags.Set(flagId, true);
            }

            if (!fieldMap!.IsWalkable(x, y, flags))
            {
                error = new SaveError(SaveErrorKind.OutOfRange, "保存された座標が通行不能タイル上です。");
                return false;
            }

            // 未定義の余分なキーが紛れ込んでいないか検査
            var expectedKeyCount = 13 + (members.Length * 7) + 1 + bagCount + 1 + flagCount;
            if (map.Count != expectedKeyCount)
            {
                error = new SaveError(SaveErrorKind.SyntaxError, "未知のキーが含まれています。");
                return false;
            }

            var explorer = Explorer.Restore(mapId, x, y, (Direction)facingInt, stepsSinceEnc, totalSteps, encRngState);
            world = World.Restore(data, matchSeed, party, inventory, flags, explorer, gold, battles, playTicks);
            error = null;
            return true;
        }

        private static void AppendLine(StringBuilder sb, string key, string value)
        {
            sb.Append(key);
            sb.Append('=');
            sb.Append(value);
            sb.Append('\n');
        }

        private static uint ComputeChecksum(string body)
        {
            var hash = StateHash.Start();
            hash.Mix(0x53415645); // "SAVE"
            hash.Mix(body.Length);
            for (var i = 0; i < body.Length; i++)
            {
                hash.Mix(body[i]);
            }

            return hash.Value;
        }

        private static bool TryParseUpperOrLowerHexUInt(string text, out uint value)
        {
            value = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                var ok = (ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'F') || (ch >= 'a' && ch <= 'f');
                if (!ok)
                {
                    return false;
                }
            }

            return uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryReadString(Dictionary<string, string> map, string key, out string value, out SaveError? error)
        {
            if (!map.TryGetValue(key, out var found))
            {
                value = string.Empty;
                error = new SaveError(SaveErrorKind.MissingKey, $"必須キー '{key}' がありません。");
                return false;
            }

            value = found;
            error = null;
            return true;
        }

        private static bool TryReadInt(Dictionary<string, string> map, string key, out int value, out SaveError? error)
        {
            value = 0;
            if (!TryReadString(map, key, out var raw, out error))
            {
                return false;
            }

            if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
            {
                error = new SaveError(SaveErrorKind.OutOfRange, $"キー '{key}' の整数値が不正です。");
                return false;
            }

            return true;
        }

        private static bool TryReadLong(Dictionary<string, string> map, string key, out long value, out SaveError? error)
        {
            value = 0;
            if (!TryReadString(map, key, out var raw, out error))
            {
                return false;
            }

            if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
            {
                error = new SaveError(SaveErrorKind.OutOfRange, $"キー '{key}' の長整数値が不正です。");
                return false;
            }

            return true;
        }
    }
}
