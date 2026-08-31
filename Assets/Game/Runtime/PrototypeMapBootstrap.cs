using UnityEngine;

namespace LittleCiv.Runtime
{
    public static class PrototypeMapBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateMapPresenter()
        {
            if (Object.FindAnyObjectByType<PrototypeMapPresenter>() != null)
            {
                return;
            }

            var root = new GameObject("Prototype Map Presenter");
            root.AddComponent<PrototypeMapPresenter>();
        }
    }
}
