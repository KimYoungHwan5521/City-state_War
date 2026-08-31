using System;
using System.Collections.Generic;
using System.IO;
using LittleCiv.Core;
using LittleCiv.Data;
using UnityEditor;
using UnityEngine;

namespace LittleCiv.Editor
{
    public static class DefaultBalanceAssetGenerator
    {
        public const string RootPath = "Assets/Game/Balance";
        public const string CatalogPath = RootPath + "/GameBalanceCatalog.asset";

        [MenuItem("Little Civilization/Generate Default Balance Assets")]
        public static void Generate()
        {
            EnsureFolder(RootPath);
            EnsureFolder(RootPath + "/Units");
            EnsureFolder(RootPath + "/Districts");
            EnsureFolder(RootPath + "/Research");

            var units = CreateUnits();
            var districts = CreateDistricts();
            var research = CreateResearch();
            var catalog = LoadOrCreate<GameBalanceCatalog>(CatalogPath);
            catalog.SchemaVersion = 1;
            catalog.Units = units.ToArray();
            catalog.Districts = districts.ToArray();
            catalog.Research = research.ToArray();
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = catalog;
            Debug.Log($"Generated default balance catalog at {CatalogPath}.");
        }

        [MenuItem("Little Civilization/Rebuild Default Balance Assets")]
        public static void Rebuild()
        {
            if (AssetDatabase.IsValidFolder(RootPath))
            {
                AssetDatabase.DeleteAsset(RootPath);
            }

            Generate();
        }

        private static List<UnitDefinition> CreateUnits()
        {
            return new List<UnitDefinition>
            {
                Unit("militia", "민병대", UnitType.Militia, null, false, 0, 3, 16, 2, 2, 6, 1, 3, 1, true),
                Unit("iron-infantry", "철제보병", UnitType.IronInfantry, "iron-working", false, 1, 5, 16, 2, 2, 6, 2, 7, 2, true),
                Unit("gunpowder-infantry", "화약보병", UnitType.GunpowderInfantry, "gunpowder", false, 2, 7, 27, 3, 2, 6, 3, 12, 3, true),
                Unit("mechanized-infantry", "기계화보병", UnitType.MechanizedInfantry, "vehicles", false, 3, 9, 38, 4, 3, 10, 4, 20, 5, true),
                Unit("supply", "보급병", UnitType.Supply, null, true, 0, 1, 9, 1, 4, 20, 1, 2, 1, true),
                Unit("motorized-supply", "차량화 보급대", UnitType.MotorizedSupply, "vehicles", true, 3, 4, 16, 2, 6, 40, 3, 14, 3, true)
            };
        }

