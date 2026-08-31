using System;
using LittleCiv.Core;
using UnityEngine;

namespace LittleCiv.Runtime
{
    public static class GameStateJsonSerializer
    {
        public static string Serialize(GameState state, bool prettyPrint = false)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return JsonUtility.ToJson(state, prettyPrint);
        }

        public static GameState Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("A save payload is required.", nameof(json));
            }

            var state = JsonUtility.FromJson<GameState>(json);
            if (state == null)
            {
                throw new InvalidOperationException("The save payload could not be deserialized.");
            }

            if (state.SchemaVersion != GameState.CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Save schema {state.SchemaVersion} is not supported by schema {GameState.CurrentSchemaVersion}.");
            }

            return state;
        }
    }
}
