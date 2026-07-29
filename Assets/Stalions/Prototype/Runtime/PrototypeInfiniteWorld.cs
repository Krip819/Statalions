using System.Collections.Generic;
using UnityEngine;

namespace Stalions.Prototype
{
    [DisallowMultipleComponent]
    public sealed class PrototypeInfiniteWorld : MonoBehaviour
    {
        public const int ChunkSize = 32;
        public const int ChunkRadius = 2;
        public const int ActiveChunkCount = 25;
        public const int PropsPerChunk = 8;
        private const float MinimumPropSpacing = 2f;
        private const int DecorPlacementAttempts = 32;

        private sealed class StreamChunk
        {
            public readonly List<PrototypeDestructible> Props = new();
            public readonly List<Vector3> PlacementScratch = new(PropsPerChunk);
            public GameObject Root;
            public Vector2Int Coordinate;
        }

        private readonly Dictionary<Vector2Int, StreamChunk> activeChunks = new();
        private readonly List<StreamChunk> allChunks = new();
        private PrototypeGameController controller;
        private Transform followTarget;
        private Vector2Int centerCoordinate;
        private bool initialized;

        public int ActiveChunks => activeChunks.Count;
        public int StreamedProps => allChunks.Count * PropsPerChunk;
        public Vector2Int CenterCoordinate => centerCoordinate;

        public void Initialize(
            PrototypeGameController gameController,
            Transform target)
        {
            controller = gameController;
            followTarget = target;
            centerCoordinate = WorldToChunk(
                followTarget != null ? followTarget.position : Vector3.zero);

            var previousRandomState = Random.state;
            Random.InitState(1941);
            for (var index = 0; index < ActiveChunkCount; index++)
            {
                allChunks.Add(CreateChunk(index));
            }

            Random.state = previousRandomState;

            var chunkIndex = 0;
            for (var z = -ChunkRadius; z <= ChunkRadius; z++)
            {
                for (var x = -ChunkRadius; x <= ChunkRadius; x++)
                {
                    var coordinate = centerCoordinate + new Vector2Int(x, z);
                    var chunk = allChunks[chunkIndex++];
                    AssignChunk(chunk, coordinate);
                    activeChunks.Add(coordinate, chunk);
                }
            }

            initialized = true;
        }

        private void LateUpdate()
        {
            RefreshForCurrentTarget();
        }

        private void RefreshForCurrentTarget()
        {
            if (!initialized || followTarget == null)
            {
                return;
            }

            var nextCenter = WorldToChunk(followTarget.position);
            if (nextCenter == centerCoordinate)
            {
                return;
            }

            centerCoordinate = nextCenter;
            RefreshChunks();
        }

#if UNITY_EDITOR
        public void DebugRefreshNow()
        {
            RefreshForCurrentTarget();
        }
#endif

        public static Vector2Int WorldToChunk(Vector3 worldPosition)
        {
            return new Vector2Int(
                Mathf.RoundToInt(worldPosition.x / ChunkSize),
                Mathf.RoundToInt(worldPosition.z / ChunkSize));
        }

        public static Vector3 ChunkCenter(Vector2Int coordinate)
        {
            return new Vector3(
                coordinate.x * ChunkSize,
                0f,
                coordinate.y * ChunkSize);
        }

        public static Vector3 DecorLocalPosition(
            Vector2Int coordinate,
            int propIndex,
            int attempt = 0)
        {
            var seed = Hash(coordinate, propIndex * 17 + attempt * 101 + 11);
            var x = Mathf.Lerp(-13.5f, 13.5f, Unit(seed));
            var z = Mathf.Lerp(-13.5f, 13.5f, Unit(Hash(seed + 0x9e3779b9u)));
            return new Vector3(x, 0f, z);
        }

        private StreamChunk CreateChunk(int chunkIndex)
        {
            var root = new GameObject($"Stream Chunk Pool {chunkIndex:00}");
            root.transform.SetParent(transform);
            controller.CreateStreamingGroundTile(root.transform, chunkIndex);

            var chunk = new StreamChunk
            {
                Root = root
            };

            for (var propIndex = 0; propIndex < PropsPerChunk; propIndex++)
            {
                var kind = propIndex % 4 == 3
                    ? PrototypeGameController.PropKind.Rock
                    : PrototypeGameController.PropKind.Tree;
                var globalPropIndex = chunkIndex * PropsPerChunk + propIndex;
                chunk.Props.Add(
                    controller.CreateStreamingProp(
                        root.transform,
                        globalPropIndex,
                        kind));
            }

            return chunk;
        }

