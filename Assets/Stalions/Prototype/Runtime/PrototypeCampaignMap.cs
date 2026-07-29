using System.Collections.Generic;
using UnityEngine;

namespace Stalions.Prototype
{
    public enum CampaignTerritory
    {
        German,
        Soviet,
        Allied,
        Neutral
    }

    public sealed class CampaignMapBackdropRegion
    {
        public CampaignMapBackdropRegion(
            CampaignTerritory territory,
            params Vector2[] polygon)
        {
            Territory = territory;
            Polygon = polygon;
        }

        public CampaignTerritory Territory { get; }
        public IReadOnlyList<Vector2> Polygon { get; }
    }

    public sealed class CampaignMapRegionDefinition
    {
        public CampaignMapRegionDefinition(
            int sectorIndex,
            Vector2 labelPosition,
            params Vector2[] polygon)
        {
            SectorIndex = sectorIndex;
            LabelPosition = labelPosition;
            Polygon = polygon;
        }

        public int SectorIndex { get; }
        public Vector2 LabelPosition { get; }
        public IReadOnlyList<Vector2> Polygon { get; }
    }

    public sealed class CampaignMapLabelDefinition
    {
        public CampaignMapLabelDefinition(
            string text,
            Vector2 position,
            float minimumZoom = 1f)
        {
            Text = text;
            Position = position;
            MinimumZoom = minimumZoom;
        }

        public string Text { get; }
        public Vector2 Position { get; }
        public float MinimumZoom { get; }
    }

    public readonly struct CampaignMapView
    {
        public CampaignMapView(
            Rect viewport,
            Vector2 centerWorld,
            float zoom,
            float scale,
            Rect visibleWorld)
        {
            Viewport = viewport;
            CenterWorld = centerWorld;
            Zoom = zoom;
            Scale = scale;
            VisibleWorld = visibleWorld;
        }

        public Rect Viewport { get; }
        public Vector2 CenterWorld { get; }
        public float Zoom { get; }
        public float Scale { get; }
        public Rect VisibleWorld { get; }

        public Vector2 WorldToGui(Vector2 worldPoint)
        {
            return Viewport.center +
                   (worldPoint - CenterWorld) * Scale;
        }

        public Vector2 GuiToWorld(Vector2 guiPoint)
        {
            return CenterWorld +
                   (guiPoint - Viewport.center) /
                   Mathf.Max(0.0001f, Scale);
        }
    }

    public static class PrototypeCampaignMapNavigation
    {
        public const float MinimumZoom = 1f;
        public const float MaximumZoom = 3.25f;

        public static CampaignMapView CreateView(
            Rect viewport,
            Vector2 desiredCenter,
            float desiredZoom)
        {
            var zoom = Mathf.Clamp(
                desiredZoom,
                MinimumZoom,
                MaximumZoom);
            var scale =
                Mathf.Max(0.0001f, viewport.height) /
                PrototypeCampaignMapLayout.MapBounds.height *
                zoom;
            var halfVisible = new Vector2(
                viewport.width / (2f * scale),
                viewport.height / (2f * scale));
            var center = ClampCenter(desiredCenter, halfVisible);
            var visibleWorld = Rect.MinMaxRect(
                center.x - halfVisible.x,
                center.y - halfVisible.y,
                center.x + halfVisible.x,
                center.y + halfVisible.y);
            return new CampaignMapView(
                viewport,
                center,
                zoom,
                scale,
                visibleWorld);
        }

        public static Vector2 PanCenter(
            CampaignMapView view,
            Vector2 guiDelta)
        {
            var desiredCenter =
                view.CenterWorld -
                guiDelta / Mathf.Max(0.0001f, view.Scale);
            return ClampCenter(
                desiredCenter,
                view.VisibleWorld.size * 0.5f);
        }

