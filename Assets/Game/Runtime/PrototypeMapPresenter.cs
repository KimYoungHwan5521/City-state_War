using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using GameEntityId = LittleCiv.Core.EntityId;

namespace LittleCiv.Runtime
{
    public sealed class PrototypeMapPresenter : MonoBehaviour
    {
        private const float HexRadius = 0.92f;
        private const float ViewSpacing = 15f;

        private readonly List<GameObject> spawnedViews = new List<GameObject>();
        private GameState state;
        private int focusedCityIndex;
        private GameEntityId selectedTileId;
        private Material buildableMaterial;
        private Material boundaryMaterial;
        private Material governmentMaterial;
        private Material selectedMaterial;

        private void Start()
        {
            state = PrototypeMatchFactory.Create(20260831);
            CreateMaterials();
            EnsureCamera();
            ShowCities(new[] { state.Cities[0].Id });
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                focusedCityIndex = (focusedCityIndex + 1) % state.Cities.Count;
                selectedTileId = default(GameEntityId);
                ShowCities(new[] { state.Cities[focusedCityIndex].Id });
            }
        }

        public void SelectTile(GameEntityId tileId)
        {
            selectedTileId = tileId;
            var tile = state.Tiles.Find(item => item.Id == tileId);
            if (tile == null || tile.VisibleCityIds == null || tile.VisibleCityIds.Count <= 1)
            {
                ShowCities(new[] { state.Cities[focusedCityIndex].Id });
                return;
            }

            ShowCities(tile.VisibleCityIds);
        }

        private void ShowCities(IEnumerable<GameEntityId> cityIds)
        {
            foreach (var view in spawnedViews)
            {
                Destroy(view);
            }
            spawnedViews.Clear();

            var ids = cityIds.Distinct().OrderBy(id => id.Value).ToList();
            var centerOffset = (ids.Count - 1) * ViewSpacing * 0.5f;
            for (var index = 0; index < ids.Count; index++)
            {
                CreateCityView(ids[index], new Vector3((index * ViewSpacing) - centerOffset, 0f, 0f));
            }

            var camera = Camera.main;
            if (camera != null)
            {
                camera.transform.position = new Vector3(0f, 18f, 0f);
                camera.orthographicSize = ids.Count == 1 ? 9f : ids.Count == 2 ? 13f : 19f;
            }
        }

        private void CreateCityView(GameEntityId cityId, Vector3 offset)
        {
            var city = state.Cities.Find(item => item.Id == cityId);
            var view = state.MapTopology.FindView(cityId);
            var root = new GameObject($"City {city.Name}");
            root.transform.position = offset;
            spawnedViews.Add(root);

            foreach (var placement in view.Tiles)
            {
                var tile = state.Tiles.Find(item => item.Id == placement.TileId);
                var local = AxialToWorld(new HexCoord(placement.LocalQ, placement.LocalR));
                var tileObject = new GameObject($"Tile {placement.LocalQ},{placement.LocalR} [{placement.TileId}]");
                tileObject.transform.SetParent(root.transform, false);
                tileObject.transform.localPosition = local;
                var filter = tileObject.AddComponent<MeshFilter>();
                filter.sharedMesh = CreateHexMesh();
                var renderer = tileObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = ResolveMaterial(placement, tile);
                var collider = tileObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                tileObject.AddComponent<PrototypeHexTileView>().Initialize(this, placement.TileId);
            }
        }

        private Material ResolveMaterial(CityTilePlacement placement, TileState tile)
        {
            if (tile.Id == selectedTileId)
            {
                return selectedMaterial;
            }
            if (placement.LocalQ == 0 && placement.LocalR == 0)
            {
                return governmentMaterial;
            }
            return placement.IsBuildable ? buildableMaterial : boundaryMaterial;
        }

        private static Vector3 AxialToWorld(HexCoord coord)
        {
            var x = Mathf.Sqrt(3f) * (coord.Q + (coord.R * 0.5f));
            var z = 1.5f * coord.R;
            return new Vector3(x, 0f, z);
        }

        private static Mesh CreateHexMesh()
        {
            var vertices = new Vector3[7];
            vertices[0] = Vector3.zero;
            for (var index = 0; index < 6; index++)
            {
                var angle = Mathf.Deg2Rad * ((60f * index) + 30f);
                vertices[index + 1] = new Vector3(Mathf.Cos(angle) * HexRadius, 0f, Mathf.Sin(angle) * HexRadius);
            }

            var triangles = new int[18];
            for (var index = 0; index < 6; index++)
            {
                triangles[index * 3] = 0;
                triangles[(index * 3) + 1] = ((index + 1) % 6) + 1;
                triangles[(index * 3) + 2] = index + 1;
            }

            var mesh = new Mesh { name = "Prototype Hex" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void CreateMaterials()
        {
            buildableMaterial = CreateMaterial(new Color(0.23f, 0.45f, 0.28f));
            boundaryMaterial = CreateMaterial(new Color(0.35f, 0.38f, 0.42f));
            governmentMaterial = CreateMaterial(new Color(0.80f, 0.60f, 0.18f));
            selectedMaterial = CreateMaterial(new Color(0.20f, 0.72f, 0.92f));
        }

        private static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader);
            material.color = color;
            return material;
        }

        private static void EnsureCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
            }

            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 18f, 0f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            if (camera.GetComponent<PrototypeMapCamera>() == null)
            {
                camera.gameObject.AddComponent<PrototypeMapCamera>();
            }
        }

        private void OnGUI()
        {
            if (state == null)
            {
                return;
            }

            var city = state.Cities[focusedCityIndex];
            GUI.Box(new Rect(16f, 16f, 300f, 76f), string.Empty);
            GUI.Label(new Rect(28f, 25f, 270f, 22f), $"City {city.Name}  World ({city.WorldQ}, {city.WorldR})");
            GUI.Label(new Rect(28f, 47f, 270f, 22f), "Tab: next city | WASD: pan | Wheel: zoom");
            GUI.Label(new Rect(28f, 68f, 270f, 22f), "Select a boundary tile to show linked cities");
        }
    }
}