        private void RefreshChunks()
        {
            var desired = new HashSet<Vector2Int>();
            for (var z = -ChunkRadius; z <= ChunkRadius; z++)
            {
                for (var x = -ChunkRadius; x <= ChunkRadius; x++)
                {
                    desired.Add(centerCoordinate + new Vector2Int(x, z));
                }
            }

            var reusable = new Queue<StreamChunk>();
            var staleCoordinates = new List<Vector2Int>();
            foreach (var pair in activeChunks)
            {
                if (!desired.Contains(pair.Key))
                {
                    staleCoordinates.Add(pair.Key);
                    reusable.Enqueue(pair.Value);
                }
            }

            foreach (var staleCoordinate in staleCoordinates)
            {
                activeChunks.Remove(staleCoordinate);
            }

            for (var z = -ChunkRadius; z <= ChunkRadius; z++)
            {
                for (var x = -ChunkRadius; x <= ChunkRadius; x++)
                {
                    var coordinate = centerCoordinate + new Vector2Int(x, z);
                    if (activeChunks.ContainsKey(coordinate))
                    {
                        continue;
                    }

                    var chunk = reusable.Dequeue();
                    AssignChunk(chunk, coordinate);
                    activeChunks.Add(coordinate, chunk);
                }
            }
        }

        private void AssignChunk(StreamChunk chunk, Vector2Int coordinate)
        {
            chunk.Coordinate = coordinate;
            chunk.Root.name = $"Stream Chunk {coordinate.x},{coordinate.y}";
            chunk.Root.transform.position = ChunkCenter(coordinate);
            var placedPositions = chunk.PlacementScratch;
            placedPositions.Clear();

            for (var propIndex = 0; propIndex < chunk.Props.Count; propIndex++)
            {
                if (!TryFindDecorLocalPosition(
                        chunk,
                        coordinate,
                        propIndex,
                        placedPositions,
                        out var localPosition))
                {
                    chunk.Props[propIndex].gameObject.SetActive(false);
                    continue;
                }

                placedPositions.Add(localPosition);
                var seed = Hash(coordinate, propIndex * 31 + 7);
                var rotation = Unit(seed) * 360f;
                var scale = Mathf.Lerp(
                    0.76f,
                    1.24f,
                    Unit(Hash(seed + 0x85ebca6bu)));
                controller.ReinitializeStreamingProp(
                    chunk.Props[propIndex],
                    localPosition,
                    rotation,
                    scale);
            }
        }

        private bool TryFindDecorLocalPosition(
            StreamChunk chunk,
            Vector2Int coordinate,
            int propIndex,
            List<Vector3> placedPositions,
            out Vector3 localPosition)
        {
            for (var attempt = 0; attempt < DecorPlacementAttempts; attempt++)
            {
                var candidate = DecorLocalPosition(
                    coordinate,
                    propIndex,
                    attempt);
                if (IsValidDecorPosition(
                        chunk,
                        candidate,
                        placedPositions))
                {
                    localPosition = candidate;
                    return true;
                }
            }

            const int fallbackGridSide = 8;
            const int fallbackGridCount = fallbackGridSide * fallbackGridSide;
            var fallbackStart = (int)(
                Hash(coordinate, propIndex * 43 + 19) %
                fallbackGridCount);
            for (var attempt = 0; attempt < fallbackGridCount; attempt++)
            {
                var cell =
                    (fallbackStart + attempt * 17) %
                    fallbackGridCount;
                var xIndex = cell % fallbackGridSide;
                var zIndex = cell / fallbackGridSide;
                var candidate = new Vector3(
                    Mathf.Lerp(
                        -12.6f,
                        12.6f,
                        xIndex / (fallbackGridSide - 1f)),
                    0f,
                    Mathf.Lerp(
                        -12.6f,
                        12.6f,
                        zIndex / (fallbackGridSide - 1f)));
                if (IsValidDecorPosition(
                        chunk,
                        candidate,
                        placedPositions))
                {
                    localPosition = candidate;
                    return true;
                }
            }

            localPosition = Vector3.zero;
            return false;
        }

        private bool IsValidDecorPosition(
            StreamChunk chunk,
            Vector3 localPosition,
            List<Vector3> placedPositions)
        {
            var worldPosition =
                chunk.Root.transform.position + localPosition;
            return !controller.IsStreamingPositionReserved(worldPosition) &&
                   IsSeparatedFromPlacedProps(
                       localPosition,
                       placedPositions);
        }

        private static bool IsSeparatedFromPlacedProps(
            Vector3 candidate,
            List<Vector3> placedPositions)
        {
            var minimumSqrDistance =
                MinimumPropSpacing * MinimumPropSpacing;
            foreach (var placedPosition in placedPositions)
            {
                var offset = candidate - placedPosition;
                if (offset.sqrMagnitude < minimumSqrDistance)
                {
                    return false;
                }
            }

            return true;
        }

        private static uint Hash(Vector2Int coordinate, int salt)
        {
            unchecked
            {
                var value = (uint)coordinate.x * 0x8da6b343u;
                value ^= (uint)coordinate.y * 0xd8163841u;
                value ^= (uint)salt * 0xcb1ab31fu;
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                return value;
            }
        }

        private static uint Hash(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                return value;
            }
        }

        private static float Unit(uint value)
        {
            return (value & 0x00ffffffu) / 16777215f;
        }
    }
}