        public static Vector2 CenterForZoomAround(
            CampaignMapView currentView,
            float newZoom,
            Vector2 guiAnchor)
        {
            var worldAnchor = currentView.GuiToWorld(guiAnchor);
            var zoom = Mathf.Clamp(
                newZoom,
                MinimumZoom,
                MaximumZoom);
            var newScale =
                Mathf.Max(0.0001f, currentView.Viewport.height) /
                PrototypeCampaignMapLayout.MapBounds.height *
                zoom;
            var desiredCenter =
                worldAnchor -
                (guiAnchor - currentView.Viewport.center) /
                newScale;
            var halfVisible = new Vector2(
                currentView.Viewport.width / (2f * newScale),
                currentView.Viewport.height / (2f * newScale));
            return ClampCenter(desiredCenter, halfVisible);
        }

        public static bool IsDrag(
            float maximumDistance,
            float threshold)
        {
            return maximumDistance >
                   Mathf.Max(0f, threshold);
        }

        private static Vector2 ClampCenter(
            Vector2 desiredCenter,
            Vector2 halfVisible)
        {
            var bounds = PrototypeCampaignMapLayout.MapBounds;
            return new Vector2(
                ClampAxis(
                    desiredCenter.x,
                    halfVisible.x,
                    bounds.xMin,
                    bounds.xMax,
                    bounds.center.x),
                ClampAxis(
                    desiredCenter.y,
                    halfVisible.y,
                    bounds.yMin,
                    bounds.yMax,
                    bounds.center.y));
        }

        private static float ClampAxis(
            float value,
            float halfVisible,
            float minimum,
            float maximum,
            float fallbackCenter)
        {
            if (halfVisible * 2f >= maximum - minimum)
            {
                return fallbackCenter;
            }

            return Mathf.Clamp(
                value,
                minimum + halfVisible,
                maximum - halfVisible);
        }
    }

    public static class PrototypeCampaignMapLayout
    {
        public static readonly Rect MapBounds =
            new(0f, 0f, 60f, 38f);

        public static readonly Vector2 DefaultCenter =
            ProjectGeographic(25f, 53f);

