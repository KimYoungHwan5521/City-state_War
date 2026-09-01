namespace LittleCiv.Core
{
    public sealed class YieldBreakdown
    {
        public int Government;
        public int DistrictBase;
        public int ResourceBonus;
        public int AdjacencyBonus;
        public int Total => Government + DistrictBase + ResourceBonus + AdjacencyBonus;
    }

    public sealed class CityEconomyBreakdown
    {
        public readonly YieldBreakdown Food = new YieldBreakdown();
        public readonly YieldBreakdown Gold = new YieldBreakdown();
        public readonly YieldBreakdown Science = new YieldBreakdown();
        public readonly YieldBreakdown Culture = new YieldBreakdown();
        public int PopulationConsumption;
        public int UnitFoodConsumption;
        public int FoodNet => Food.Total - PopulationConsumption - UnitFoodConsumption;
        public int GrowthRequired;
        public int FamineRequired;
        public int UnitUpkeep;
        public int FacilityUpkeep;
    }
}
