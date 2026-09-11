using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Orbis.M1;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.M15.Tests
{
    public sealed class GachaEngineTests
    {
        private readonly List<ScriptableObject> assets = new List<ScriptableObject>();
        private GachaRules rules;
        private GachaBanner standard, limited;
        private CharacterDefinition[] three, four, five;

        [SetUp]
        public void SetUp()
        {
            rules = Asset<GachaRules>();
            three = new[] { Character("starter-a", CharacterRarity.Three), Character("starter-b", CharacterRarity.Three), Character("starter-c", CharacterRarity.Three) };
            four = new[] { Character("four-a", CharacterRarity.Four), Character("four-b", CharacterRarity.Four) };
            five = new[] { Character("five-a", CharacterRarity.Five), Character("five-b", CharacterRarity.Five), Character("five-c", CharacterRarity.Five) };
            standard = Banner("standard", BannerType.Standard);
            limited = Banner("limited-a", BannerType.Limited);
        }
        [TearDown]
        public void TearDown()
        {
            for (int i = assets.Count - 1; i >= 0; i--) Object.DestroyImmediate(assets[i]);
            assets.Clear();
        }
        private T Asset<T>() where T : ScriptableObject
        { T value = ScriptableObject.CreateInstance<T>(); assets.Add(value); return value; }
        private CharacterDefinition Character(string id, CharacterRarity rarity)
        {
            var result = Asset<CharacterDefinition>();
            result.Id = id; result.DisplayName = id; result.Element = ElementType.Fire;
            result.Rarity = rarity; result.Weapon = WeaponType.Longsword; result.Role = "Test";
            result.IsStarter = rarity == CharacterRarity.Three;
            return result;
        }
        private GachaBanner Banner(string id, BannerType type)
        {
            var result = Asset<GachaBanner>(); result.Id = id; result.DisplayName = id; result.Type = type;
            result.Rules = rules; result.ThreeStars = three; result.FourStars = four; result.FiveStars = five;
            result.Featured = type == BannerType.Limited ? five[0] : null;
            return result;
        }
        private static Func<double> Rolls(params double[] values)
        {
            int index = 0;
            return () => index < values.Length ? values[index++] : throw new InvalidOperationException("Unexpected RNG consumption.");
        }
        private static string Snapshot(EconomySaveData save) => JsonUtility.ToJson(save);

        [Test]
        public void BaseOddsUseAuthoredProbabilities()
        {
            GachaOdds odds = GachaEngine.NextOdds(standard, null);
            Assert.That(odds.Three, Is.EqualTo(.943).Within(1e-12));
            Assert.That(odds.Four, Is.EqualTo(.051).Within(1e-12));
            Assert.That(odds.Five, Is.EqualTo(.006).Within(1e-12));
        }
        [TestCase(72, .006)]
        [TestCase(73, .066)]
        [TestCase(74, .126)]
        [TestCase(88, .966)]
        [TestCase(89, 1d)]
        public void SoftPityStartsOnPull74AndHardPityOn90(int misses, double expected)
        {
            GachaOdds odds = GachaEngine.NextOdds(standard, new BannerPityData { bannerId = standard.Id, fiveStarMisses = misses });
            Assert.That(odds.Five, Is.EqualTo(expected).Within(1e-12));
            Assert.That(odds.Three + odds.Four + odds.Five, Is.EqualTo(1d).Within(1e-12));
            Assert.That(odds.Three, Is.GreaterThanOrEqualTo(0d));
            Assert.That(odds.Four, Is.GreaterThanOrEqualTo(0d));
        }
        [Test]
        public void FiveStarHardPityWinsOverSmallPityAndResetsBoth()
        {
            var save = new EconomySaveData(); var pity = save.GetPity(standard.Id);
            pity.fiveStarMisses = 89; pity.fourStarMisses = 9;
            PullResult result = GachaEngine.Draw(standard, save, Rolls(.999999, .5));
            Assert.That(result.Rarity, Is.EqualTo(CharacterRarity.Five));
            Assert.That(pity.fiveStarMisses, Is.Zero); Assert.That(pity.fourStarMisses, Is.Zero);
        }
        [TestCase(.99, CharacterRarity.Four)]
        [TestCase(0d, CharacterRarity.Five)]
        public void TenthSmallPityAllowsFourOrFive(double roll, CharacterRarity expected)
        {
            var save = new EconomySaveData(); save.GetPity(standard.Id).fourStarMisses = 9;
            PullResult result = GachaEngine.Draw(standard, save, Rolls(roll, 0d));
            Assert.That(result.Rarity, Is.EqualTo(expected));
            Assert.That(save.GetPity(standard.Id).fourStarMisses, Is.Zero);
            Assert.That(save.GetPity(standard.Id).fiveStarMisses, Is.EqualTo(expected == CharacterRarity.Five ? 0 : 1));
        }
        [Test]
        public void FourStarDoesNotResetFiveStarCounter()
        {
            var save = new EconomySaveData(); var pity = save.GetPity(standard.Id);
            pity.fiveStarMisses = 70; pity.fourStarMisses = 9;
            Assert.That(GachaEngine.Draw(standard, save, Rolls(.5, 0d)).Rarity, Is.EqualTo(CharacterRarity.Four));
            Assert.That(pity.fiveStarMisses, Is.EqualTo(71)); Assert.That(pity.fourStarMisses, Is.Zero);
        }
        [Test]
        public void LosingFeaturedExcludesItAndGuaranteesTheNextFiveStar()
        {
            var save = new EconomySaveData();
            PullResult lost = GachaEngine.Draw(limited, save, Rolls(0d, .5, 0d));
            Assert.That(lost.Character, Is.SameAs(five[1]));
            var pity = save.GetPity(limited.Id); Assert.That(pity.guaranteedFeatured, Is.True);
            pity.fiveStarMisses = 89;
            PullResult guaranteed = GachaEngine.Draw(limited, save, Rolls(.999999));
            Assert.That(guaranteed.Character, Is.SameAs(limited.Featured));
            Assert.That(pity.guaranteedFeatured, Is.False);
        }
        [TestCase(.499999, true)]
        [TestCase(.5, false)]
        public void FeaturedCoinUsesStrictHalfOpenBoundary(double coin, bool featured)
        {
            var save = new EconomySaveData();
            PullResult result = GachaEngine.Draw(limited, save, Rolls(0d, coin, .99));
            Assert.That(result.Character == limited.Featured, Is.EqualTo(featured));
            Assert.That(save.GetPity(limited.Id).guaranteedFeatured, Is.EqualTo(!featured));
        }
        [TestCase(.5)]
        [TestCase(.02)]
        public void LowerRarityKeepsTheFeaturedGuarantee(double rarityRoll)
        {
            var save = new EconomySaveData(); save.GetPity(limited.Id).guaranteedFeatured = true;
            GachaEngine.Draw(limited, save, Rolls(rarityRoll, 0d));
            Assert.That(save.GetPity(limited.Id).guaranteedFeatured, Is.True);
        }
        [TestCase(0d, 0)]
        [TestCase(.34, 1)]
        [TestCase(.999999, 2)]
        public void StandardFiveStarPoolUsesUniformIndexIntervals(double selection, int expected)
        {
            PullResult result = GachaEngine.Draw(standard, new EconomySaveData(), Rolls(0d, selection));
            Assert.That(result.Character, Is.SameAs(five[expected]));
        }
        [Test]
        public void BannerIdsKeepCountersAndGuaranteesIndependent()
        {
            var save = new EconomySaveData(); var old = save.GetPity(limited.Id);
            old.fiveStarMisses = 76; old.fourStarMisses = 7; old.guaranteedFeatured = true;
            GachaEngine.Draw(standard, save, Rolls(.9, .1));
            GachaBanner nextLimited = Banner("limited-b", BannerType.Limited);
            GachaEngine.Draw(nextLimited, save, Rolls(.9, .1));
            Assert.That(old.fiveStarMisses, Is.EqualTo(76)); Assert.That(old.fourStarMisses, Is.EqualTo(7)); Assert.That(old.guaranteedFeatured, Is.True);
            Assert.That(save.GetPity(standard.Id).fiveStarMisses, Is.EqualTo(1));
            Assert.That(save.GetPity(nextLimited.Id).fiveStarMisses, Is.EqualTo(1));
            Assert.That(save.GetPity(nextLimited.Id).guaranteedFeatured, Is.False);
        }
        [Test]
        public void DuplicatesOnlyIncreaseCopiesWithoutDebitingCurrencyOrRevision()
        {
            var save = new EconomySaveData { revision = 17, startersGranted = true };
            save.balances[(int)CurrencyType.StandardPledge] = 20;
            PullResult first = GachaEngine.Draw(standard, save, Rolls(.9, 0d));
            PullResult second = GachaEngine.Draw(standard, save, Rolls(.9, 0d));
            Assert.That(first.IsNew, Is.True); Assert.That(second.IsNew, Is.False);
            Assert.That(second.OwnedCopies, Is.EqualTo(2)); Assert.That(save.owned.Length, Is.EqualTo(1));
            Assert.That(save.GetBalance(CurrencyType.StandardPledge), Is.EqualTo(20));
            Assert.That(save.revision, Is.EqualTo(17)); Assert.That(save.startersGranted, Is.True);
        }
        [TestCase(-.1)]
        [TestCase(1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidFirstRandomValueLeavesTheDraftUntouched(double invalid)
        {
            var save = new EconomySaveData(); string before = Snapshot(save);
            Assert.Throws<InvalidOperationException>(() => GachaEngine.Draw(standard, save, Rolls(invalid)));
            Assert.That(Snapshot(save), Is.EqualTo(before));
        }
        [Test]
        public void InvalidPoolRandomValueDoesNotCommitTheFeaturedLossOrPity()
        {
            var save = new EconomySaveData(); var pity = save.GetPity(limited.Id);
            pity.fiveStarMisses = 33; pity.fourStarMisses = 8;
            string before = Snapshot(save);
            Assert.Throws<InvalidOperationException>(() => GachaEngine.Draw(limited, save, Rolls(0d, .99, double.NaN)));
            Assert.That(Snapshot(save), Is.EqualTo(before));
        }
        [Test]
        public void AuthoredThresholdsAndFeaturedChanceReplaceAllDefaults()
        {
            rules.ThreeStarProbability = .7; rules.FourStarProbability = .2; rules.FiveStarProbability = .1;
            rules.SoftPityStart = 3; rules.HardPity = 4; rules.FourStarHardPity = 2;
            rules.SoftPityStep = .25; rules.FeaturedChance = .8;
            var save = new EconomySaveData(); var pity = save.GetPity(limited.Id); pity.fiveStarMisses = 2;
            GachaOdds odds = GachaEngine.NextOdds(limited, pity);
            Assert.That(odds.Five, Is.EqualTo(.35).Within(1e-12));
            Assert.That(GachaEngine.Draw(limited, save, Rolls(.2, .7)).Character, Is.SameAs(limited.Featured));
            pity.fiveStarMisses = 3; pity.fourStarMisses = 1;
            Assert.That(GachaEngine.NextOdds(limited, pity).Five, Is.EqualTo(1d));
        }
        [Test]
        public void TenSequentialPullsApplyTheSmallPityToTheTenthResult()
        {
            var save = new EconomySaveData(); var results = new List<PullResult>();
            for (int i = 0; i < 10; i++) results.Add(GachaEngine.Draw(standard, save, () => .99));
            Assert.That(results.Take(9).All(x => x.Rarity == CharacterRarity.Three), Is.True);
            Assert.That(results[9].Rarity, Is.EqualTo(CharacterRarity.Four));
            Assert.That(save.CopiesOf(three[2].Id), Is.EqualTo(9)); Assert.That(save.CopiesOf(four[1].Id), Is.EqualTo(1));
            Assert.That(save.GetPity(standard.Id).fiveStarMisses, Is.EqualTo(10)); Assert.That(save.GetPity(standard.Id).fourStarMisses, Is.Zero);
        }
        [Test]
        public void TenSerialPullsMatchSerializedResumeWithTheSameRandomStream()
        {
            var a = new EconomySaveData(); a.GetPity(limited.Id).fiveStarMisses = 72;
            EconomySaveData b = a.Clone(); var randomA = new System.Random(1974); var randomB = new System.Random(1974);
            var resultsA = new List<string>(); var resultsB = new List<string>();
            for (int i = 0; i < 10; i++) resultsA.Add(GachaEngine.Draw(limited, a, randomA.NextDouble).Character.Id);
            for (int i = 0; i < 10; i++)
            {
                if (i == 5) b = JsonUtility.FromJson<EconomySaveData>(Snapshot(b));
                resultsB.Add(GachaEngine.Draw(limited, b, randomB.NextDouble).Character.Id);
            }
            CollectionAssert.AreEqual(resultsA, resultsB); Assert.That(Snapshot(b), Is.EqualTo(Snapshot(a)));
        }
        [Test]
        public void CloneDeeplySeparatesWalletOwnedAndPityAndPreservesUnknownIds()
        {
            var save = new EconomySaveData { revision = 9, startersGranted = true };
            save.balances[0] = 45; save.owned = new[] { new OwnedCharacterData { id = "removed-character", copies = 2 } };
            save.GetPity("retired-banner").guaranteedFeatured = true;
            EconomySaveData clone = save.Clone();
            clone.balances[0]++; clone.owned[0].copies++; clone.pities[0].guaranteedFeatured = false; clone.GetPity("new-banner");
            Assert.That(save.Validate(out _), Is.True); Assert.That(save.GetBalance(CurrencyType.Lumen), Is.EqualTo(45));
            Assert.That(save.CopiesOf("removed-character"), Is.EqualTo(2)); Assert.That(save.pities.Length, Is.EqualTo(1));
            Assert.That(save.GetPity("retired-banner").guaranteedFeatured, Is.True); Assert.That(clone.revision, Is.EqualTo(9)); Assert.That(clone.startersGranted, Is.True);
        }
        [TestCase("negative-balance")]
        [TestCase("wrong-wallet-size")]
        [TestCase("duplicate-owned")]
        [TestCase("zero-copies")]
        [TestCase("negative-pity")]
        [TestCase("pity-overflow")]
        [TestCase("duplicate-pity")]
        [TestCase("negative-revision")]
        public void InvalidSaveStructuresAreRejected(string condition)
        {
            var save = new EconomySaveData();
            switch (condition)
            {
                case "negative-balance": save.balances[1] = -1; break;
                case "wrong-wallet-size": save.balances = new int[5]; break;
                case "duplicate-owned": save.owned = new[] { new OwnedCharacterData { id = "x", copies = 1 }, new OwnedCharacterData { id = "x", copies = 2 } }; break;
                case "zero-copies": save.owned = new[] { new OwnedCharacterData { id = "x", copies = 0 } }; break;
                case "negative-pity": save.GetPity("x").fiveStarMisses = -1; break;
                case "pity-overflow": save.GetPity("x").fourStarMisses = int.MaxValue; break;
                case "duplicate-pity": save.pities = new[] { new BannerPityData { bannerId = "x" }, new BannerPityData { bannerId = "x" } }; break;
                case "negative-revision": save.revision = -1; break;
            }
            Assert.That(save.Validate(out string error), Is.False); Assert.That(error, Is.Not.Empty);
            int calls = 0;
            Assert.Throws<InvalidOperationException>(() => GachaEngine.Draw(standard, save, () => { calls++; return 0d; }));
            Assert.That(calls, Is.Zero);
        }
        [TestCase("nan")]
        [TestCase("sum")]
        [TestCase("negative-step")]
        [TestCase("threshold-order")]
        [TestCase("empty-pool")]
        [TestCase("wrong-rarity")]
        [TestCase("duplicate-id")]
        [TestCase("featured-only")]
        public void BadAuthoredDataFailsBeforeRandomSampling(string condition)
        {
            switch (condition)
            {
                case "nan": rules.FiveStarProbability = double.NaN; break;
                case "sum": rules.ThreeStarProbability = .9; break;
                case "negative-step": rules.SoftPityStep = -.1; break;
                case "threshold-order": rules.SoftPityStart = 91; break;
                case "empty-pool": limited.ThreeStars = Array.Empty<CharacterDefinition>(); break;
                case "wrong-rarity": limited.FourStars = new[] { three[0] }; break;
                case "duplicate-id": limited.ThreeStars = new[] { three[0], three[0] }; break;
                case "featured-only": limited.FiveStars = new[] { limited.Featured }; break;
            }
            int calls = 0; var save = new EconomySaveData(); string before = Snapshot(save);
            Assert.Throws<InvalidOperationException>(() => GachaEngine.Draw(limited, save, () => { calls++; return .2; }));
            Assert.That(calls, Is.Zero); Assert.That(Snapshot(save), Is.EqualTo(before));
        }
        [Test]
        public void MaximumStoredRevisionAndCopiesAreValidButNextCopyOverflowIsAtomic()
        {
            var save = new EconomySaveData { revision = long.MaxValue };
            save.owned = new[] { new OwnedCharacterData { id = three[0].Id, copies = int.MaxValue } };
            Assert.That(save.Validate(out _), Is.True);
            string before = Snapshot(save);
            Assert.Throws<OverflowException>(() => GachaEngine.Draw(standard, save, Rolls(.9, 0d)));
            Assert.That(Snapshot(save), Is.EqualTo(before));
            Assert.That(save.pities.Length, Is.Zero, "Overflow must not create a partially committed pity entry.");
        }
        [Test]
        public void LastRepresentableCopyCountRemainsAValidSave()
        {
            var save = new EconomySaveData();
            save.owned = new[] { new OwnedCharacterData { id = three[0].Id, copies = int.MaxValue - 1 } };
            PullResult result = GachaEngine.Draw(standard, save, Rolls(.9, 0d));
            Assert.That(result.OwnedCopies, Is.EqualTo(int.MaxValue));
            Assert.That(save.Validate(out _), Is.True);
        }
        [Test]
        public void CatalogRejectsUnregisteredAndConflictingCharacterReferences()
        {
            var catalog = Asset<GachaCatalog>(); catalog.Rules = rules;
            catalog.Characters = three.Concat(four).Concat(five).ToArray(); catalog.Banners = new[] { standard, limited };
            Assert.DoesNotThrow(catalog.Validate);
            CharacterDefinition replacement = Character(five[0].Id, CharacterRarity.Five);
            limited.Featured = replacement; limited.FiveStars = new[] { five[1], five[2] };
            Assert.Throws<InvalidOperationException>(catalog.Validate);
        }
        [Test]
        public void CatalogRejectsDifferentRuleAssetsSoDisplayedExchangeCostsCannotDiverge()
        {
            var catalog = Asset<GachaCatalog>(); catalog.Rules = rules;
            catalog.Characters = three.Concat(four).Concat(five).ToArray(); catalog.Banners = new[] { standard, limited };
            Assert.DoesNotThrow(catalog.Validate);
            limited.Rules = Asset<GachaRules>(); limited.Rules.ExchangeCost = 80;
            Assert.DoesNotThrow(limited.Validate);
            Assert.Throws<InvalidOperationException>(catalog.Validate);
        }
        [Test]
        public void NextOddsRejectsAnotherBannersPityWithoutMutatingIt()
        {
            var pity = new BannerPityData { bannerId = limited.Id, fiveStarMisses = 73 };
            Assert.Throws<InvalidOperationException>(() => GachaEngine.NextOdds(standard, pity));
            Assert.That(pity.fiveStarMisses, Is.EqualTo(73));
        }
    }
}


