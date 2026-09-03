using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class ResearchAdvanceRecord
    {
        public EntityId PlayerId;
        public ResearchType Type;
        public int AddedProgress;
        public int TotalProgress;
        public bool Completed;
    }

    public static class ResearchRules
    {
        public static int Cost(ResearchType type)
        {
            switch (type)
            {
                case ResearchType.School: return 3;
                case ResearchType.IronWorking:
                case ResearchType.Arts:
                case ResearchType.Currency:
                case ResearchType.Irrigation:
                case ResearchType.Fortification: return 12;
                case ResearchType.Salting: return 20;
                case ResearchType.Gunpowder:
                case ResearchType.Printing:
                case ResearchType.Finance:
                case ResearchType.Fertilizer:
                case ResearchType.AdvancedFortification: return 30;
                case ResearchType.Canning: return 40;
                case ResearchType.Vehicles:
                case ResearchType.MassMedia:
                case ResearchType.EconomicAdministration:
                case ResearchType.MechanizedAgriculture:
                case ResearchType.ModernDefense: return 60;
                case ResearchType.NuclearFission: return 100;
                case ResearchType.SelfLearningAI: return 300;
                default: return 0;
            }
        }

        public static ResearchType Prerequisite(ResearchType type)
        {
            switch (type)
            {
                case ResearchType.IronWorking:
                case ResearchType.Arts:
                case ResearchType.Currency:
                case ResearchType.Irrigation:
                case ResearchType.Fortification: return ResearchType.School;
                case ResearchType.Gunpowder: return ResearchType.IronWorking;
                case ResearchType.Vehicles: return ResearchType.Gunpowder;
                case ResearchType.NuclearFission: return ResearchType.Vehicles;
                case ResearchType.Printing: return ResearchType.Arts;
                case ResearchType.MassMedia: return ResearchType.Printing;
                case ResearchType.Finance: return ResearchType.Currency;
                case ResearchType.EconomicAdministration: return ResearchType.Finance;
                case ResearchType.Fertilizer:
                case ResearchType.Salting: return ResearchType.Irrigation;
                case ResearchType.MechanizedAgriculture: return ResearchType.Fertilizer;
                case ResearchType.Canning: return ResearchType.Salting;
                case ResearchType.AdvancedFortification: return ResearchType.Fortification;
                case ResearchType.ModernDefense: return ResearchType.AdvancedFortification;
                case ResearchType.SelfLearningAI: return ResearchType.NuclearFission;
                default: return ResearchType.None;
            }
        }
    }

    public static class ResearchResolver
    {
        public static bool TrySelect(GameState state, GameCommand command, out ResearchType selected)
        {
            selected = ResearchType.None;
            if (state == null || command == null || !Enum.IsDefined(typeof(ResearchType), command.PrimaryValue))
                return false;
            selected = (ResearchType)command.PrimaryValue;
            if (selected == ResearchType.None || ResearchRules.Cost(selected) <= 0) return false;
            var player = state.Players.Find(item => item.Id == command.PlayerId);
            if (player == null || player.CompletedResearch.Contains(selected)) return false;
            if (selected == ResearchType.SelfLearningAI && !player.HasUnlockedSelfLearningAI) return false;
            var prerequisite = ResearchRules.Prerequisite(selected);
            if (prerequisite != ResearchType.None && !player.CompletedResearch.Contains(prerequisite)) return false;
            player.CurrentResearch = selected;
            return true;
        }

        public static List<ResearchAdvanceRecord> Advance(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var records = new List<ResearchAdvanceRecord>();
            var players = new List<PlayerState>(state.Players);
            players.Sort((left, right) => left.Id.CompareTo(right.Id));
            foreach (var player in players)
            {
                if (player.Slot == PlayerSlot.Neutral) continue;
                var cities = state.Cities.FindAll(item => item.OwnerId == player.Id);
                cities.Sort((left, right) => left.Id.CompareTo(right.Id));
                if (player.CurrentResearch == ResearchType.None)
                {
                    foreach (var city in cities)
                        city.ResearchPoints = Math.Min(city.ResearchPoints, Math.Max(0, city.LastScienceProduction));
                    continue;
                }
                var progress = GetProgress(player, player.CurrentResearch);
                var needed = Math.Max(0, ResearchRules.Cost(progress.Type) - progress.Progress);
                var consumed = 0;
                foreach (var city in cities)
                {
                    var amount = Math.Min(needed - consumed, Math.Max(0, city.ResearchPoints));
                    if (amount <= 0) continue;
                    city.ResearchPoints -= amount;
                    consumed += amount;
                    if (consumed >= needed) break;
                }
                progress.Progress += consumed;
                var completed = progress.Progress >= ResearchRules.Cost(progress.Type);
                records.Add(new ResearchAdvanceRecord
                {
                    PlayerId = player.Id, Type = progress.Type, AddedProgress = consumed,
                    TotalProgress = progress.Progress, Completed = completed
                });
                if (!completed) continue;
                if (!player.CompletedResearch.Contains(progress.Type)) player.CompletedResearch.Add(progress.Type);
                ApplyUnlock(state, player, progress.Type);
                player.CurrentResearch = ResearchType.None;
            }
            return records;
        }

        public static int Progress(PlayerState player, ResearchType type)
        {
            var item = player.ResearchProgress == null ? null : player.ResearchProgress.Find(entry => entry.Type == type);
            return item == null ? 0 : item.Progress;
        }

        public static void CompleteAllStandardResearchForTesting(GameState state, EntityId playerId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var player = state.Players.Find(item => item.Id == playerId && item.Slot != PlayerSlot.Neutral);
            if (player == null) return;
            foreach (ResearchType type in Enum.GetValues(typeof(ResearchType)))
            {
                if (type == ResearchType.None || type == ResearchType.SelfLearningAI) continue;
                if (!player.CompletedResearch.Contains(type)) player.CompletedResearch.Add(type);
                var progress = GetProgress(player, type);
                progress.Progress = ResearchRules.Cost(type);
                ApplyUnlock(state, player, type);
            }
            player.CurrentResearch = ResearchType.None;
        }

        private static ResearchProgressState GetProgress(PlayerState player, ResearchType type)
        {
            if (player.ResearchProgress == null) player.ResearchProgress = new List<ResearchProgressState>();
            var progress = player.ResearchProgress.Find(item => item.Type == type);
            if (progress != null) return progress;
            progress = new ResearchProgressState { Type = type };
            player.ResearchProgress.Add(progress);
            return progress;
        }

        private static void ApplyUnlock(GameState state, PlayerState player, ResearchType type)
        {
            switch (type)
            {
                case ResearchType.School: Add(player.UnlockedDistrictTypes, DistrictType.Science); break;
                case ResearchType.IronWorking: Add(player.UnlockedUnitTypes, UnitType.IronInfantry); break;
                case ResearchType.Gunpowder: Add(player.UnlockedUnitTypes, UnitType.GunpowderInfantry); break;
                case ResearchType.Vehicles:
                    Add(player.UnlockedUnitTypes, UnitType.MechanizedInfantry);
                    Add(player.UnlockedUnitTypes, UnitType.MotorizedSupply);
                    break;
                case ResearchType.NuclearFission: Add(player.UnlockedDistrictTypes, DistrictType.NuclearFacility); break;
                case ResearchType.Arts: Add(player.UnlockedDistrictTypes, DistrictType.Culture); break;
                case ResearchType.Fortification: Add(player.UnlockedDefenseTypes, DefenseFacilityType.Wall); break;
                case ResearchType.AdvancedFortification: Add(player.UnlockedDefenseTypes, DefenseFacilityType.Moat); break;
                case ResearchType.ModernDefense: Add(player.UnlockedDefenseTypes, DefenseFacilityType.ModernDefense); break;
                case ResearchType.Salting: player.FoodCapacityPercent = Math.Max(player.FoodCapacityPercent, 150); break;
                case ResearchType.Canning: player.FoodCapacityPercent = Math.Max(player.FoodCapacityPercent, 200); break;
                case ResearchType.SelfLearningAI: player.HasCompletedSelfLearningAI = true; break;
                case ResearchType.MechanizedAgriculture:
                    for (var index = 0; index < state.Districts.Count; index++)
                    {
                        var district = state.Districts[index];
                        if (district.Type == DistrictType.Agriculture && district.AssignedCitizens > 1)
                        {
                            var city = state.Cities.Find(item => item.Id == district.CityId);
                            if (city != null && city.OwnerId == player.Id) district.AssignedCitizens = 1;
                        }
                    }
                    break;
            }
        }

        private static void Add<T>(List<T> list, T value)
        {
            if (list == null) return;
            if (!list.Contains(value)) list.Add(value);
        }
    }
}