        private static readonly CampaignMapBackdropRegion[]
            BackdropRegionData =
            {
                // Atlantic islands and the British Isles.
                Region(
                    CampaignTerritory.Allied,
                    (-9f, 67.5f),
                    (-3f, 67.5f),
                    (0f, 64f),
                    (-7f, 63f)),
                Region(
                    CampaignTerritory.Allied,
                    (-10.5f, 55.2f),
                    (-6f, 55.3f),
                    (-5.5f, 51.5f),
                    (-9.8f, 51.3f)),
                Region(
                    CampaignTerritory.Allied,
                    (-6f, 58.5f),
                    (0f, 58f),
                    (-1f, 54.5f),
                    (-5.5f, 55f)),
                Region(
                    CampaignTerritory.Allied,
                    (-5.5f, 55f),
                    (-1f, 54.5f),
                    (1.8f, 51f),
                    (-5f, 50f)),

                // Iberia and France.
                Region(
                    CampaignTerritory.Neutral,
                    (-9.5f, 42.2f),
                    (-6.8f, 41.8f),
                    (-7.2f, 36.8f),
                    (-9.2f, 37f)),
                Region(
                    CampaignTerritory.Neutral,
                    (-9f, 43.2f),
                    (3f, 43.3f),
                    (3.2f, 39f),
                    (-1f, 36.2f),
                    (-7.2f, 36.8f)),
                Region(
                    CampaignTerritory.German,
                    (-5f, 50f),
                    (2f, 50.8f),
                    (2.5f, 46f),
                    (-1f, 43.3f),
                    (-5f, 45f)),
                Region(
                    CampaignTerritory.German,
                    (2f, 50.8f),
                    (8f, 49f),
                    (7.5f, 45f),
                    (3f, 43.3f),
                    (2.5f, 46f)),

                // Low Countries, Germany and Denmark.
                Region(
                    CampaignTerritory.German,
                    (2.5f, 53f),
                    (7.5f, 53f),
                    (7.5f, 49.5f),
                    (2f, 50.8f)),
                Region(
                    CampaignTerritory.German,
                    (7.5f, 54.8f),
                    (14.7f, 54.8f),
                    (14.5f, 51f),
                    (7.5f, 49.5f)),
                Region(
                    CampaignTerritory.German,
                    (7.5f, 49.5f),
                    (14.5f, 51f),
                    (14f, 47.5f),
                    (8f, 47f)),
                Region(
                    CampaignTerritory.German,
                    (8f, 57.5f),
                    (12.8f, 57.5f),
                    (14.7f, 54.8f),
                    (9f, 54.8f)),

                // Scandinavia and Finland.
                Region(
                    CampaignTerritory.Neutral,
                    (5f, 62f),
                    (10f, 71f),
                    (18f, 70f),
                    (12.8f, 60f)),
                Region(
                    CampaignTerritory.Neutral,
                    (12f, 69f),
                    (20f, 69f),
                    (24f, 62f),
                    (16f, 61f)),
                Region(
                    CampaignTerritory.Neutral,
                    (16f, 61f),
                    (24f, 62f),
                    (23f, 56f),
                    (15f, 55f)),
                Region(
                    CampaignTerritory.German,
                    (20f, 69f),
                    (29f, 70f),
                    (32f, 64f),
                    (24f, 62f)),
                Region(
                    CampaignTerritory.German,
                    (24f, 62f),
                    (32f, 64f),
                    (31f, 59f),
                    (24f, 59f)),

                // Central Europe.
                Region(
                    CampaignTerritory.German,
                    (14.5f, 54.8f),
                    (24.5f, 55f),
                    (24.5f, 50f),
                    (14f, 47.5f)),
                Region(
                    CampaignTerritory.German,
                    (21f, 59f),
                    (29f, 59f),
                    (28f, 55f),
                    (24f, 55f)),
                Region(
                    CampaignTerritory.Neutral,
                    (5.8f, 47.8f),
                    (10.5f, 47.8f),
                    (10f, 45.7f),
                    (6f, 45.7f)),
                Region(
                    CampaignTerritory.German,
                    (10.5f, 49f),
                    (17f, 49f),
                    (16f, 46f),
                    (10f, 45.7f)),
                Region(
                    CampaignTerritory.German,
                    (12f, 51f),
                    (22f, 50.5f),
                    (18f, 48f),
                    (13f, 48f)),

                // Italy and the Balkans.
                Region(
                    CampaignTerritory.German,
                    (7.5f, 47f),
                    (14f, 47.5f),
                    (13f, 44f),
                    (9f, 43f)),
                Region(
                    CampaignTerritory.German,
                    (9f, 44f),
                    (13f, 44f),
                    (16f, 39f),
                    (13f, 38f)),
                Region(
                    CampaignTerritory.German,
                    (11.5f, 38.5f),
                    (15f, 38.7f),
                    (14f, 36.5f),
                    (11f, 37f)),
                Region(
                    CampaignTerritory.German,
                    (16f, 48.5f),
                    (23f, 48.5f),
                    (22f, 45.5f),
                    (16f, 46f)),
                Region(
                    CampaignTerritory.German,
                    (13f, 46f),
                    (22f, 45.5f),
                    (22f, 41.5f),
                    (17f, 41f),
                    (13f, 43f)),
                Region(
                    CampaignTerritory.German,
                    (22f, 48.5f),
                    (30f, 48.5f),
                    (29f, 43.5f),
                    (23f, 44f)),
                Region(
                    CampaignTerritory.German,
                    (22f, 44f),
                    (29f, 43.5f),
                    (28f, 41f),
                    (22f, 41.5f)),
                Region(
                    CampaignTerritory.German,
                    (19f, 41.5f),
                    (24f, 41.5f),
                    (26f, 35f),
                    (21f, 36f)),
                Region(
                    CampaignTerritory.Neutral,
                    (26f, 41.5f),
                    (42f, 42f),
                    (44f, 38f),
                    (30f, 36f),
                    (26f, 38f)),

                // Belarus, Ukraine and the western USSR.
                Region(
                    CampaignTerritory.Soviet,
                    (24f, 56.5f),
                    (32f, 58f),
                    (34f, 52f),
                    (24.5f, 50f)),
                Region(
                    CampaignTerritory.Soviet,
                    (24f, 50f),
                    (40f, 51f),
                    (40f, 44f),
                    (30f, 44f),
                    (22f, 48f)),
                Region(
                    CampaignTerritory.Soviet,
                    (29f, 70f),
                    (48f, 68f),
                    (48f, 58f),
                    (32f, 58f)),
                Region(
                    CampaignTerritory.Soviet,
                    (28f, 59f),
                    (48f, 58f),
                    (48f, 51f),
                    (40f, 51f),
                    (32f, 52f)),
                Region(
                    CampaignTerritory.Soviet,
                    (34f, 52f),
                    (48f, 51f),
                    (48f, 43f),
                    (40f, 44f)),

                // A thin North-African rim keeps the full Europe view grounded.
                Region(
                    CampaignTerritory.Neutral,
                    (-10f, 36f),
                    (-1f, 36f),
                    (0f, 34f),
                    (-10f, 34f)),
                Region(
                    CampaignTerritory.Neutral,
                    (-1f, 36f),
                    (9f, 37f),
                    (12f, 34f),
                    (0f, 34f)),
                Region(
                    CampaignTerritory.Neutral,
                    (9f, 37f),
                    (12f, 37f),
                    (12f, 34f)),
                Region(
                    CampaignTerritory.Neutral,
                    (12f, 37f),
                    (25f, 37f),
                    (25f, 34f),
                    (12f, 34f))
            };

