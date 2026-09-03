using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class GameStateHasher
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Compute(GameState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var hash = OffsetBasis;
            Add(ref hash, state.SchemaVersion);
            Add(ref hash, state.MatchSeed);
            Add(ref hash, state.TurnNumber);
            Add(ref hash, state.NextEntityId);
            Add(ref hash, (int)state.Victory);
            Add(ref hash, state.WinnerId.Value);

            HashPlayers(ref hash, state.Players);
            HashCities(ref hash, state.Cities);
            HashTiles(ref hash, state.Tiles);
            HashUnits(ref hash, state.Units);
            HashDistricts(ref hash, state.Districts);
            HashUnitTrainings(ref hash, state.UnitTrainings);
            HashDefenseFacilities(ref hash, state.DefenseFacilities);
            HashNuclearProjects(ref hash, state.NuclearProjects);
            HashTradeReservations(ref hash, state.TradeReservations);
            HashLevies(ref hash, state.Levies);
            HashMapTopology(ref hash, state.MapTopology);
            return hash;
        }

        private static void HashPlayers(ref ulong hash, List<PlayerState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, (int)item.Slot);
                Add(ref hash, item.Gold);
                Add(ref hash, item.StoredFood);
                Add(ref hash, item.ReserveTimeSeconds);
                var unlockedUnits = item.UnlockedUnitTypes == null
                    ? new List<UnitType>()
                    : new List<UnitType>(item.UnlockedUnitTypes);
                unlockedUnits.Sort();
                Add(ref hash, unlockedUnits.Count);
                for (var unlockedIndex = 0; unlockedIndex < unlockedUnits.Count; unlockedIndex++)
                    Add(ref hash, (int)unlockedUnits[unlockedIndex]);
                var districts = item.UnlockedDistrictTypes == null
                    ? new List<DistrictType>() : new List<DistrictType>(item.UnlockedDistrictTypes);
                districts.Sort();
                Add(ref hash, districts.Count);
                foreach (var type in districts) Add(ref hash, (int)type);
                var defenses = item.UnlockedDefenseTypes == null
                    ? new List<DefenseFacilityType>() : new List<DefenseFacilityType>(item.UnlockedDefenseTypes);
                defenses.Sort();
                Add(ref hash, defenses.Count);
                foreach (var type in defenses) Add(ref hash, (int)type);
                Add(ref hash, (int)item.CurrentResearch);
                var completed = item.CompletedResearch == null
                    ? new List<ResearchType>() : new List<ResearchType>(item.CompletedResearch);
                completed.Sort();
                Add(ref hash, completed.Count);
                foreach (var type in completed) Add(ref hash, (int)type);
                var progress = item.ResearchProgress == null
                    ? new List<ResearchProgressState>() : new List<ResearchProgressState>(item.ResearchProgress);
                progress.Sort((left, right) => left.Type.CompareTo(right.Type));
                Add(ref hash, progress.Count);
                foreach (var research in progress)
                {
                    Add(ref hash, (int)research.Type);
                    Add(ref hash, research.Progress);
                }
                Add(ref hash, item.FoodCapacityPercent);
                Add(ref hash, item.ResearchUnlocksEnabled ? 1 : 0);
                Add(ref hash, item.HasCompletedNuclearProject ? 1 : 0);
                Add(ref hash, item.HasUnlockedSelfLearningAI ? 1 : 0);
                Add(ref hash, item.HasCompletedSelfLearningAI ? 1 : 0);
                Add(ref hash, item.HasMetCultureVictoryCondition ? 1 : 0);
            }
        }

        private static void HashCities(ref ulong hash, List<CityState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.Name);
                Add(ref hash, item.OwnerId.Value);
                Add(ref hash, item.WorldQ);
                Add(ref hash, item.WorldR);
                Add(ref hash, item.Population);
                Add(ref hash, item.GovernmentCitizens);
                Add(ref hash, item.Gold);
                Add(ref hash, item.StoredFood);
                Add(ref hash, item.GrowthProgress);
                Add(ref hash, item.FamineProgress);
                Add(ref hash, item.ResearchPoints);
                Add(ref hash, item.LastFoodProduction);
                Add(ref hash, item.LastUnitFoodConsumption);
                Add(ref hash, item.LastGoldProduction);
                Add(ref hash, item.LastScienceProduction);
                Add(ref hash, item.LastCultureProduction);
                Add(ref hash, item.TestGovernmentFoodBonus);
                Add(ref hash, item.TestGovernmentScienceBonus);
                Add(ref hash, item.TestGovernmentCultureBonus);
                Add(ref hash, item.TestGovernmentGoldBonus);
                Add(ref hash, (int)item.NeutralCurrentResearch);
                var neutralCompleted = item.NeutralCompletedResearch == null
                    ? new List<ResearchType>() : new List<ResearchType>(item.NeutralCompletedResearch);
                neutralCompleted.Sort();
                Add(ref hash, neutralCompleted.Count);
                foreach (var research in neutralCompleted) Add(ref hash, (int)research);
                var neutralProgress = item.NeutralResearchProgress == null
                    ? new List<ResearchProgressState>() : new List<ResearchProgressState>(item.NeutralResearchProgress);
                neutralProgress.Sort((left, right) => left.Type.CompareTo(right.Type));
                Add(ref hash, neutralProgress.Count);
                foreach (var research in neutralProgress)
                {
                    Add(ref hash, (int)research.Type);
                    Add(ref hash, research.Progress);
                }
                Add(ref hash, (int)item.NeutralSpecialization);
                Add(ref hash, item.CultureSubjectToId.Value);
                Add(ref hash, item.OccupyingPlayerId.Value);
                Add(ref hash, item.IndependenceProgress);
                var relations = item.NeutralRelations == null
                    ? new List<NeutralRelationState>()
                    : new List<NeutralRelationState>(item.NeutralRelations);
                relations.Sort((left, right) => left.PlayerId.CompareTo(right.PlayerId));
                Add(ref hash, relations.Count);
                foreach (var relation in relations)
                {
                    Add(ref hash, relation.PlayerId.Value);
                    Add(ref hash, relation.Favor);
                }
                var influences = item.CultureInfluences == null
                    ? new List<CultureInfluenceState>()
                    : new List<CultureInfluenceState>(item.CultureInfluences);
                influences.Sort((left, right) => left.CultureOwnerId.CompareTo(right.CultureOwnerId));
                Add(ref hash, influences.Count);
                foreach (var influence in influences)
                {
                    Add(ref hash, influence.CultureOwnerId.Value);
                    Add(ref hash, influence.PreferredCitizens);
                    Add(ref hash, influence.ConversionProgress);
                    Add(ref hash, influence.ReversionProgress);
                }
            }
        }

        private static void HashTiles(ref ulong hash, List<TileState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.CityId.Value);
                Add(ref hash, item.Q);
                Add(ref hash, item.R);
                Add(ref hash, item.ControllerId.Value);
                Add(ref hash, item.GroundFood);
                Add(ref hash, item.GroundFoodOwnerId.Value);
                Add(ref hash, item.GroundFoodReturnTurn);
                Add(ref hash, item.IsSharedBoundary ? 1 : 0);
                Add(ref hash, item.DefenseBonusPercent);
                Add(ref hash, (int)item.ResourceType);
                var visibleCities = item.VisibleCityIds == null
                    ? new List<EntityId>()
                    : new List<EntityId>(item.VisibleCityIds);
                visibleCities.Sort();
                Add(ref hash, visibleCities.Count);
                foreach (var cityId in visibleCities)
                {
                    Add(ref hash, cityId.Value);
                }
            }
        }

        private static void HashUnits(ref ulong hash, List<UnitState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.OwnerId.Value);
                Add(ref hash, item.HomeCityId.Value);
                Add(ref hash, item.TileId.Value);
                Add(ref hash, (int)item.Type);
                Add(ref hash, item.HitPoints);
                Add(ref hash, item.CarriedFood);
                Add(ref hash, item.IsStarving ? 1 : 0);
                Add(ref hash, item.RemainingMovement);
                Add(ref hash, item.HasAutomaticDefense ? 1 : 0);
                Add(ref hash, item.MaintenancePriority);
                Add(ref hash, item.CreatedTurn);
                Add(ref hash, item.ManeuverRecommandTurn);
            }
        }

        private static void HashDistricts(ref ulong hash, List<DistrictState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.CityId.Value);
                Add(ref hash, item.TileId.Value);
                Add(ref hash, (int)item.Type);
                Add(ref hash, item.ControllerId.Value);
                Add(ref hash, item.IsOperational ? 1 : 0);
                Add(ref hash, item.AssignedCitizens);
                Add(ref hash, item.RemainingConstructionTurns);
                Add(ref hash, item.CitizenRemovalPriority);
                Add(ref hash, item.MaintenancePriority);
                Add(ref hash, item.IsMaintenanceSuspended ? 1 : 0);
                Add(ref hash, item.IsPillaged ? 1 : 0);
                Add(ref hash, item.RemainingRepairTurns);
            }
        }

        private static void HashUnitTrainings(ref ulong hash, List<UnitTrainingState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.DistrictId.Value);
                Add(ref hash, item.OwnerId.Value);
                Add(ref hash, (int)item.Type);
                Add(ref hash, item.RemainingTurns);
                Add(ref hash, item.IsAwaitingDeployment ? 1 : 0);
            }
        }

        private static void HashDefenseFacilities(ref ulong hash, List<DefenseFacilityState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.CityId.Value);
                Add(ref hash, item.TileId.Value);
                Add(ref hash, (int)item.Type);
                Add(ref hash, (int)item.BuildingType);
                Add(ref hash, item.RemainingConstructionTurns);
                Add(ref hash, item.IsModernDefenseActive ? 1 : 0);
                Add(ref hash, item.RemainingReactivationTurns);
            }
        }

        private static void HashNuclearProjects(ref ulong hash, List<NuclearProjectState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.DistrictId.Value);
                Add(ref hash, item.OwnerId.Value);
                Add(ref hash, item.RemainingTurns);
                Add(ref hash, item.IsCompleted ? 1 : 0);
            }
        }

        private static void HashTradeReservations(ref ulong hash, List<TradeReservationState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.PlayerId.Value);
                Add(ref hash, item.SourceCityId.Value);
                Add(ref hash, item.TargetCityId.Value);
                Add(ref hash, (int)item.ResourceType);
                Add(ref hash, item.ResourceAmount);
                Add(ref hash, item.NetGoldPayment);
                Add(ref hash, item.ApplyTurn);
                Add(ref hash, item.IsSale ? 1 : 0);
            }
        }

        private static void HashLevies(ref ulong hash, List<LevyState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.MilitaryCityId.Value);
                Add(ref hash, item.PlayerId.Value);
                Add(ref hash, item.PaymentCityId.Value);
                Add(ref hash, item.StartTurn);
                Add(ref hash, item.EndTurnExclusive);
                Add(ref hash, item.PaidGold);
                var units = item.Units == null ? new List<LevyUnitState>() :
                    new List<LevyUnitState>(item.Units);
                units.Sort((left, right) => left.UnitId.CompareTo(right.UnitId));
                Add(ref hash, units.Count);
                foreach (var unit in units)
                {
                    Add(ref hash, unit.UnitId.Value);
                    Add(ref hash, unit.OriginalHomeCityId.Value);
                }
            }
        }

        private static void HashMapTopology(ref ulong hash, WorldMapTopology topology)
        {
            var views = topology == null || topology.CityViews == null
                ? new List<CityMapView>()
                : new List<CityMapView>(topology.CityViews);
            views.Sort((left, right) => left.CityId.CompareTo(right.CityId));
            Add(ref hash, views.Count);
            foreach (var view in views)
            {
                Add(ref hash, view.CityId.Value);
                var placements = view.Tiles == null
                    ? new List<CityTilePlacement>()
                    : new List<CityTilePlacement>(view.Tiles);
                placements.Sort((left, right) =>
                {
                    var qComparison = left.LocalQ.CompareTo(right.LocalQ);
                    return qComparison != 0 ? qComparison : left.LocalR.CompareTo(right.LocalR);
                });
                Add(ref hash, placements.Count);
                foreach (var placement in placements)
                {
                    Add(ref hash, placement.TileId.Value);
                    Add(ref hash, placement.LocalQ);
                    Add(ref hash, placement.LocalR);
                    Add(ref hash, placement.IsBuildable ? 1 : 0);
                }
            }
        }

        private static List<T> SortedCopy<T>(List<T> source, Func<T, long> keySelector)
        {
            var result = source == null ? new List<T>() : new List<T>(source);
            result.Sort((left, right) => keySelector(left).CompareTo(keySelector(right)));
            return result;
        }

        private static void Add(ref ulong hash, int value) => Add(ref hash, (long)value);

        private static void Add(ref ulong hash, string value)
        {
            value = value ?? string.Empty;

            Add(ref hash, value.Length);
            foreach (var character in value)
            {
                Add(ref hash, character);
            }
        }

        private static void Add(ref ulong hash, long value)
        {
            unchecked
            {
                var data = (ulong)value;
                for (var i = 0; i < sizeof(long); i++)
                {
                    hash ^= (byte)(data & 0xFF);
                    hash *= Prime;
                    data >>= 8;
                }
            }
        }
    }
}
