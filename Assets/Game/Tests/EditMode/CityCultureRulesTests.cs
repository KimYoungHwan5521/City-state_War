using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class CityCultureRulesTests
    {
        [Test]
        public void CitizensWithoutForeignPreferenceAreNativeCulture()
        {
            var city = new CityState { OwnerId = new EntityId(1), Population = 7 };
            city.CultureInfluences.Add(new CultureInfluenceState
            {
                CultureOwnerId = new EntityId(2), PreferredCitizens = 3, ConversionProgress = 6
            });

            Assert.That(CityCultureRules.NativeCitizens(city), Is.EqualTo(4));
            Assert.That(CityCultureRules.PreferredCitizens(city, new EntityId(1)), Is.EqualTo(4));
            Assert.That(CityCultureRules.PreferredCitizens(city, new EntityId(2)), Is.EqualTo(3));
        }

        [Test]
        public void NormalizeMergesDuplicateCulturesAndCapsForeignCitizensAtPopulation()
        {
            var city = new CityState { OwnerId = new EntityId(1), Population = 4 };
            city.CultureInfluences.Add(new CultureInfluenceState
                { CultureOwnerId = new EntityId(3), PreferredCitizens = 3, ConversionProgress = 2 });
            city.CultureInfluences.Add(new CultureInfluenceState
                { CultureOwnerId = new EntityId(2), PreferredCitizens = 3, ConversionProgress = 4 });
            city.CultureInfluences.Add(new CultureInfluenceState
                { CultureOwnerId = new EntityId(2), PreferredCitizens = 2, ConversionProgress = 3 });

            CityCultureRules.Normalize(city);

            Assert.That(city.CultureInfluences.Count, Is.EqualTo(2));
            Assert.That(city.CultureInfluences[0].CultureOwnerId, Is.EqualTo(new EntityId(2)));
            Assert.That(city.CultureInfluences[0].PreferredCitizens, Is.EqualTo(4));
            Assert.That(city.CultureInfluences[0].ConversionProgress, Is.EqualTo(7));
            Assert.That(city.CultureInfluences[1].PreferredCitizens, Is.Zero);
            Assert.That(CityCultureRules.NativeCitizens(city), Is.Zero);
        }

        [Test]
        public void CulturePopulationSurvivesCopyAndChangesDeterministicHash()
        {
            var state = GameState.CreateNew(12001);
            var owner = state.AllocateId();
            var foreign = state.AllocateId();
            state.Players.Add(new PlayerState { Id = owner, Slot = PlayerSlot.PlayerOne });
            state.Players.Add(new PlayerState { Id = foreign, Slot = PlayerSlot.PlayerTwo });
            state.Cities.Add(new CityState { Id = state.AllocateId(), OwnerId = owner, Population = 4 });
            CityCultureRules.GetOrCreate(state.Cities[0], foreign).PreferredCitizens = 1;
            var copy = GameStateCopy.Clone(state);

            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(state)));
            copy.Cities[0].CultureInfluences[0].PreferredCitizens++;
            Assert.That(GameStateHasher.Compute(copy), Is.Not.EqualTo(GameStateHasher.Compute(state)));
        }
    }
}
