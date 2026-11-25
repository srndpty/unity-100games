using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class DataValidationTests
    {
        [Test]
        public void DefaultGameDataValidatesAndProducesSensitivityFingerprint()
        {
            var data = DefaultGameData.Create();
            Assert.That(data.Fingerprint, Is.Not.EqualTo(0u));
            Assert.That(data.Classes.Count, Is.EqualTo(3));
            Assert.That(data.Maps.Count, Is.EqualTo(5));

            var modified = DefaultGameData.CreateRaw();
            modified.Rules.InnCost += 1;
            var data2 = GameData.Validate(modified);
            Assert.That(data2.Fingerprint, Is.Not.EqualTo(data.Fingerprint));
        }

        [Test]
        public void RejectsNullAndDuplicateIdsAndInvalidIdFormat()
        {
            Assert.That(GameData.TryValidate(null!, out _, out var nullErr), Is.False);
            Assert.That(nullErr!.Kind, Is.EqualTo(DataErrorKind.NullInput));

            var dupSpell = DefaultGameData.CreateRaw();
            dupSpell.Spells[1].Id = dupSpell.Spells[0].Id;
            AssertError(dupSpell, DataErrorKind.DuplicateId, "Spells[1]");

            var itemEquipCollision = DefaultGameData.CreateRaw();
            itemEquipCollision.Equips[0].Id = itemEquipCollision.Items[0].Id;
            AssertError(itemEquipCollision, DataErrorKind.DuplicateId, "Equips[0]");

            var badId = DefaultGameData.CreateRaw();
            badId.Items[0].Id = "Bad Id!";
            AssertError(badId, DataErrorKind.InvalidId, "Items[0]");
        }

        [Test]
        public void RejectsUnresolvedReferencesAcrossTables()
        {
            var badWeapon = DefaultGameData.CreateRaw();
            badWeapon.Classes[0].StartWeaponId = "missing_sword";
            AssertError(badWeapon, DataErrorKind.UnresolvedReference, "Classes[0]");

            var badSpell = DefaultGameData.CreateRaw();
            badSpell.Classes[1].Spells[0].SpellId = "missing_spell";
            AssertError(badSpell, DataErrorKind.UnresolvedReference, "Classes[1]");

            var badEnemyGroup = DefaultGameData.CreateRaw();
            badEnemyGroup.EnemyGroups[0].EnemyIds[0] = "missing_enemy";
            AssertError(badEnemyGroup, DataErrorKind.UnresolvedReference, "EnemyGroups[0]");

            var badShop = DefaultGameData.CreateRaw();
            badShop.Shops[0].ProductIds[0] = "missing_item";
            AssertError(badShop, DataErrorKind.UnresolvedReference, "Shops[0]");

            var badPortal = DefaultGameData.CreateRaw();
            badPortal.Maps[0].Portals[0].TargetMapId = "missing_map";
            AssertError(badPortal, DataErrorKind.UnresolvedReference, "Maps[0].Portals[0]");

            var unobtainableDoorKey = DefaultGameData.CreateRaw();
            unobtainableDoorKey.Maps[3].Doors[0].RequiredKeyFlag = "flag_never_granted";
            AssertError(unobtainableDoorKey, DataErrorKind.UnresolvedReference, "Maps[3].Doors[0]");
        }

        [Test]
        public void RejectsNonMonotonicExpAndOutOfRangeValuesAndNonFiniteFloats()
        {
            var flatExp = DefaultGameData.CreateRaw();
            flatExp.ExpTable.RequiredExp[3] = flatExp.ExpTable.RequiredExp[2];
            AssertError(flatExp, DataErrorKind.NonMonotonicExp, "ExpTable.RequiredExp[3]");

            var decExp = DefaultGameData.CreateRaw();
            decExp.ExpTable.RequiredExp[4] = flatExp.ExpTable.RequiredExp[2] - 1;
            AssertError(decExp, DataErrorKind.NonMonotonicExp, "ExpTable.RequiredExp[4]");

            var zeroEnemyHp = DefaultGameData.CreateRaw();
            zeroEnemyHp.Enemies[0].MaxHp = 0;
            AssertError(zeroEnemyHp, DataErrorKind.OutOfRange, "Enemies[0]");

            var negMpCost = DefaultGameData.CreateRaw();
            negMpCost.Spells[0].MpCost = -1;
            AssertError(negMpCost, DataErrorKind.OutOfRange, "Spells[0]");

            var nanRate = DefaultGameData.CreateRaw();
            nanRate.Rules.EncounterRateMultiplier = float.NaN;
            AssertError(nanRate, DataErrorKind.NonFiniteValue, "Rules.EncounterRateMultiplier");

            var infRate = DefaultGameData.CreateRaw();
            infRate.Rules.EncounterRateMultiplier = float.PositiveInfinity;
            AssertError(infRate, DataErrorKind.NonFiniteValue, "Rules.EncounterRateMultiplier");
        }

        [Test]
        public void RejectsEquipWithNoAllowedClassEmptyChestAndInvalidPortalTargetTile()
        {
            var orphanEquip = DefaultGameData.CreateRaw();
            orphanEquip.Equips[2].AllowedClassIds = Array.Empty<string>();
            AssertError(orphanEquip, DataErrorKind.NoAllowedClass, "Equips[2]");

            var emptyChest = DefaultGameData.CreateRaw();
            emptyChest.Maps[1].Chests[0].RewardGold = 0;
            emptyChest.Maps[1].Chests[0].RewardItemId = string.Empty;
            emptyChest.Maps[1].Chests[0].RewardEquipId = string.Empty;
            emptyChest.Maps[1].Chests[0].RewardFlag = string.Empty;
            AssertError(emptyChest, DataErrorKind.EmptyChest, "Maps[1].Chests[0]");

            var portalIntoWall = DefaultGameData.CreateRaw();
            portalIntoWall.Maps[0].Portals[0].TargetX = 0;
            portalIntoWall.Maps[0].Portals[0].TargetY = 0;
            AssertError(portalIntoWall, DataErrorKind.InvalidMap, "Maps[0].Portals[0]");
        }

        private static void AssertError(GameDataDef raw, DataErrorKind expectedKind, string expectedLocationPrefix)
        {
            var ok = GameData.TryValidate(raw, out var data, out var error);
            Assert.That(ok, Is.False);
            Assert.That(data, Is.Null);
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.Kind, Is.EqualTo(expectedKind));
            Assert.That(error.Location.StartsWith(expectedLocationPrefix, StringComparison.Ordinal), Is.True, $"Actual location: {error.Location}");
            var ex = Assert.Throws<GameDataException>(() => GameData.Validate(raw));
            Assert.That(ex!.Error.Kind, Is.EqualTo(expectedKind));
        }
    }
}