        private static readonly CampaignMapRegionDefinition[] RegionData =
        {
            new(
                0,
                ProjectGeographic(33.5f, 56.3f),
                ProjectGeographic(27.5f, 57.5f),
                ProjectGeographic(40.5f, 57.5f),
                ProjectGeographic(40.5f, 54.1f),
                ProjectGeographic(29f, 53.4f),
                ProjectGeographic(26.7f, 55.5f)),
            new(
                1,
                ProjectGeographic(37f, 52.7f),
                ProjectGeographic(29f, 54f),
                ProjectGeographic(40.5f, 54.2f),
                ProjectGeographic(41f, 51.8f),
                ProjectGeographic(30.5f, 51.2f),
                ProjectGeographic(27.7f, 52.4f)),
            new(
                2,
                ProjectGeographic(34.5f, 48.8f),
                ProjectGeographic(30.5f, 51.2f),
                ProjectGeographic(41f, 51.8f),
                ProjectGeographic(43f, 48.3f),
                ProjectGeographic(33f, 47.2f),
                ProjectGeographic(29f, 49.2f))
        };

        private static readonly CampaignMapLabelDefinition[] LabelData =
        {
            Label("БРИТАНИЯ", -3f, 54f, 1.15f),
            Label("ФРАНЦИЯ", 2f, 47f, 1.12f),
            Label("ИСПАНИЯ", -3f, 40f, 1.2f),
            Label("ГЕРМАНИЯ", 10.5f, 51.5f, 1.05f),
            Label("ИТАЛИЯ", 12f, 43f, 1.35f),
            Label("ПОЛЬША", 19.5f, 52.5f, 1.2f),
            Label("СКАНДИНАВИЯ", 18f, 64.5f, 1.05f),
            Label("БАЛКАНЫ", 22f, 44.2f, 1.35f),
            Label("УКРАИНА", 33f, 47f, 1.1f),
            Label("СССР", 40f, 61f, 1f)
        };

        public static IReadOnlyList<CampaignMapBackdropRegion>
            BackdropRegions => BackdropRegionData;

        public static IReadOnlyList<CampaignMapRegionDefinition>
            Regions => RegionData;

        public static IReadOnlyList<CampaignMapLabelDefinition>
            Labels => LabelData;

        public static Vector2 ProjectGeographic(
            float longitude,
            float latitude)
        {
            return new Vector2(
                longitude + 12f,
                72f - latitude);
        }

