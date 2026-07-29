using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Stalions.Prototype.Tests
{
    public sealed class PrototypeModelTests
    {
        [Test]
        public void DefaultSequence_HasEightSlotsAndExpectedCards()
        {
            var sequence = new BeatSequenceModel();

            Assert.That(sequence.Slots.Count, Is.EqualTo(BeatSequenceModel.SlotCount));
            Assert.That(sequence[0], Is.EqualTo(BeatActionType.Ppsh));
            Assert.That(sequence[2], Is.EqualTo(BeatActionType.Rifle));
            Assert.That(sequence[4], Is.EqualTo(BeatActionType.Grenade));
            Assert.That(sequence[6], Is.EqualTo(BeatActionType.Ppsh));
        }

        [Test]
        public void Swap_ChangesOrderWithoutLosingActions()
        {
            var sequence = new BeatSequenceModel();

            sequence.Swap(0, 1);

            Assert.That(sequence[0], Is.EqualTo(BeatActionType.Rest));
            Assert.That(sequence[1], Is.EqualTo(BeatActionType.Ppsh));
        }

        [Test]
        public void TryReplaceFirstRest_InsertsRuntimeUpgrade()
        {
            var sequence = new BeatSequenceModel();

            var inserted = sequence.TryReplaceFirstRest(BeatActionType.Echo);

            Assert.That(inserted, Is.True);
            Assert.That(sequence[1], Is.EqualTo(BeatActionType.Echo));
        }

        [TestCase(BeatActionType.Ppsh, "КОНУС")]
        [TestCase(BeatActionType.Rifle, "ЛИНИЯ")]
        [TestCase(BeatActionType.Grenade, "КРУГ")]
        [TestCase(BeatActionType.Dash, "ПРОРЫВ")]
        public void WeaponPattern_DescribesDirectionalGeometry(
            BeatActionType action,
            string expected)
        {
            Assert.That(BeatActionNames.Pattern(action), Is.EqualTo(expected));
        }

        [TestCase(false, false, 0f)]
        [TestCase(true, false, 2f)]
        [TestCase(true, true, 5f)]
        public void Contribution_MatchesMissionOutcome(
            bool objectiveComplete,
            bool extracted,
            float expected)
        {
            var result = FactionContributionCalculator.Calculate(objectiveComplete, extracted);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void SelectedSlot_CanBeReplacedWithoutChangingOtherBlocks()
        {
            var sequence = new BeatSequenceModel();
            var previousFirst = sequence[0];
            var previousThird = sequence[2];

            sequence[1] = BeatActionType.Dash;

            Assert.That(sequence[0], Is.EqualTo(previousFirst));
            Assert.That(sequence[1], Is.EqualTo(BeatActionType.Dash));
            Assert.That(sequence[2], Is.EqualTo(previousThird));
            Assert.That(sequence.Contains(BeatActionType.Dash), Is.True);
        }

        [Test]
        public void CombatBoundaryGeometry_DetectsExitAndPointsBackInside()
        {
            Assert.That(
                CombatBoundaryGeometry.Contains(
                    new Vector3(69.9f, 0f, 109.9f),
                    70f,
                    110f),
                Is.True);
            Assert.That(
                CombatBoundaryGeometry.Contains(
                    new Vector3(70.1f, 0f, 0f),
                    70f,
                    110f),
                Is.False);

            var closest = CombatBoundaryGeometry.ClosestPointInside(
                new Vector3(80f, 0f, 120f),
                70f,
                110f,
                1.75f);

            Assert.That(closest, Is.EqualTo(new Vector3(68.25f, 0f, 108.25f)));
            Assert.That(
                CombatBoundaryGeometry.DistanceOutside(
                    new Vector3(80f, 0f, 120f),
                    70f,
                    110f),
                Is.EqualTo(Mathf.Sqrt(200f)).Within(0.001f));
        }

        [Test]
        public void CombatBoundaryTimer_UsesHysteresisAndResetsAfterSafeReturn()
        {
            var timer = new CombatBoundaryTimer(8f);

            var expired = timer.Tick(true, false, 2f);

            Assert.That(expired, Is.False);
            Assert.That(timer.Active, Is.True);
            Assert.That(timer.Remaining, Is.EqualTo(6f));

            timer.Tick(false, false, 1f);
            Assert.That(timer.Active, Is.True);
            Assert.That(timer.Remaining, Is.EqualTo(5f));

            timer.Tick(false, true, 0.1f);
            Assert.That(timer.Active, Is.False);
            Assert.That(timer.Remaining, Is.EqualTo(8f));
        }

        [Test]
        public void CombatBoundaryTimer_ExpiresAtZero()
        {
            var timer = new CombatBoundaryTimer(8f);

            var expired = timer.Tick(true, false, 8f);

            Assert.That(expired, Is.True);
            Assert.That(timer.Expired, Is.True);
            Assert.That(timer.Remaining, Is.Zero);
        }

        [Test]
        public void MusicLoop_HasExactEightBeatStereoLength()
        {
            const int sampleRate = 48000;
            const float beatInterval = 0.5f;

            var samples =
                PrototypeMusicGenerator.GenerateStereoLoop(
                    sampleRate,
                    beatInterval);

            Assert.That(
                samples.Length,
                Is.EqualTo(
                    sampleRate *
                    4 *
                    PrototypeMusicGenerator.ChannelCount));
        }

        [Test]
        public void MusicLoop_IsDeterministicFiniteAndHeadroomSafe()
        {
            var first =
                PrototypeMusicGenerator.GenerateStereoLoop(
                    12000,
                    0.5f);
            var second =
                PrototypeMusicGenerator.GenerateStereoLoop(
                    12000,
                    0.5f);

            Assert.That(second, Is.EqualTo(first));
            foreach (var sample in first)
            {
                Assert.That(float.IsNaN(sample), Is.False);
                Assert.That(float.IsInfinity(sample), Is.False);
                Assert.That(sample, Is.InRange(-1f, 1f));
            }

            Assert.That(
                Mathf.Abs(first[0] - first[^2]),
                Is.LessThan(0.03f));
            Assert.That(
                Mathf.Abs(first[1] - first[^1]),
                Is.LessThan(0.03f));
        }

        [Test]
        public void MusicLoop_EveryBeatHasAudibleTransient()
        {
            const int sampleRate = 12000;
            const float beatInterval = 0.5f;
            var samples =
                PrototypeMusicGenerator.GenerateStereoLoop(
                    sampleRate,
                    beatInterval);
            var framesPerBeat =
                Mathf.RoundToInt(sampleRate * beatInterval);
            var transientFrames =
                Mathf.RoundToInt(sampleRate * 0.06f);

            for (var beat = 0;
                 beat < PrototypeMusicGenerator.BeatsPerLoop;
                 beat++)
            {
                var peak = 0f;
                var firstFrame = beat * framesPerBeat;
                for (var offset = 0;
                     offset < transientFrames;
                     offset++)
                {
                    var sampleIndex =
                        (firstFrame + offset) *
                        PrototypeMusicGenerator.ChannelCount;
                    peak = Mathf.Max(
                        peak,
                        Mathf.Abs(samples[sampleIndex]),
                        Mathf.Abs(samples[sampleIndex + 1]));
                }

                Assert.That(
                    peak,
                    Is.GreaterThan(0.05f),
                    $"Beat {beat} has no audible transient");
            }
        }

        [TestCase(9.999, -1L)]
        [TestCase(10.0, 0L)]
        [TestCase(10.4999, 0L)]
        [TestCase(10.5, 1L)]
        [TestCase(13.9999, 7L)]
        [TestCase(14.0, 8L)]
        public void BeatTransport_OrdinalHonorsDspBoundaries(
            double dspTime,
            long expectedOrdinal)
        {
            Assert.That(
                PrototypeBeatTransportMath.OrdinalAt(
                    dspTime,
                    10d,
                    0.5d),
                Is.EqualTo(expectedOrdinal));
        }

        [Test]
        public void TempoMultiplier_KeepsMusicAndWeaponGridAligned()
        {
            const double tempoMultiplier = 1.08d;
            var beatInterval = 0.5d / tempoMultiplier;
            var heardLoopDuration =
                PrototypeMusicGenerator.LoopDuration(0.5f) /
                tempoMultiplier;

            Assert.That(
                heardLoopDuration,
                Is.EqualTo(
                    beatInterval *
                    PrototypeMusicGenerator.BeatsPerLoop)
                    .Within(0.000001d));
            Assert.That(
                PrototypeBeatTransportMath.SlotForOrdinal(
                    8L,
                    BeatSequenceModel.SlotCount),
                Is.EqualTo(0));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void CampaignMap_LabelAnchorsSelectTheirSector(
            int expectedSector)
        {
            var anchor =
                PrototypeCampaignMapLayout
                    .Regions[expectedSector]
                    .LabelPosition;
            var selected = PrototypeCampaignMapLayout.FindSectorIndex(
                anchor);

            Assert.That(selected, Is.EqualTo(expectedSector));
        }

        [Test]
        public void CampaignMap_OutsidePointSelectsNothing()
        {
            var selected = PrototypeCampaignMapLayout.FindSectorIndex(
                PrototypeCampaignMapLayout.ProjectGeographic(
                    -8f,
                    68f));

            Assert.That(selected, Is.EqualTo(-1));
        }

        [Test]
        public void CampaignMap_SharedEdgeUsesStableDisplayOrder()
        {
            var sharedEdgePoint =
                Vector2.Lerp(
                    PrototypeCampaignMapLayout
                        .ProjectGeographic(30.5f, 51.2f),
                    PrototypeCampaignMapLayout
                        .ProjectGeographic(41f, 51.8f),
                    0.5f);

            Assert.That(
                PrototypeCampaignMapLayout.ContainsPoint(
                    PrototypeCampaignMapLayout.Regions[1].Polygon,
                    sharedEdgePoint),
                Is.True);
            Assert.That(
                PrototypeCampaignMapLayout.ContainsPoint(
                    PrototypeCampaignMapLayout.Regions[2].Polygon,
                    sharedEdgePoint),
                Is.True);
            Assert.That(
                PrototypeCampaignMapLayout.FindSectorIndex(sharedEdgePoint),
                Is.EqualTo(1));
        }

        [TestCase(0, 0.47f)]
        [TestCase(1, 0.55f)]
        [TestCase(2, 0.42f)]
        public void CampaignMap_ControlSplitMatchesPolygonArea(
            int regionIndex,
            float expectedSovietFraction)
        {
            var region =
                PrototypeCampaignMapLayout.Regions[regionIndex];
            var scratch = new List<Vector2>();
            var split =
                PrototypeCampaignMapLayout.FindVerticalSplitForRightArea(
                    region.Polygon,
                    expectedSovietFraction,
                    scratch);
            PrototypeCampaignMapLayout.ClipToRight(
                region.Polygon,
                split,
                scratch);

            var actualFraction =
                PrototypeCampaignMapLayout.Area(scratch) /
                PrototypeCampaignMapLayout.Area(region.Polygon);

            Assert.That(
                actualFraction,
                Is.EqualTo(expectedSovietFraction).Within(0.001f));
        }

        [Test]
        public void CampaignMap_AllRenderedPolygonsAreConvex()
        {
            foreach (var backdrop in
                     PrototypeCampaignMapLayout.BackdropRegions)
            {
                Assert.That(
                    PrototypeCampaignMapLayout.IsConvex(
                        backdrop.Polygon),
                    Is.True);
            }

            foreach (var region in
                     PrototypeCampaignMapLayout.Regions)
            {
                Assert.That(
                    PrototypeCampaignMapLayout.IsConvex(
                        region.Polygon),
                    Is.True);
            }
        }

        [Test]
        public void CampaignMap_AllGeometryStaysInsideEuropeBounds()
        {
            var mapBounds =
                PrototypeCampaignMapLayout.MapBounds;
            foreach (var backdrop in
                     PrototypeCampaignMapLayout.BackdropRegions)
            {
                foreach (var point in backdrop.Polygon)
                {
                    Assert.That(
                        mapBounds.Contains(point) ||
                        Mathf.Approximately(point.x, mapBounds.xMax) ||
                        Mathf.Approximately(point.y, mapBounds.yMax),
                        Is.True,
                        $"Backdrop point {point} is outside Europe");
                }
            }

            foreach (var region in
                     PrototypeCampaignMapLayout.Regions)
            {
                foreach (var point in region.Polygon)
                {
                    Assert.That(
                        mapBounds.Contains(point),
                        Is.True,
                        $"Sector point {point} is outside Europe");
                }
            }
        }

        [Test]
        public void CampaignMap_ViewTransformRoundTripsAfterPan()
        {
            var viewport = new Rect(20f, 40f, 320f, 220f);
            var view =
                PrototypeCampaignMapNavigation.CreateView(
                    viewport,
                    new Vector2(34f, 19f),
                    2.4f);
            var worldPoint =
                PrototypeCampaignMapLayout.ProjectGeographic(
                    24f,
                    52f);

            var roundTrip =
                view.GuiToWorld(
                    view.WorldToGui(worldPoint));

            Assert.That(
                Vector2.Distance(worldPoint, roundTrip),
                Is.LessThan(0.00001f));
        }

        [Test]
        public void CampaignMap_PanClampsWithoutShowingEmptySpace()
        {
            var viewport = new Rect(0f, 0f, 300f, 200f);
            var view =
                PrototypeCampaignMapNavigation.CreateView(
                    viewport,
                    new Vector2(-999f, 999f),
                    1.8f);
            var mapBounds =
                PrototypeCampaignMapLayout.MapBounds;

            Assert.That(
                view.VisibleWorld.xMin,
                Is.GreaterThanOrEqualTo(mapBounds.xMin - 0.0001f));
            Assert.That(
                view.VisibleWorld.xMax,
                Is.LessThanOrEqualTo(mapBounds.xMax + 0.0001f));
            Assert.That(
                view.VisibleWorld.yMin,
                Is.GreaterThanOrEqualTo(mapBounds.yMin - 0.0001f));
            Assert.That(
                view.VisibleWorld.yMax,
                Is.LessThanOrEqualTo(mapBounds.yMax + 0.0001f));
        }

        [Test]
        public void CampaignMap_ZoomAroundPointerPreservesWorldAnchor()
        {
            var viewport = new Rect(10f, 20f, 300f, 210f);
            var anchorGui = new Vector2(230f, 115f);
            var before =
                PrototypeCampaignMapNavigation.CreateView(
                    viewport,
                    new Vector2(34f, 19f),
                    1.2f);
            var worldAnchor =
                before.GuiToWorld(anchorGui);
            var nextCenter =
                PrototypeCampaignMapNavigation.CenterForZoomAround(
                    before,
                    2.4f,
                    anchorGui);
            var after =
                PrototypeCampaignMapNavigation.CreateView(
                    viewport,
                    nextCenter,
                    2.4f);

            Assert.That(
                Vector2.Distance(
                    anchorGui,
                    after.WorldToGui(worldAnchor)),
                Is.LessThan(0.001f));
        }

        [Test]
        public void CampaignMap_DragThresholdSeparatesTapFromPan()
        {
            Assert.That(
                PrototypeCampaignMapNavigation.IsDrag(9f, 10f),
                Is.False);
            Assert.That(
                PrototypeCampaignMapNavigation.IsDrag(12f, 10f),
                Is.True);
        }

        [Test]
        public void CampaignMap_ClippingKeepsVerticesInsideViewport()
        {
            var output = new List<Vector2>();
            var scratch = new List<Vector2>();
            var clipRect = new Rect(42f, 15f, 6f, 5f);

            PrototypeCampaignMapLayout.ClipToRect(
                PrototypeCampaignMapLayout.Regions[0].Polygon,
                clipRect,
                output,
                scratch);

            Assert.That(output.Count, Is.GreaterThanOrEqualTo(3));
            foreach (var point in output)
            {
                Assert.That(
                    point.x,
                    Is.InRange(
                        clipRect.xMin - 0.0001f,
                        clipRect.xMax + 0.0001f));
                Assert.That(
                    point.y,
                    Is.InRange(
                        clipRect.yMin - 0.0001f,
                        clipRect.yMax + 0.0001f));
            }
        }
    }
}
