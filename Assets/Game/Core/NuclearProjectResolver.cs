using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class NuclearProjectAdvanceRecord
    {
        public EntityId ProjectId;
        public EntityId OwnerId;
        public int RemainingTurns;
        public bool Completed;
    }

    public static class NuclearProjectResolver
    {
        public const int StartGold = 10;
        public const int ProjectTurns = 5;

        public static bool TryStart(GameState state, GameCommand command, out NuclearProjectState project)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (command == null) throw new ArgumentNullException(nameof(command));
            project = null;
            var district = state.Districts.Find(item => item.Id == command.SubjectId);
            if (district == null || district.Type != DistrictType.NuclearFacility ||
                !CanOperate(state, district)) return false;
            var city = state.Cities.Find(item => item.Id == district.CityId);
            var player = state.Players.Find(item => item.Id == command.PlayerId);
            if (city == null || player == null || city.OwnerId != command.PlayerId ||
                !player.CompletedResearch.Contains(ResearchType.NuclearFission) ||
                player.HasCompletedNuclearProject || city.Gold < StartGold ||
                state.NuclearProjects.Exists(item => item.DistrictId == district.Id)) return false;
            city.Gold -= StartGold;
            project = new NuclearProjectState
            {
                Id = state.AllocateId(), DistrictId = district.Id, OwnerId = command.PlayerId,
                RemainingTurns = ProjectTurns
            };
            state.NuclearProjects.Add(project);
            return true;
        }

        public static List<NuclearProjectAdvanceRecord> Advance(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<NuclearProjectAdvanceRecord>();
            var projects = new List<NuclearProjectState>(state.NuclearProjects);
            projects.Sort((left, right) => left.Id.CompareTo(right.Id));
            foreach (var project in projects)
            {
                if (project.IsCompleted) continue;
                var district = state.Districts.Find(item => item.Id == project.DistrictId);
                if (district == null || !CanOperate(state, district) ||
                    district.ControllerId != project.OwnerId) continue;
                project.RemainingTurns--;
                if (project.RemainingTurns <= 0)
                {
                    project.RemainingTurns = 0;
                    project.IsCompleted = true;
                    var player = state.Players.Find(item => item.Id == project.OwnerId);
                    if (player != null) player.HasCompletedNuclearProject = true;
                }
                result.Add(new NuclearProjectAdvanceRecord
                {
                    ProjectId = project.Id, OwnerId = project.OwnerId,
                    RemainingTurns = project.RemainingTurns, Completed = project.IsCompleted
                });
            }
            return result;
        }

        private static bool CanOperate(GameState state, DistrictState district)
        {
            var city = state.Cities.Find(item => item.Id == district.CityId);
            return city != null && district.ControllerId == city.OwnerId && district.IsOperational &&
                   !district.IsPillaged && !district.IsMaintenanceSuspended &&
                   district.AssignedCitizens > 0 && district.RemainingConstructionTurns <= 0 &&
                   district.RemainingRepairTurns <= 0;
        }
    }
}