        private static UnitDefinition Unit(
            string id, string displayName, UnitType type, string requiredResearchId, bool isSupply,
            int equipmentTier, int attack, int hitPoints, int healing, int movement, int food,
            int trainingTurns, int trainingGold, int maintenanceGold, bool provisional)
        {
            var asset = LoadOrCreate<UnitDefinition>($"{RootPath}/Units/{id}.asset");
            asset.Id = id;
            asset.DisplayName = displayName;
            asset.Type = type;
            asset.RequiredResearchId = requiredResearchId;
            asset.IsSupplyUnit = isSupply;
            asset.EquipmentTier = equipmentTier;
            asset.Attack = attack;
            asset.MaxHitPoints = hitPoints;
            asset.HealingPerTurn = healing;
            asset.Movement = movement;
            asset.BaseFoodCapacity = food;
            asset.TrainingTurns = trainingTurns;
            asset.TrainingGold = trainingGold;
            asset.MaintenanceGold = maintenanceGold;
            asset.IsEconomicDataProvisional = provisional;
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static List<DistrictDefinition> CreateDistricts()
        {
            return new List<DistrictDefinition>
            {
                District("agriculture", "농업지구", DistrictType.Agriculture, null, 3, ResourceType.Food, 2, 2, 0, 0, 0, 0),
                District("commerce", "상업지구", DistrictType.Commerce, null, 3, ResourceType.Gold, 2, 2, 1, 2, 0, 0),
                District("science", "과학지구", DistrictType.Science, "school", 3, ResourceType.Science, 2, 2, 1, 2, 1, 0),
                District("culture", "문화지구", DistrictType.Culture, "arts", 3, ResourceType.Culture, 2, 1, 1, 2, 1, 0),
                District("military", "군사지구", DistrictType.Military, null, 3, ResourceType.None, 0, 0, 0, 0, 0, 0),
                District("nuclear-facility", "핵시설", DistrictType.NuclearFacility, "nuclear-fission", 5, ResourceType.None, 0, 0, 0, 0, 3, 1)
            };
        }

        private static DistrictDefinition District(
            string id, string displayName, DistrictType type, string requiredResearchId,
            int constructionTurns, ResourceType yieldType, int baseYield, int resourceBonus,
            int adjacencyBonus, int maxAdjacency, int maintenance, int maxPerCity)
        {
            var asset = LoadOrCreate<DistrictDefinition>($"{RootPath}/Districts/{id}.asset");
            asset.Id = id;
            asset.DisplayName = displayName;
            asset.Type = type;
            asset.RequiredResearchId = requiredResearchId;
            asset.ConstructionTurns = constructionTurns;
            asset.YieldType = yieldType;
            asset.BaseYield = baseYield;
            asset.ResourceTileBonus = resourceBonus;
            asset.SameDistrictAdjacencyBonus = adjacencyBonus;
            asset.MaxAdjacencyBonus = maxAdjacency;
            asset.MaintenanceGold = maintenance;
            asset.MaxPerCity = maxPerCity;
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static List<ResearchDefinition> CreateResearch()
        {
            return new List<ResearchDefinition>
            {
                Research("school", "학교", 3, null, Effect(ResearchEffectType.UnlockDistrict, "science")),
                Research("iron-working", "철기", 12, new[] { "school" }, Effect(ResearchEffectType.UnlockUnit, "iron-infantry")),
                Research("gunpowder", "화약", 30, new[] { "iron-working" }, Effect(ResearchEffectType.UnlockUnit, "gunpowder-infantry")),
                Research("vehicles", "차량", 60, new[] { "gunpowder" }, Effect(ResearchEffectType.UnlockUnit, "mechanized-infantry"), Effect(ResearchEffectType.UnlockUnit, "motorized-supply")),
                Research("nuclear-fission", "핵분열", 100, new[] { "vehicles" }, Effect(ResearchEffectType.UnlockDistrict, "nuclear-facility"), Effect(ResearchEffectType.UnlockNuclearProject, "nuclear-project")),
                Research("arts", "예술", 12, new[] { "school" }, Effect(ResearchEffectType.UnlockDistrict, "culture")),
                Research("printing", "인쇄", 30, new[] { "arts" }, Effect(ResearchEffectType.AddDistrictYield, "culture", 1)),
                Research("mass-media", "대중매체", 60, new[] { "printing" }, Effect(ResearchEffectType.MultiplyCityYieldPercent, "culture", 125)),
                Research("currency", "화폐", 12, new[] { "school" }, Effect(ResearchEffectType.AddDistrictYield, "commerce", 1)),
                Research("finance", "금융", 30, new[] { "currency" }, Effect(ResearchEffectType.IncreaseAdjacencyPerNeighbor, "commerce", 2)),
                Research("economic-administration", "경제행정", 60, new[] { "finance" }, Effect(ResearchEffectType.MultiplyCityYieldPercent, "gold", 125)),
                Research("irrigation", "관개", 12, new[] { "school" }, Effect(ResearchEffectType.EnableSecondAgricultureCitizen, "agriculture", 150)),
                Research("fertilizer", "비료", 30, new[] { "irrigation" }, Effect(ResearchEffectType.AddDistrictYield, "agriculture", 1)),
                Research("mechanized-agriculture", "기계농업", 60, new[] { "fertilizer" }, Effect(ResearchEffectType.EnableMechanizedAgriculture, "agriculture", 150)),
                Research("salting", "염지", 20, new[] { "irrigation" }, Effect(ResearchEffectType.MultiplyBaseFoodCapacityPercent, "all-units", 150)),
                Research("canning", "통조림", 40, new[] { "salting" }, Effect(ResearchEffectType.MultiplyBaseFoodCapacityPercent, "all-units", 200)),
                Research("fortification", "축성", 12, new[] { "school" }, Effect(ResearchEffectType.UnlockDefense, "walls")),
                Research("advanced-fortification", "요새화", 30, new[] { "fortification" }, Effect(ResearchEffectType.UnlockDefense, "moat")),
                Research("modern-defense", "현대 방어체계", 60, new[] { "advanced-fortification" }, Effect(ResearchEffectType.UnlockDefense, "modern-defense"))
            };
        }

        private static ResearchDefinition Research(
            string id, string displayName, int cost, string[] prerequisites, params ResearchEffect[] effects)
        {
            var asset = LoadOrCreate<ResearchDefinition>($"{RootPath}/Research/{id}.asset");
            asset.Id = id;
            asset.DisplayName = displayName;
            asset.ScienceCost = cost;
            asset.PrerequisiteIds = prerequisites ?? Array.Empty<string>();
            asset.Effects = effects ?? Array.Empty<ResearchEffect>();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static ResearchEffect Effect(ResearchEffectType type, string targetId, int value = 0)
        {
            return new ResearchEffect { Type = type, TargetId = targetId, Value = value };
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path))
            {
                AssetDatabase.DeleteAsset(path);
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            var normalized = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            var parent = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            var folderName = Path.GetFileName(normalized);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
            {
                throw new InvalidOperationException($"Invalid asset folder path: {path}");
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
