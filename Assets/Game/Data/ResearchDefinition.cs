using System;
using UnityEngine;

namespace LittleCiv.Data
{
    [CreateAssetMenu(fileName = "ResearchDefinition", menuName = "Little Civilization/Research Definition")]
    public sealed class ResearchDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public int ScienceCost;
        public string[] PrerequisiteIds = Array.Empty<string>();
        public ResearchEffect[] Effects = Array.Empty<ResearchEffect>();
    }
}
