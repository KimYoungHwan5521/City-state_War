using System;

namespace LittleCiv.Data
{
    public enum ResourceType
    {
        None = 0,
        Food = 1,
        Gold = 2,
        Science = 3,
        Culture = 4
    }

    public enum ResearchEffectType
    {
        None = 0,
        UnlockDistrict = 1,
        UnlockUnit = 2,
        UnlockDefense = 3,
        AddDistrictYield = 4,
        MultiplyCityYieldPercent = 5,
        IncreaseAdjacencyPerNeighbor = 6,
        EnableSecondAgricultureCitizen = 7,
        EnableMechanizedAgriculture = 8,
        MultiplyBaseFoodCapacityPercent = 9,
        UnlockNuclearProject = 10
    }

    [Serializable]
    public struct ResearchEffect
    {
        public ResearchEffectType Type;
        public string TargetId;
        public int Value;
    }
}
