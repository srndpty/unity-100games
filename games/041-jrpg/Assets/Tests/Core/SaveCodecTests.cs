using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class SaveCodecTests
    {
        [Test]
        public void SaveAllowedOnlyAtChurchAndRoundTripPreservesExactWorldAndFutureHash()
        {
            var data = DefaultGameData.Create();
            var world = World.CreateNew(data, matchSeed: 12345);

            // 初期位置（町の中央）ではセーブ不可
            Assert.That(SaveCodec.TryEncodeAtChurch(world, out _, out var notAtChurchErr), Is.False);
            Assert.That(notAtChurchErr!.Kind, Is.EqualTo(SaveErrorKind.NotAtChurch));

            // 成長・装備変更・フラグ取得・乱数進行を経た状態を作る
            world.Party[0].AddExp(0, 150);
            world.Party[1].AddExp(1, 150);
            world.Party[2].AddExp(2, 150);
            world.Party[0].TakeDamage(7);
            world.Party[2].SpendMp(5);
            world.Inventory.Add("iron_blade", 1);
            world.Inventory.EquipFromBag(world.Party[0], "iron_blade");
            world.Inventory.Add("hi_herb", 2);
            world.Flags.Set("chest_f1_gold", true);
            world.Flags.Set("flag_iron_key", true);
            world.AddGold(135);
            world.AdvancePlayTicks(4321);
            world.Explorer.EncounterRng.Next(100);
            world.Explorer.EncounterRng.Next(100);

            // 教会の神父（14, 4）の正面（14, 5）で上を向くとセーブ可能
            world.Explorer.WarpTo("town", 14, 5, Direction.Up);
            Assert.That(world.CanSaveAtChurch(), Is.True);
            Assert.That(SaveCodec.TryEncodeAtChurch(world, out var saveText, out var encodeErr), Is.True, encodeErr?.ToString());

            var hashBefore = world.Hash();
            Assert.That(SaveCodec.TryDecode(data, saveText, out var restored, out var decodeErr), Is.True, decodeErr?.ToString());
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Hash(), Is.EqualTo(hashBefore));

            // セーブ復元後もダンジョン探索・遭遇乱数の未来軌道が完全に一致する
            world.Explorer.WarpTo("dungeon_f1", 4, 5, Direction.Right);
            restored.Explorer.WarpTo("dungeon_f1", 4, 5, Direction.Right);
            for (var t = 0; t < 200; t++)
            {
                var dir = ((t / 8) % 2 == 0) ? Direction.Right : Direction.Left;
                world.Explorer.Tick(data, world.Flags, dir, out _, out _);
                restored.Explorer.Tick(data, restored.Flags, dir, out _, out _);
            }

            Assert.That(restored.Hash(), Is.EqualTo(world.Hash()));
        }

        [Test]
        public void RejectsEveryTruncationPositionAndSingleCharTamper()
        {
            var data = DefaultGameData.Create();
            var world = World.CreateNew(data, matchSeed: 77);
            world.Flags.Set("flag_iron_key", true);
            var valid = SaveCodec.EncodeUnchecked(world);

            // 末尾を 1 文字ずつ削った全位置（0 .. valid.Length - 1）で拒否される
            for (var len = 0; len < valid.Length; len++)
            {
                var truncated = valid.Substring(0, len);
                var ok = SaveCodec.TryDecode(data, truncated, out var decoded, out var err);
                Assert.That(ok, Is.False, $"Expected truncation at length={len} to fail.");
                Assert.That(decoded, Is.Null);
                Assert.That(
                    err!.Kind == SaveErrorKind.Empty || err.Kind == SaveErrorKind.Truncated,
                    Is.True,
                    $"Unexpected kind {err.Kind} at length={len}");
            }

            // 全文字位置の 1 文字改ざん（改行以外）で拒否される
            for (var pos = 0; pos < valid.Length - 1; pos++)
            {
                if (valid[pos] == '\n')
                {
                    continue;
                }

                var chars = valid.ToCharArray();
                chars[pos] = chars[pos] == 'A' ? 'B' : 'A';
                var tampered = new string(chars);
                Assert.That(SaveCodec.TryDecode(data, tampered, out _, out var tamperErr), Is.False, $"Expected tamper at pos={pos} to fail.");
                Assert.That(tamperErr, Is.Not.Null);
            }
        }

        [Test]
        public void RejectsVersionFingerprintUnknownIdsDuplicateKeysAndOutOfRangeValues()
        {
            var data = DefaultGameData.Create();
            var world = World.CreateNew(data, matchSeed: 9);
            var baseText = SaveCodec.EncodeUnchecked(world);
            var bodyEnd = baseText.LastIndexOf("\nchecksum=", StringComparison.Ordinal) + 1;
            var body = baseText.Substring(0, bodyEnd);

            // 版不一致
            AssertSemanticError(data, body.Replace("version=1\n", "version=2\n", StringComparison.Ordinal), SaveErrorKind.VersionMismatch);

            // データ Fingerprint 不一致
            AssertSemanticError(
                data,
                body.Replace($"fingerprint={data.Fingerprint:X8}\n", "fingerprint=00000000\n", StringComparison.Ordinal),
                SaveErrorKind.FingerprintMismatch);

            // 重複キー
            AssertSemanticError(data, body + "gold=10\n", SaveErrorKind.DuplicateKey);

            // 未知 ID（マップ・装備・所持品・フラグ）
            AssertSemanticError(data, body.Replace("map=town\n", "map=atlantis\n", StringComparison.Ordinal), SaveErrorKind.UnknownId);
            AssertSemanticError(
                data,
                body.Replace("m0.weapon=bronze_sword\n", "m0.weapon=laser_gun\n", StringComparison.Ordinal),
                SaveErrorKind.UnknownId);
            AssertSemanticError(
                data,
                body.Replace("bag.0=ether_dew:1\n", "bag.0=mystery_orb:1\n", StringComparison.Ordinal),
                SaveErrorKind.UnknownId);
            AssertSemanticError(
                data,
                body.Replace("flags.count=0\n", "flags.count=1\nflags.0=unknown_flag\n", StringComparison.Ordinal),
                SaveErrorKind.UnknownId);

            // 範囲外（所持金負数・HP 上限超え・所持合計 99 超え・壁タイル座標・乱数状態 0）
            AssertSemanticError(data, body.Replace("gold=60\n", "gold=-1\n", StringComparison.Ordinal), SaveErrorKind.OutOfRange);
            AssertSemanticError(data, body.Replace("m0.hp=44\n", "m0.hp=999\n", StringComparison.Ordinal), SaveErrorKind.OutOfRange);
            AssertSemanticError(
                data,
                body.Replace("bag.count=2\n", "bag.count=3\n", StringComparison.Ordinal) + "bag.2=bronze_sword:99\n",
                SaveErrorKind.OutOfRange);
            AssertSemanticError(data, body.Replace("x=8\ny=7\n", "x=0\ny=0\n", StringComparison.Ordinal), SaveErrorKind.OutOfRange);
            AssertSemanticError(
                data,
                body.Replace($"enc_rng={world.Explorer.EncounterRng.State:X8}\n", "enc_rng=00000000\n", StringComparison.Ordinal),
                SaveErrorKind.OutOfRange);
        }

        private static void AssertSemanticError(GameData data, string unsignedBody, SaveErrorKind expected)
        {
            var signed = SaveCodec.SignBody(unsignedBody);
            var ok = SaveCodec.TryDecode(data, signed, out var decoded, out var err);
            Assert.That(ok, Is.False);
            Assert.That(decoded, Is.Null);
            Assert.That(err!.Kind, Is.EqualTo(expected), $"Actual error: {err}");
        }
    }
}