        public static int FindSectorIndex(Vector2 worldPoint)
        {
            foreach (var region in RegionData)
            {
                if (ContainsPoint(region.Polygon, worldPoint))
                {
                    return region.SectorIndex;
                }
            }

            return -1;
        }

        public static bool ContainsPoint(
            IReadOnlyList<Vector2> polygon,
            Vector2 point)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }

            var inside = false;
            for (var current = 0; current < polygon.Count; current++)
            {
                var previous = current == 0
                    ? polygon.Count - 1
                    : current - 1;
                var first = polygon[previous];
                var second = polygon[current];

                if (PointOnSegment(first, second, point))
                {
                    return true;
                }

                var crossesScanline =
                    (first.y > point.y) !=
                    (second.y > point.y);
                if (!crossesScanline)
                {
                    continue;
                }

                var crossingX =
                    (second.x - first.x) *
                    (point.y - first.y) /
                    (second.y - first.y) +
                    first.x;
                if (point.x < crossingX)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        public static void ClipToRight(
            IReadOnlyList<Vector2> polygon,
            float minimumX,
            List<Vector2> output)
        {
            output.Clear();
            if (polygon == null || polygon.Count < 3)
            {
                return;
            }

            var previous = polygon[polygon.Count - 1];
            var previousInside = previous.x >= minimumX;
            for (var index = 0; index < polygon.Count; index++)
            {
                var current = polygon[index];
                var currentInside = current.x >= minimumX;

                if (currentInside != previousInside)
                {
                    var denominator =
                        current.x - previous.x;
                    var interpolation =
                        Mathf.Abs(denominator) <= 0.00001f
                            ? 0f
                            : (minimumX - previous.x) /
                              denominator;
                    output.Add(new Vector2(
                        minimumX,
                        Mathf.Lerp(
                            previous.y,
                            current.y,
                            interpolation)));
                }

                if (currentInside)
                {
                    output.Add(current);
                }

                previous = current;
                previousInside = currentInside;
            }
        }

        public static void ClipToRect(
            IReadOnlyList<Vector2> polygon,
            Rect clipRect,
            List<Vector2> output,
            List<Vector2> scratch)
        {
            output.Clear();
            scratch.Clear();
            if (polygon == null || polygon.Count < 3)
            {
                return;
            }

            for (var index = 0; index < polygon.Count; index++)
            {
                output.Add(polygon[index]);
            }

            for (var edge = 0; edge < 4; edge++)
            {
                scratch.Clear();
                ClipAgainstRectEdge(
                    output,
                    clipRect,
                    edge,
                    scratch);
                output.Clear();
                output.AddRange(scratch);
                if (output.Count < 3)
                {
                    output.Clear();
                    return;
                }
            }
        }

        public static bool TryClipSegmentToRect(
            Vector2 start,
            Vector2 end,
            Rect rect,
            out Vector2 clippedStart,
            out Vector2 clippedEnd)
        {
            var delta = end - start;
            var minimum = 0f;
            var maximum = 1f;
            if (!ClipLineParameter(
                    -delta.x,
                    start.x - rect.xMin,
                    ref minimum,
                    ref maximum) ||
                !ClipLineParameter(
                    delta.x,
                    rect.xMax - start.x,
                    ref minimum,
                    ref maximum) ||
                !ClipLineParameter(
                    -delta.y,
                    start.y - rect.yMin,
                    ref minimum,
                    ref maximum) ||
                !ClipLineParameter(
                    delta.y,
                    rect.yMax - start.y,
                    ref minimum,
                    ref maximum))
            {
                clippedStart = default;
                clippedEnd = default;
                return false;
            }

            clippedStart = start + delta * minimum;
            clippedEnd = start + delta * maximum;
            return true;
        }

        public static Rect Bounds(IReadOnlyList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count == 0)
            {
                return default;
            }

            var minimum = polygon[0];
            var maximum = polygon[0];
            for (var index = 1; index < polygon.Count; index++)
            {
                minimum = Vector2.Min(
                    minimum,
                    polygon[index]);
                maximum = Vector2.Max(
                    maximum,
                    polygon[index]);
            }

            return Rect.MinMaxRect(
                minimum.x,
                minimum.y,
                maximum.x,
                maximum.y);
        }

