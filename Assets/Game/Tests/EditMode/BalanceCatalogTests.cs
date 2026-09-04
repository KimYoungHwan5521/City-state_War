using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using LittleCiv.Data;
using NUnit.Framework;
using UnityEditor;

namespace LittleCiv.Tests
{
    public sealed class BalanceCatalogTests
    {
        private const string CatalogPath = "Assets/Game/Balance/GameBalanceCatalog.asset";

        [Test]
        public void Catalog_ContainsExpectedDefinitionCountsAndUniqueIds()
        {
            var catalog = LoadCatalog();

            Assert.That(catalog.Units, Has.Length.EqualTo(6));
            Assert.That(catalog.Districts, Has.Length.EqualTo(6));
            Assert.That(catalog.Research, Has.Length.EqualTo(19));
            AssertUnique(catalog.Units.Select(item => item.Id), "unit");
            AssertUnique(catalog.Districts.Select(item => item.Id), "district");
            AssertUnique(catalog.Research.Select(item => item.Id), "research");
        }

        [Test]
        public void Catalog_ResearchPrerequisitesReferenceExistingResearch()
        {
            var catalog = LoadCatalog();
            var researchIds = new HashSet<string>(catalog.Research.Select(item => item.Id));

            foreach (var research in catalog.Research)
            {
                foreach (var prerequisiteId in research.PrerequisiteIds)
                {
                    Assert.That(
                        researchIds.Contains(prerequisiteId),
                        Is.True,
                        $"Research '{research.Id}' has unknown prerequisite '{prerequisiteId}'.");
                }
            }
        }

        [Test]
        public void Catalog_UnlockEffectsReferenceExistingUnitsOrDistricts()
        {
            var catalog = LoadCatalog();
            var unitIds = new HashSet<string>(catalog.Units.Select(item => item.Id));
            var districtIds = new HashSet<string>(catalog.Districts.Select(item => item.Id));

            foreach (var research in catalog.Research)
            {
                foreach (var effect in research.Effects)
                {
                    if (effect.Type == ResearchEffectType.UnlockUnit)
                    {
                        Assert.That(unitIds.Contains(effect.TargetId), Is.True);
                    }
                    else if (effect.Type == ResearchEffectType.UnlockDistrict)
                    {
                        Assert.That(districtIds.Contains(effect.TargetId), Is.True);
                    }
                }
            }
        }

        [Test]
        public void Catalog_UnitCombatStatsMatchPrototypeRules()
        {
            var catalog = LoadCatalog();

            AssertUnit(catalog, UnitType.Militia, 0, 9, 16, 2, 2, 6);
            AssertUnit(catalog, UnitType.IronInfantry, 1, 12, 22, 2, 2, 6);
            AssertUnit(catalog, UnitType.GunpowderInfantry, 2, 15, 27, 3, 2, 6);
            AssertUnit(catalog, UnitType.MechanizedInfantry, 3, 18, 32, 4, 3, 10);
            AssertUnit(catalog, UnitType.Supply, 0, 3, 12, 1, 4, 20);
            AssertUnit(catalog, UnitType.MotorizedSupply, 3, 12, 21, 2, 6, 40);
        }

        [Test]
        public void Catalog_SchoolAndFoodPreservationCostsMatchPrototypeRules()
        {
            var catalog = LoadCatalog();

            Assert.That(catalog.Research.Single(item => item.Id == "school").ScienceCost, Is.EqualTo(3));
            Assert.That(catalog.Research.Single(item => item.Id == "salting").ScienceCost, Is.EqualTo(20));
            Assert.That(catalog.Research.Single(item => item.Id == "canning").ScienceCost, Is.EqualTo(40));
            CollectionAssert.AreEqual(
                new[] { "irrigation" },
                catalog.Research.Single(item => item.Id == "salting").PrerequisiteIds);
        }

        private static GameBalanceCatalog LoadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GameBalanceCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null, $"Missing balance catalog at {CatalogPath}.");
            return catalog;
        }

        private static void AssertUnique(IEnumerable<string> ids, string label)
        {
            var values = ids.ToArray();
            Assert.That(values, Has.All.Not.Null.And.Not.Empty);
            Assert.That(values.Distinct().Count(), Is.EqualTo(values.Length), $"Duplicate {label} IDs found.");
        }

        private static void AssertUnit(
            GameBalanceCatalog catalog, UnitType type, int tier, int attack, int hitPoints,
            int healing, int movement, int foodCapacity)
        {
            var unit = catalog.Units.Single(item => item.Type == type);
            Assert.That(unit.EquipmentTier, Is.EqualTo(tier));
            Assert.That(unit.Attack, Is.EqualTo(attack));
            Assert.That(unit.MaxHitPoints, Is.EqualTo(hitPoints));
            Assert.That(unit.HealingPerTurn, Is.EqualTo(healing));
            Assert.That(unit.Movement, Is.EqualTo(movement));
            Assert.That(unit.BaseFoodCapacity, Is.EqualTo(foodCapacity));
        }
    }
}
