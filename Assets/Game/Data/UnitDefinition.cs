using LittleCiv.Core;
using UnityEngine;

namespace LittleCiv.Data
{
    [CreateAssetMenu(fileName = "UnitDefinition", menuName = "Little Civilization/Unit Definition")]
    public sealed class UnitDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public UnitType Type;
        public string RequiredResearchId;
        public bool IsSupplyUnit;
        public int EquipmentTier;
        public int Attack;
        public int MaxHitPoints;
        public int HealingPerTurn;
        public int Movement;
        public int BaseFoodCapacity;
        public int TrainingTurns;
        public int TrainingGold;
        public int MaintenanceGold;
        public bool IsEconomicDataProvisional;
    }
}
