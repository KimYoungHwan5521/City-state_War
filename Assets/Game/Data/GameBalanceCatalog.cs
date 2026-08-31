using UnityEngine;

namespace LittleCiv.Data
{
    [CreateAssetMenu(fileName = "GameBalanceCatalog", menuName = "Little Civilization/Game Balance Catalog")]
    public sealed class GameBalanceCatalog : ScriptableObject
    {
        public int SchemaVersion = 1;
        public UnitDefinition[] Units = System.Array.Empty<UnitDefinition>();
        public DistrictDefinition[] Districts = System.Array.Empty<DistrictDefinition>();
        public ResearchDefinition[] Research = System.Array.Empty<ResearchDefinition>();
    }
}