        public static float Area(
            IReadOnlyList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return 0f;
            }

            var doubledArea = 0f;
            for (var index = 0; index < polygon.Count; index++)
            {
                var first = polygon[index];
                var second =
                    polygon[(index + 1) % polygon.Count];
                doubledArea +=
                    first.x * second.y -
                    second.x * first.y;
            }

            return Mathf.Abs(doubledArea) * 0.5f;
        }

        public static bool IsConvex(
            IReadOnlyList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }

            var expectedSign = 0f;
            for (var index = 0; index < polygon.Count; index++)
            {
                var first = polygon[index];
                var second =
                    polygon[(index + 1) % polygon.Count];
                var third =
                    polygon[(index + 2) % polygon.Count];
                var firstEdge = second - first;
                var secondEdge = third - second;
                var cross =
                    firstEdge.x * secondEdge.y -
                    firstEdge.y * secondEdge.x;
                if (Mathf.Abs(cross) <= 0.00001f)
                {
                    continue;
                }

                if (Mathf.Approximately(expectedSign, 0f))
                {
                    expectedSign = Mathf.Sign(cross);
                    continue;
                }

                if (Mathf.Sign(cross) != expectedSign)
                {
                    return false;
                }
            }

            return !Mathf.Approximately(expectedSign, 0f);
        }

        public static float FindVerticalSplitForRightArea(
            IReadOnlyList<Vector2> polygon,
            float desiredRightFraction,
            List<Vector2> scratch)
        {
            var bounds = Bounds(polygon);
            var totalArea = Area(polygon);
            if (totalArea <= 0.00001f)
            {
                return bounds.center.x;
            }

            desiredRightFraction =
                Mathf.Clamp01(desiredRightFraction);
            var minimum = bounds.xMin;
            var maximum = bounds.xMax;
            for (var iteration = 0; iteration < 18; iteration++)
            {
                var candidate =
                    (minimum + maximum) * 0.5f;
                ClipToRight(polygon, candidate, scratch);
                var actualRightFraction =
                    Area(scratch) / totalArea;
                if (actualRightFraction >
                    desiredRightFraction)
                {
                    minimum = candidate;
                }
                else
                {
                    maximum = candidate;
                }
            }

            return (minimum + maximum) * 0.5f;
        }

        public static bool TryGetVerticalSpan(
            IReadOnlyList<Vector2> polygon,
            float x,
            out float minimumY,
            out float maximumY)
        {
            minimumY = float.PositiveInfinity;
            maximumY = float.NegativeInfinity;
            if (polygon == null || polygon.Count < 2)
            {
                return false;
            }

            var intersections = 0;
            for (var index = 0; index < polygon.Count; index++)
            {
                var first = polygon[index];
                var second =
                    polygon[(index + 1) % polygon.Count];
                var minimumX = Mathf.Min(first.x, second.x);
                var maximumX = Mathf.Max(first.x, second.x);
                if (x < minimumX - 0.00001f ||
                    x > maximumX + 0.00001f)
                {
                    continue;
                }

                var deltaX = second.x - first.x;
                if (Mathf.Abs(deltaX) <= 0.00001f)
                {
                    minimumY = Mathf.Min(
                        minimumY,
                        first.y,
                        second.y);
                    maximumY = Mathf.Max(
                        maximumY,
                        first.y,
                        second.y);
                    intersections += 2;
                    continue;
                }

                var interpolation =
                    Mathf.Clamp01((x - first.x) / deltaX);
                var y = Mathf.Lerp(
                    first.y,
                    second.y,
                    interpolation);
                minimumY = Mathf.Min(minimumY, y);
                maximumY = Mathf.Max(maximumY, y);
                intersections++;
            }

            return intersections >= 2 &&
                   minimumY <= maximumY;
        }

        private static CampaignMapBackdropRegion Region(
            CampaignTerritory territory,
            params (float longitude, float latitude)[]
                coordinates)
        {
            var polygon = new Vector2[coordinates.Length];
            for (var index = 0;
                 index < coordinates.Length;
                 index++)
            {
                polygon[index] = ProjectGeographic(
                    coordinates[index].longitude,
                    coordinates[index].latitude);
            }

            return new CampaignMapBackdropRegion(
                territory,
                polygon);
        }

        private static CampaignMapLabelDefinition Label(
            string text,
            float longitude,
            float latitude,
            float minimumZoom)
        {
            return new CampaignMapLabelDefinition(
                text,
                ProjectGeographic(longitude, latitude),
                minimumZoom);
        }

        private static bool PointOnSegment(
            Vector2 first,
            Vector2 second,
            Vector2 point)
        {
            var segment = second - first;
            var pointOffset = point - first;
            var cross =
                segment.x * pointOffset.y -
                segment.y * pointOffset.x;
            if (Mathf.Abs(cross) > 0.00001f)
            {
                return false;
            }

            var dot = Vector2.Dot(pointOffset, segment);
            return dot >= -0.00001f &&
                   dot <= segment.sqrMagnitude + 0.00001f;
        }

        private static void ClipAgainstRectEdge(
            IReadOnlyList<Vector2> input,
            Rect rect,
            int edge,
            List<Vector2> output)
        {
            if (input.Count == 0)
            {
                return;
            }

            var previous = input[input.Count - 1];
            var previousInside =
                IsInsideRectEdge(previous, rect, edge);
            for (var index = 0; index < input.Count; index++)
            {
                var current = input[index];
                var currentInside =
                    IsInsideRectEdge(current, rect, edge);
                if (currentInside != previousInside)
                {
                    output.Add(RectEdgeIntersection(
                        previous,
                        current,
                        rect,
                        edge));
                }

                if (currentInside)
                {
                    output.Add(current);
                }

                previous = current;
                previousInside = currentInside;
            }
        }

        private static bool IsInsideRectEdge(
            Vector2 point,
            Rect rect,
            int edge)
        {
            return edge switch
            {
                0 => point.x >= rect.xMin,
                1 => point.x <= rect.xMax,
                2 => point.y >= rect.yMin,
                _ => point.y <= rect.yMax
            };
        }

        private static Vector2 RectEdgeIntersection(
            Vector2 start,
            Vector2 end,
            Rect rect,
            int edge)
        {
            if (edge <= 1)
            {
                var x = edge == 0
                    ? rect.xMin
                    : rect.xMax;
                var delta = end.x - start.x;
                var interpolation =
                    Mathf.Abs(delta) <= 0.00001f
                        ? 0f
                        : (x - start.x) / delta;
                return new Vector2(
                    x,
                    Mathf.Lerp(
                        start.y,
                        end.y,
                        interpolation));
            }

            var y = edge == 2
                ? rect.yMin
                : rect.yMax;
            var yDelta = end.y - start.y;
            var yInterpolation =
                Mathf.Abs(yDelta) <= 0.00001f
                    ? 0f
                    : (y - start.y) / yDelta;
            return new Vector2(
                Mathf.Lerp(
                    start.x,
                    end.x,
                    yInterpolation),
                y);
        }

        private static bool ClipLineParameter(
            float denominator,
            float numerator,
            ref float minimum,
            ref float maximum)
        {
            if (Mathf.Abs(denominator) <= 0.00001f)
            {
                return numerator >= 0f;
            }

            var ratio = numerator / denominator;
            if (denominator < 0f)
            {
                if (ratio > maximum)
                {
                    return false;
                }

                minimum = Mathf.Max(minimum, ratio);
            }
            else
            {
                if (ratio < minimum)
                {
                    return false;
                }

                maximum = Mathf.Min(maximum, ratio);
            }

            return true;
        }
    }
}
