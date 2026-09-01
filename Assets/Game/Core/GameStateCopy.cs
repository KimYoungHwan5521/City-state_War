using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class GameStateCopy
    {
        public static GameState Clone(GameState source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var result = new GameState
            {
                SchemaVersion = source.SchemaVersion,
                MatchSeed = source.MatchSeed,
                TurnNumber = source.TurnNumber,
                NextEntityId = source.NextEntityId,
                Victory = source.Victory,
                WinnerId = source.WinnerId,
                MapTopology = CloneTopology(source.MapTopology)
            };

            for (var i = 0; i < source.Players.Count; i++)
            {
                var item = source.Players[i];
                result.Players.Add(new PlayerState
                {
                    Id = item.Id,
                    Slot = item.Slot,
                    Gold = item.Gold,
                    StoredFood = item.StoredFood,
                    ReserveTimeSeconds = item.ReserveTimeSeconds,
                    UnlockedUnitTypes = item.UnlockedUnitTypes == null
                        ? new List<UnitType>()
                        : new List<UnitType>(item.UnlockedUnitTypes)
                });
            }

            for (var i = 0; i < source.Cities.Count; i++)
            {
                var item = source.Cities[i];
                result.Cities.Add(new CityState
                {
                    Id = item.Id,
                    Name = item.Name,
                    OwnerId = item.OwnerId,
                    WorldQ = item.WorldQ,
                    WorldR = item.WorldR,
                    Population = item.Population,
                    GovernmentCitizens = item.GovernmentCitizens,
                    Gold = item.Gold,
                    StoredFood = item.StoredFood,
                    GrowthProgress = item.GrowthProgress,
                    FamineProgress = item.FamineProgress,
                    ResearchPoints = item.ResearchPoints,
                    LastFoodProduction = item.LastFoodProduction,
                    LastGoldProduction = item.LastGoldProduction,
                    LastScienceProduction = item.LastScienceProduction,
                    LastCultureProduction = item.LastCultureProduction
                });
            }

            for (var i = 0; i < source.Tiles.Count; i++)
            {
                var item = source.Tiles[i];
                result.Tiles.Add(new TileState
                {
                    Id = item.Id,
                    CityId = item.CityId,
                    Q = item.Q,
                    R = item.R,
                    ControllerId = item.ControllerId,
                    GroundFood = item.GroundFood,
                    GroundFoodOwnerId = item.GroundFoodOwnerId,
                    GroundFoodReturnTurn = item.GroundFoodReturnTurn,
                    IsSharedBoundary = item.IsSharedBoundary,
                    DefenseBonusPercent = item.DefenseBonusPercent,
                    ResourceType = item.ResourceType,
                    VisibleCityIds = item.VisibleCityIds == null
                        ? new List<EntityId>()
                        : new List<EntityId>(item.VisibleCityIds)
                });
            }

            for (var i = 0; i < source.Units.Count; i++)
            {
                var item = source.Units[i];
                result.Units.Add(new UnitState
                {
                    Id = item.Id,
                    OwnerId = item.OwnerId,
                    TileId = item.TileId,
                    Type = item.Type,
                    HitPoints = item.HitPoints,
                    CarriedFood = item.CarriedFood,
                    IsStarving = item.IsStarving,
                    RemainingMovement = item.RemainingMovement,
                    HasAutomaticDefense = item.HasAutomaticDefense,
                    MaintenancePriority = item.MaintenancePriority,
                    CreatedTurn = item.CreatedTurn
                });
            }

            for (var i = 0; i < source.Districts.Count; i++)
            {
                var item = source.Districts[i];
                result.Districts.Add(new DistrictState
                {
                    Id = item.Id,
                    CityId = item.CityId,
                    TileId = item.TileId,
                    Type = item.Type,
                    ControllerId = item.ControllerId,
                    IsOperational = item.IsOperational,
                    AssignedCitizens = item.AssignedCitizens,
                    RemainingConstructionTurns = item.RemainingConstructionTurns,
                    CitizenRemovalPriority = item.CitizenRemovalPriority,
                    MaintenancePriority = item.MaintenancePriority,
                    IsMaintenanceSuspended = item.IsMaintenanceSuspended
                });
            }

            for (var i = 0; i < source.UnitTrainings.Count; i++)
            {
                var item = source.UnitTrainings[i];
                result.UnitTrainings.Add(new UnitTrainingState
                {
                    Id = item.Id,
                    DistrictId = item.DistrictId,
                    OwnerId = item.OwnerId,
                    Type = item.Type,
                    RemainingTurns = item.RemainingTurns,
                    IsAwaitingDeployment = item.IsAwaitingDeployment
                });
            }

            return result;
        }

        private static WorldMapTopology CloneTopology(WorldMapTopology source)
        {
            var result = new WorldMapTopology();
            if (source == null || source.CityViews == null) return result;
            for (var i = 0; i < source.CityViews.Count; i++)
            {
                var sourceView = source.CityViews[i];
                var view = new CityMapView { CityId = sourceView.CityId };
                if (sourceView.Tiles != null)
                {
                    for (var j = 0; j < sourceView.Tiles.Count; j++)
                    {
                        var tile = sourceView.Tiles[j];
                        view.Tiles.Add(new CityTilePlacement
                        {
                            TileId = tile.TileId,
                            LocalQ = tile.LocalQ,
                            LocalR = tile.LocalR,
                            IsBuildable = tile.IsBuildable
                        });
                    }
                }

                result.CityViews.Add(view);
            }

            return result;
        }
    }
}
