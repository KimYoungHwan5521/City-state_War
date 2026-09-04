using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class DefenseFacilityResolver
    {
        public const int ModernUpkeep = 2;

        public static bool TryStart(GameState state, GameCommand command, out DefenseFacilityState facility)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (command == null) throw new ArgumentNullException(nameof(command));
            facility = null;
            if (!Enum.IsDefined(typeof(DefenseFacilityType), command.PrimaryValue)) return false;
            var targetType = (DefenseFacilityType)command.PrimaryValue;
            if (targetType == DefenseFacilityType.None) return false;
            var player = state.Players.Find(item => item.Id == command.PlayerId);
            var city = FindCity(state, command.SubjectId);
            var tile = FindTile(state, command.TargetId);
            if (player == null || city == null || tile == null || city.OwnerId != command.PlayerId ||
                tile.CityId != city.Id || tile.ControllerId != city.OwnerId ||
                !HasCompletedGovernment(state, tile.Id)) return false;
            if (!IsUnlocked(player, city, targetType)) return false;

            facility = FindAt(state, tile.Id);
            var current = facility == null ? DefenseFacilityType.None : facility.Type;
            if ((int)targetType != (int)current + 1 ||
                (facility != null && facility.RemainingConstructionTurns > 0)) return false;
            var cost = GoldCost(targetType);
            if (city.Gold < cost) return false;
            city.Gold -= cost;
            if (facility == null)
            {
                facility = new DefenseFacilityState
                {
                    Id = state.AllocateId(), CityId = city.Id, TileId = tile.Id
                };
                state.DefenseFacilities.Add(facility);
            }
            facility.BuildingType = targetType;
            facility.RemainingConstructionTurns = ConstructionTurns(targetType);
            return true;
        }

        public static List<EntityId> AdvanceConstruction(GameState state)
        {
            var completed = new List<EntityId>();
            var facilities = new List<DefenseFacilityState>(state.DefenseFacilities);
            facilities.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var facility in facilities)
            {
                if (facility.RemainingConstructionTurns <= 0) continue;
                var tile = FindTile(state, facility.TileId);
                var city = FindCity(state, facility.CityId);
                if (tile == null || city == null || tile.ControllerId != city.OwnerId) continue;
                facility.RemainingConstructionTurns--;
                if (facility.RemainingConstructionTurns > 0) continue;
                facility.Type = facility.BuildingType;
                facility.BuildingType = DefenseFacilityType.None;
                facility.IsModernDefenseActive = facility.Type == DefenseFacilityType.ModernDefense;
                ApplyTileBonus(state, facility);
                completed.Add(facility.Id);
            }
            return completed;
        }

        public static bool TrySetModernActive(GameState state, GameCommand command)
        {
            var facility = Find(state, command.SubjectId);
            if (facility == null || facility.Type != DefenseFacilityType.ModernDefense) return false;
            var city = FindCity(state, facility.CityId);
            var tile = FindTile(state, facility.TileId);
            if (city == null || tile == null || city.OwnerId != command.PlayerId ||
                tile.ControllerId != city.OwnerId) return false;
            var activate = command.PrimaryValue != 0;
            if (!activate)
            {
                facility.IsModernDefenseActive = false;
                facility.RemainingReactivationTurns = 0;
            }
            else
            {
                if (facility.IsModernDefenseActive || facility.RemainingReactivationTurns > 0) return false;
                facility.RemainingReactivationTurns = 2;
            }
            ApplyTileBonus(state, facility);
            return true;
        }

        public static void ResolveModernMaintenance(GameState state, CityState city, List<EntityId> deactivated, List<EntityId> reactivated)
        {
            var facilities = state.DefenseFacilities.FindAll(item => item.CityId == city.Id &&
                item.Type == DefenseFacilityType.ModernDefense &&
                (item.IsModernDefenseActive || item.RemainingReactivationTurns > 0));
            facilities.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var facility in facilities)
            {
                if (city.Gold < ModernUpkeep)
                {
                    facility.IsModernDefenseActive = false;
                    facility.RemainingReactivationTurns = 0;
                    ApplyTileBonus(state, facility);
                    deactivated.Add(facility.Id);
                    continue;
                }
                city.Gold -= ModernUpkeep;
                if (facility.RemainingReactivationTurns <= 0) continue;
                facility.RemainingReactivationTurns--;
                if (facility.RemainingReactivationTurns > 0) continue;
                facility.IsModernDefenseActive = true;
                ApplyTileBonus(state, facility);
                reactivated.Add(facility.Id);
            }
        }

        public static int EffectiveBonus(DefenseFacilityState facility)
        {
            if (facility == null) return 0;
            if (facility.Type == DefenseFacilityType.ModernDefense && !facility.IsModernDefenseActive) return 50;
            switch (facility.Type)
            {
                case DefenseFacilityType.Wall: return 20;
                case DefenseFacilityType.Moat: return 50;
                case DefenseFacilityType.ModernDefense: return 100;
                default: return 0;
            }
        }

        public static int EffectiveEquipmentTier(DefenseFacilityState facility)
        {
            if (facility == null) return 0;
            if (facility.Type == DefenseFacilityType.ModernDefense && !facility.IsModernDefenseActive) return 2;
            switch (facility.Type)
            {
                case DefenseFacilityType.Wall: return 1;
                case DefenseFacilityType.Moat: return 2;
                case DefenseFacilityType.ModernDefense: return 3;
                default: return 0;
            }
        }

        public static int ConstructionTurns(DefenseFacilityType type) => type == DefenseFacilityType.Wall ? 2 : type == DefenseFacilityType.Moat ? 4 : type == DefenseFacilityType.ModernDefense ? 8 : 0;
        public static int GoldCost(DefenseFacilityType type) => type == DefenseFacilityType.Wall ? 5 : type == DefenseFacilityType.Moat ? 10 : type == DefenseFacilityType.ModernDefense ? 20 : 0;

        private static bool IsUnlocked(PlayerState player, CityState city, DefenseFacilityType type)
        {
            if (player.Slot != PlayerSlot.Neutral)
                return !player.ResearchUnlocksEnabled || player.UnlockedDefenseTypes.Contains(type);
            switch (type)
            {
                case DefenseFacilityType.Wall:
                    return NeutralResearchResolver.HasResearch(city, ResearchType.Fortification);
                case DefenseFacilityType.Moat:
                    return NeutralResearchResolver.HasResearch(city, ResearchType.AdvancedFortification);
                case DefenseFacilityType.ModernDefense:
                    return NeutralResearchResolver.HasResearch(city, ResearchType.ModernDefense);
                default: return false;
            }
        }

        private static void ApplyTileBonus(GameState state, DefenseFacilityState facility)
        {
            var tile = FindTile(state, facility.TileId);
            if (tile != null) tile.DefenseBonusPercent = EffectiveBonus(facility);
        }
        private static bool HasCompletedGovernment(GameState state, EntityId tileId) => state.Districts.Exists(item =>
            item.TileId == tileId && item.Type == DistrictType.Government &&
            item.RemainingConstructionTurns <= 0);
        private static DefenseFacilityState FindAt(GameState state, EntityId tileId) => state.DefenseFacilities.Find(item => item.TileId == tileId);
        private static DefenseFacilityState Find(GameState state, EntityId id) => state.DefenseFacilities.Find(item => item.Id == id);
        private static CityState FindCity(GameState state, EntityId id) => state.Cities.Find(item => item.Id == id);
        private static TileState FindTile(GameState state, EntityId id) => state.Tiles.Find(item => item.Id == id);
    }
}
