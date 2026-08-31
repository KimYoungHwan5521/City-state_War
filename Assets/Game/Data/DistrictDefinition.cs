using LittleCiv.Core;
using UnityEngine;

namespace LittleCiv.Data
{
    [CreateAssetMenu(fileName = "DistrictDefinition", menuName = "Little Civilization/District Definition")]
    public sealed class DistrictDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public DistrictType Type;
        public string RequiredResearchId;
        public int ConstructionTurns;
        public ResourceType YieldType;
        public int BaseYield;
        public int ResourceTileBonus;
        public int SameDistrictAdjacencyBonus;
        public int MaxAdjacencyBonus;
        public int MaintenanceGold;
        public int MaxPerCity;
    }
}
