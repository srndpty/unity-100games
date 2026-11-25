using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class PartyAndEquipTests
    {
        [Test]
        public void HealAndMpRestoreSaturateAtMaxForHugeValidatedPower()
        {
            // 回復量は 1 以上なら検証を通るので、int.MaxValue の道具も有効なデータになる
            var raw = DefaultGameData.CreateRaw();
            var herbIndex = Array.FindIndex(raw.Items, i => i.Id == "herb");
            var etherIndex = Array.FindIndex(raw.Items, i => i.Id == "ether_dew");
            raw.Items[herbIndex].Power = int.MaxValue;
            raw.Items[etherIndex].Power = int.MaxValue;
            var data = GameData.Validate(raw);
            var party = new Party(data);
            var member = party[1];
            member.TakeDamage(member.MaxHp - 1);
            member.SpendMp(member.Mp - 1);

            Assert.That(member.HealHp(data.GetItem("herb").Power), Is.EqualTo(member.MaxHp - 1));
            Assert.That(member.Hp, Is.EqualTo(member.MaxHp));
            Assert.That(member.RestoreMp(data.GetItem("ether_dew").Power), Is.EqualTo(member.MaxMp - 1));
            Assert.That(member.Mp, Is.EqualTo(member.MaxMp));

            // 満タンでも負にならず、回復量 0
            Assert.That(member.HealHp(int.MaxValue), Is.EqualTo(0));
            Assert.That(member.Hp, Is.EqualTo(member.MaxHp));

            var enemy = new BattleState(data, data.GetGroup("f1_slimes")).Enemies[0];
            enemy.SetVitals(1, enemy.Mp);
            Assert.That(enemy.HealHp(int.MaxValue), Is.EqualTo(enemy.Info.MaxHp - 1));
            Assert.That(enemy.Hp, Is.EqualTo(enemy.Info.MaxHp));
        }

        [Test]
        public void LevelUpBoundariesMultiLevelJumpAndDeadMemberZeroExp()
        {
            var data = DefaultGameData.Create();
            var party = new Party(data);
            var priest = party[1];

            // Lv2 閾値 1 だけ手前ではレベルアップしない
            var lv2Exp = data.ExpTable.ExpForLevel(2);
            Assert.That(priest.AddExp(1, lv2Exp - 1).Count, Is.EqualTo(0));
            Assert.That(priest.Level, Is.EqualTo(1));

            // ちょうど閾値で Lv2 に上がる
            var singleGain = priest.AddExp(1, 1);
            Assert.That(singleGain.Count, Is.EqualTo(1));
            Assert.That(priest.Level, Is.EqualTo(2));

            // 複数レベル一括上昇（Lv1 -> Lv4）で習得呪文と能力増分がすべて反映される
            var mage = party[2];
            mage.TakeDamage(5);
            mage.SpendMp(4);
            var hpBefore = mage.Hp;
            var mpBefore = mage.Mp;
            var lv4Exp = data.ExpTable.ExpForLevel(4);
            var multiGains = mage.AddExp(2, lv4Exp);

            Assert.That(multiGains.Count, Is.EqualTo(3));
            Assert.That(mage.Level, Is.EqualTo(4));
            Assert.That(mage.Hp, Is.EqualTo(hpBefore + (3 * mage.Class.HpGain)));
            Assert.That(mage.Mp, Is.EqualTo(mpBefore + (3 * mage.Class.MpGain)));
            Assert.That(mage.KnowsSpell("frost_lance"), Is.True);
            Assert.That(mage.KnowsSpell("flame_wave"), Is.True);
            Assert.That(mage.KnowsSpell("glacier"), Is.False);

            // 戦闘不能メンバーは経験値を得ない
            var warrior = party[0];
            warrior.TakeDamage(warrior.MaxHp);
            Assert.That(warrior.IsAlive, Is.False);
            Assert.That(warrior.AddExp(0, 500).Count, Is.EqualTo(0));
            Assert.That(warrior.Exp, Is.EqualTo(0));
            Assert.That(warrior.Level, Is.EqualTo(1));
        }

        [Test]
        public void EquipSwapClassRestrictionAndOwnershipCap99AreEnforced()
        {
            var data = DefaultGameData.Create();
            var party = new Party(data);
            var inv = new Inventory(data);
            var warrior = party[0];
            var mage = party[2];

            var baseAtk = warrior.Atk;
            Assert.That(warrior.WeaponId, Is.EqualTo("bronze_sword"));

            inv.Add("iron_blade", 1);
            Assert.That(inv.EquipFromBag(warrior, "iron_blade"), Is.True);
            Assert.That(warrior.WeaponId, Is.EqualTo("iron_blade"));
            Assert.That(inv.Count("iron_blade"), Is.EqualTo(0));
            Assert.That(inv.Count("bronze_sword"), Is.EqualTo(1));
            Assert.That(warrior.Atk, Is.GreaterThan(baseAtk));

            // 魔法使いは剣を装備できない
            inv.Add("iron_blade", 1);
            Assert.That(inv.EquipFromBag(mage, "iron_blade"), Is.False);
            Assert.Throws<InvalidOperationException>(() => mage.Equip(data.GetEquip("iron_blade")));

            // 所持上限 99（袋 + 装備中 <= 99）
            Assert.That(inv.CanAdd(party, "bronze_sword", 98), Is.True);
            Assert.That(inv.CanAdd(party, "bronze_sword", 99), Is.False);

            // 旧装備が袋に 99 個ある場合は外せず交換も拒否される
            var invFull = new Inventory(data);
            invFull.Add("bronze_sword", 99);
            invFull.Add("ember_greatsword", 1);
            var partyFresh = new Party(data);
            Assert.That(invFull.UnequipToBag(partyFresh[0], EquipSlot.Weapon), Is.False);
            Assert.That(invFull.EquipFromBag(partyFresh[0], "ember_greatsword"), Is.False);
        }
    }
}
