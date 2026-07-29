using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Stalions.Prototype
{
    public sealed class PrototypeGameController : MonoBehaviour
    {
        private const float ArenaHalfWidth = 70f;
        private const float ArenaHalfHeight = 110f;
        private const float BaseBeatInterval = 0.5f;
        private const float MissionTimeLimit = 420f;
        private const float ExtractionWaitDuration = 18f;
        private const double BeatScheduleLookAhead = 0.22d;
        private const double BeatResumeDelay = 0.22d;
        private const double BurstShotInterval = 0.065d;
        private const float MissionRadarRange = 70f;
        private const float CombatBoundaryGraceDuration = 8f;
        private const float CombatBoundaryReturnInset = 1.75f;
        private const float CombatBoundarySpawnInset = 5f;
        private const float CampaignMapDragThreshold = 9f;

        private static readonly Color GroundColor = new Color32(169, 171, 176, 255);
        private static readonly Color RoadColor = new Color32(148, 150, 155, 255);
        private static readonly Color DeepGraphiteColor = new Color32(31, 32, 38, 255);
        private static readonly Color GraphiteColor = new Color32(43, 44, 51, 255);
        private static readonly Color GraphiteLightColor = new Color32(56, 57, 64, 255);
        private static readonly Color TrunkColor = new Color32(66, 67, 72, 255);
        private static readonly Color RockColor = new Color32(61, 62, 69, 255);
        private static readonly Color CrateColor = new Color32(82, 83, 88, 255);
        private static readonly Color SandbagColor = new Color32(99, 100, 105, 255);
        private static readonly Color SovietRed = new Color32(190, 18, 42, 255);
        private static readonly Color SoftWhite = new Color32(225, 227, 231, 255);
        private static readonly BeatActionType[] LoadoutCatalogActions =
        {
            BeatActionType.Ppsh,
            BeatActionType.Rifle,
            BeatActionType.Grenade,
            BeatActionType.Dash,
            BeatActionType.Rest
        };

        private readonly List<PrototypeEnemy> enemies = new();
        private readonly List<PrototypeObjectiveNode> objectiveNodes = new();
        private readonly List<PrototypeDestructible> destructibles = new();
        private readonly Dictionary<Color32, Material> materialCache = new();
        private readonly Queue<PrototypeEnemy> enemyPool = new();
        private readonly Queue<PrototypeProjectile> projectilePool = new();
        private readonly Queue<PrototypePulseFx> pulsePool = new();
        private readonly List<PendingBurst> pendingBursts = new();
        private readonly List<Vector2> campaignMapControlScratch = new(16);
        private readonly List<Vector2> campaignMapClipScratch = new(24);
        private readonly List<Vector2> campaignMapClipWork = new(24);

        private SectorState[] sectors;
        private BeatSequenceModel baseSequence;
        private BeatSequenceModel runSequence;
        private PrototypePhase phase;
        private Camera gameplayCamera;
        private PrototypeFollowCamera followCamera;
        private Material campaignMapMaterial;
        private AudioSource[] beatAudioSources;
        private AudioClip beatClip;
        private AudioSource musicAudioSource;
        private AudioClip musicLoopClip;
        private GameObject worldRoot;
        private GameObject extractionBeacon;
        private GameObject extractionAircraft;
        private PrototypeActor player;
        private PrototypeObjectiveNode trackedObjective;
        private PrototypeInfiniteWorld infiniteWorld;
        private CombatBoundaryTimer boundaryTimer;

        private int selectedSectorIndex;
        private int selectedLoadoutSlot = -1;
        private int currentBeatIndex = -1;
        private int completedObjectives;
        private int kills;
        private int shotsFired;
        private int grenadesThrown;
        private int dashesPerformed;
        private int experience;
        private int level;
        private int experienceToNext;

        private double beatEpochDspTime;
        private long lastTriggeredBeatOrdinal;
        private long nextScheduledAudioBeatOrdinal;
        private int nextBeatAudioVoice;
        private float spawnTimer;
        private float runElapsed;
        private float damageMultiplier;
        private float tempoMultiplier;
        private float grenadeRadiusMultiplier;
        private float healingMultiplier;
        private float pendingAccentMultiplier;
        private BeatActionType lastWeaponAction;
        private bool dashSlashEnabled;

        private bool objectiveComplete;
        private bool extractionCalled;
        private bool extractionReady;
        private bool extracted;
        private bool boundaryDeath;
        private float extractionCountdown;
        private float boardingProgress;
        private float departureTimer;
        private Vector3 extractionPosition;

        private bool upgradeOpen;
        private bool upgradePending;
        private bool transportSuspended;
        private RunUpgradeType[] upgradeChoices;
        private float lastContribution;
        private float lastSectorBefore;

        private GUIStyle titleStyle;
        private GUIStyle headingStyle;
        private GUIStyle bodyStyle;
        private GUIStyle centeredStyle;
        private GUIStyle smallStyle;
        private GUIStyle warningStyle;
        private int consumedPointerFrame = -1;
        private bool queuedPointerClickPending;
        private Vector2 queuedPointerClickPosition;
        private Vector2 campaignMapCenter;
        private float campaignMapZoom;
        private Rect lastCampaignMapGeometryRect;
        private bool campaignMapDragActive;
        private Vector2 campaignMapDragStart;
        private Vector2 campaignMapDragPrevious;
        private float campaignMapMaximumDragDistance;
        private int campaignMapSuppressClickThroughFrame = -1;
        private int lastGuiWidth;
        private int lastGuiHeight;
        private long guiRepaintCount;

        internal enum PropKind
        {
            Tree,
            Rock,
            Crate,
            Sandbags
        }

        private sealed class PendingBurst
        {
            public int ShotsRemaining;
            public int ShotIndex;
            public int ShotCount;
            public float Damage;
            public float Range;
            public Vector3 Direction;
            public double NextShotDspTime;
        }

        public PrototypePhase Phase => phase;
        public PrototypeActor Player => player;
        public bool BoundaryWarningActive => boundaryTimer?.Active ?? false;
        public float BoundaryTimeRemaining =>
            boundaryTimer?.Remaining ?? CombatBoundaryGraceDuration;
        public bool IsSimulationRunning =>
            !upgradeOpen &&
            (phase == PrototypePhase.Mission || phase == PrototypePhase.Extraction) &&
            player != null &&
            player.IsAlive;

        private void Awake()
        {
            baseSequence = new BeatSequenceModel();
            boundaryTimer = new CombatBoundaryTimer(CombatBoundaryGraceDuration);
            campaignMapCenter =
                PrototypeCampaignMapLayout.DefaultCenter;
            campaignMapZoom = 1.55f;
            sectors = new[]
            {
                new SectorState("smolensk", "Смоленск", 47f, 1f),
                new SectorState("bryansk", "Брянск", 55f, 1.12f),
                new SectorState("orel", "Орёл", 42f, 1.25f)
            };

            phase = PrototypePhase.FrontMap;
            EnsureCamera();
            EnsureBeatAudio();
            Application.targetFrameRate = 60;
            Application.runInBackground = false;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            AudioSettings.OnAudioConfigurationChanged += HandleAudioConfigurationChanged;

#if !UNITY_EDITOR
            Screen.orientation = ScreenOrientation.Portrait;
#endif
        }

        private void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= HandleAudioConfigurationChanged;
            StopBeatTransport();

            if (beatClip != null)
            {
                Destroy(beatClip);
            }

            if (musicLoopClip != null)
            {
                Destroy(musicLoopClip);
            }

            if (campaignMapMaterial != null)
            {
                Destroy(campaignMapMaterial);
            }

            foreach (var cachedMaterial in materialCache.Values)
            {
                if (cachedMaterial != null)
                {
                    Destroy(cachedMaterial);
                }
            }

            materialCache.Clear();
        }

        private void Update()
        {
            CaptureUiPointerInput();

            if (phase == PrototypePhase.Departing)
            {
                UpdateDeparture();
                return;
            }

            if (!IsSimulationRunning)
            {
                return;
            }

            runElapsed += Time.deltaTime;
            if (runElapsed >= MissionTimeLimit)
            {
                EndRun(false);
                return;
            }

            UpdateBeatTransport();
            UpdatePendingBursts();
            UpdateEnemyDirector();

            if (phase == PrototypePhase.Extraction)
            {
                UpdateExtraction();
            }
        }

        private void LateUpdate()
        {
            if (IsSimulationRunning)
            {
                UpdateCombatBoundary();
            }
        }

        private void CaptureUiPointerInput()
        {
            var pointer = Pointer.current;
            if (pointer == null)
            {
                return;
            }

            var guiPosition =
                ToGuiPosition(pointer.position.ReadValue());
            var pointerOverMap =
                lastCampaignMapGeometryRect.width > 1f &&
                lastCampaignMapGeometryRect.height > 1f &&
                lastCampaignMapGeometryRect.Contains(guiPosition) &&
                phase == PrototypePhase.FrontMap &&
                selectedLoadoutSlot < 0;

            if (pointer.press.wasPressedThisFrame &&
                pointerOverMap)
            {
                campaignMapDragActive = true;
                campaignMapDragStart = guiPosition;
                campaignMapDragPrevious = guiPosition;
                campaignMapMaximumDragDistance = 0f;
                queuedPointerClickPending = false;
            }

            if (campaignMapDragActive &&
                pointer.press.isPressed)
            {
                campaignMapMaximumDragDistance = Mathf.Max(
                    campaignMapMaximumDragDistance,
                    Vector2.Distance(
                        campaignMapDragStart,
                        guiPosition));
                var delta =
                    guiPosition - campaignMapDragPrevious;
                if (delta.sqrMagnitude > 0.0001f)
                {
                    var view = GetCampaignMapView(
                        lastCampaignMapGeometryRect);
                    campaignMapCenter =
                        PrototypeCampaignMapNavigation.PanCenter(
                            view,
                            delta);
                    campaignMapDragPrevious = guiPosition;
                }
            }

            if (pointerOverMap &&
                !pointer.press.isPressed &&
                Mouse.current != null)
            {
                var scroll = Mouse.current.scroll.ReadValue();
                if (scroll.sqrMagnitude > 0.01f)
                {
                    var clampedScroll = new Vector2(
                        Mathf.Clamp(scroll.x, -120f, 120f),
                        Mathf.Clamp(scroll.y, -120f, 120f));
                    var view = GetCampaignMapView(
                        lastCampaignMapGeometryRect);
                    campaignMapCenter =
                        PrototypeCampaignMapNavigation.PanCenter(
                            view,
                            new Vector2(
                                clampedScroll.x * 0.28f,
                                -clampedScroll.y * 0.28f));
                }
            }

            if (!pointer.press.wasReleasedThisFrame)
            {
                return;
            }

            if (campaignMapDragActive)
            {
                var wasDrag =
                    PrototypeCampaignMapNavigation.IsDrag(
                        campaignMapMaximumDragDistance,
                        CampaignMapDragThreshold);
                campaignMapDragActive = false;
                if (wasDrag)
                {
                    queuedPointerClickPending = false;
                    campaignMapSuppressClickThroughFrame =
                        Time.frameCount + 1;
                    return;
                }
            }

            queuedPointerClickPending = true;
            queuedPointerClickPosition = guiPosition;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                SuspendBeatTransport();
                return;
            }

            ResumeBeatTransportIfNeeded();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                SuspendBeatTransport();
                return;
            }

            ResumeBeatTransportIfNeeded();
        }

        public Vector3 ClampToArena(Vector3 position)
        {
            position.y = 0f;
            return position;
        }

        private void UpdateCombatBoundary()
        {
            UpdateCombatBoundary(Time.deltaTime);
        }

        private void UpdateCombatBoundary(float deltaTime)
        {
            if (player == null || boundaryTimer == null)
            {
                return;
            }

            var playerPosition = player.transform.position;
            var outsideBoundary = !CombatBoundaryGeometry.Contains(
                playerPosition,
                ArenaHalfWidth,
                ArenaHalfHeight);
            var safelyInsideBoundary = CombatBoundaryGeometry.Contains(
                playerPosition,
                ArenaHalfWidth,
                ArenaHalfHeight,
                CombatBoundaryReturnInset);

            if (!boundaryTimer.Tick(
                    outsideBoundary,
                    safelyInsideBoundary,
                    deltaTime))
            {
                return;
            }

            boundaryDeath = true;
            CreatePulse(playerPosition, 3.4f, 0.42f, SovietRed);
            player.KillImmediately();
        }

        private Vector3 DirectionBackIntoCombatArea()
        {
            if (player == null)
            {
                return Vector3.zero;
            }

            var safePoint = CombatBoundaryGeometry.ClosestPointInside(
                player.transform.position,
                ArenaHalfWidth,
                ArenaHalfHeight,
                CombatBoundaryReturnInset);
            return safePoint - player.transform.position;
        }

        private void EnsureCamera()
        {
            gameplayCamera = Camera.main;
            if (gameplayCamera == null)
            {
                var cameraObject = new GameObject("Gameplay Camera");
                cameraObject.tag = "MainCamera";
                gameplayCamera = cameraObject.AddComponent<Camera>();
            }

            gameplayCamera.orthographic = true;
            gameplayCamera.orthographicSize = 9.6f;
            gameplayCamera.transform.SetPositionAndRotation(
                new Vector3(0f, 15.8f, -12.3f),
                Quaternion.Euler(52f, 0f, 0f));
            gameplayCamera.clearFlags = CameraClearFlags.SolidColor;
            gameplayCamera.backgroundColor = new Color32(174, 178, 185, 255);
            gameplayCamera.allowHDR = false;
            gameplayCamera.nearClipPlane = 0.1f;
            gameplayCamera.farClipPlane = 120f;

            if (gameplayCamera.GetComponent<AudioListener>() == null &&
                FindAnyObjectByType<AudioListener>() == null)
            {
                gameplayCamera.gameObject.AddComponent<AudioListener>();
            }

            followCamera = gameplayCamera.GetComponent<PrototypeFollowCamera>();
            if (followCamera == null)
            {
                followCamera = gameplayCamera.gameObject.AddComponent<PrototypeFollowCamera>();
            }

            followCamera.SetFraming(9.6f, 52f, 20f);
            followCamera.ClearTarget();

        }

        private void EnsureBeatAudio()
        {
            const int voiceCount = 4;
            beatAudioSources = new AudioSource[voiceCount];
            var existingSource = gameObject.GetComponent<AudioSource>();
            musicAudioSource = existingSource != null
                ? existingSource
                : gameObject.AddComponent<AudioSource>();
            musicAudioSource.playOnAwake = false;
            musicAudioSource.loop = true;
            musicAudioSource.spatialBlend = 0f;
            musicAudioSource.dopplerLevel = 0f;
            musicAudioSource.priority = 32;
            musicAudioSource.volume = 0.5f;

            for (var i = 0; i < voiceCount; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.dopplerLevel = 0f;
                source.priority = 48;
                source.volume = 0.05f;
                beatAudioSources[i] = source;
            }

            var sampleRate = Mathf.Max(22050, AudioSettings.outputSampleRate);
            var sampleCount = Mathf.RoundToInt(sampleRate * 0.035f);
            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                var normalized = i / (float)sampleRate;
                var envelope = 1f - i / (float)sampleCount;
                samples[i] = Mathf.Sin(2f * Mathf.PI * 760f * normalized) * envelope * envelope * 0.35f;
            }

            beatClip = AudioClip.Create("Prototype Beat", sampleCount, 1, sampleRate, false);
            beatClip.SetData(samples, 0);
            foreach (var source in beatAudioSources)
            {
                source.clip = beatClip;
            }

            musicLoopClip = PrototypeMusicGenerator.CreateLoop(
                "Iron Pulse — Original Combat Loop",
                sampleRate,
                BaseBeatInterval);
            musicAudioSource.clip = musicLoopClip;
        }

        private void StartMission(int sectorIndex)
        {
            selectedSectorIndex = Mathf.Clamp(sectorIndex, 0, sectors.Length - 1);
            ClearWorld();

            runSequence = new BeatSequenceModel(baseSequence);
            phase = PrototypePhase.Mission;
            currentBeatIndex = -1;
            completedObjectives = 0;
            kills = 0;
            shotsFired = 0;
            grenadesThrown = 0;
            dashesPerformed = 0;
            experience = 0;
            level = 1;
            experienceToNext = 8;
            runElapsed = 0f;
            spawnTimer = 0.35f;
            damageMultiplier = 1f;
            tempoMultiplier = 1f;
            grenadeRadiusMultiplier = 1f;
            healingMultiplier = 1f;
            pendingAccentMultiplier = 1f;
            lastWeaponAction = BeatActionType.Rest;
            dashSlashEnabled = false;
            boundaryDeath = false;
            boundaryTimer.Reset();
            objectiveComplete = false;
            extractionCalled = false;
            extractionReady = false;
            extracted = false;
            extractionCountdown = ExtractionWaitDuration;
            boardingProgress = 0f;
            upgradeOpen = false;
            upgradePending = false;
            selectedLoadoutSlot = -1;
            pendingBursts.Clear();

            BuildArena();
            BuildPlayer();
            BuildObjectives();
            BuildInfiniteWorld();
            SelectNextObjective();

            for (var i = 0; i < 4; i++)
            {
                SpawnEnemy();
            }

            ReanchorBeatTransport(0.25d);
        }

        private void BuildArena()
        {
            worldRoot = new GameObject("Prototype World");
            ConfigureMissionEnvironment();

            var roadSegments = new[]
            {
                (new Vector3(-20f, 0.025f, -65f), new Vector3(5.5f, 0.025f, 48f), -18f),
                (new Vector3(14f, 0.026f, 0f), new Vector3(5f, 0.025f, 58f), 14f),
                (new Vector3(0f, 0.027f, 76f), new Vector3(4.5f, 0.025f, 42f), -24f)
            };

            foreach (var (position, scale, rotation) in roadSegments)
            {
                var road = CreatePrimitive(
                    PrimitiveType.Cube,
                    "Dirt Road",
                    position,
                    scale,
                    RoadColor,
                    worldRoot.transform);
                road.transform.rotation = Quaternion.Euler(0f, rotation, 0f);
            }

            BuildCombatBoundary();
        }

        private void BuildCombatBoundary()
        {
            var boundaryRoot = new GameObject("Combat Area Boundary");
            boundaryRoot.transform.SetParent(worldRoot.transform);

            CreateBoundaryLine(
                boundaryRoot.transform,
                "North Boundary",
                new Vector3(0f, 0.045f, ArenaHalfHeight),
                new Vector3(ArenaHalfWidth * 2f, 0.035f, 0.18f));
            CreateBoundaryLine(
                boundaryRoot.transform,
                "South Boundary",
                new Vector3(0f, 0.045f, -ArenaHalfHeight),
                new Vector3(ArenaHalfWidth * 2f, 0.035f, 0.18f));
            CreateBoundaryLine(
                boundaryRoot.transform,
                "East Boundary",
                new Vector3(ArenaHalfWidth, 0.045f, 0f),
                new Vector3(0.18f, 0.035f, ArenaHalfHeight * 2f));
            CreateBoundaryLine(
                boundaryRoot.transform,
                "West Boundary",
                new Vector3(-ArenaHalfWidth, 0.045f, 0f),
                new Vector3(0.18f, 0.035f, ArenaHalfHeight * 2f));

            const int horizontalMarkerCount = 11;
            for (var index = 0; index < horizontalMarkerCount; index++)
            {
                var x = Mathf.Lerp(
                    -ArenaHalfWidth,
                    ArenaHalfWidth,
                    index / (horizontalMarkerCount - 1f));
                CreateBoundaryMarker(
                    boundaryRoot.transform,
                    new Vector3(x, 0f, ArenaHalfHeight));
                CreateBoundaryMarker(
                    boundaryRoot.transform,
                    new Vector3(x, 0f, -ArenaHalfHeight));
            }

            const int verticalMarkerCount = 15;
            for (var index = 1; index < verticalMarkerCount - 1; index++)
            {
                var z = Mathf.Lerp(
                    -ArenaHalfHeight,
                    ArenaHalfHeight,
                    index / (verticalMarkerCount - 1f));
                CreateBoundaryMarker(
                    boundaryRoot.transform,
                    new Vector3(ArenaHalfWidth, 0f, z));
                CreateBoundaryMarker(
                    boundaryRoot.transform,
                    new Vector3(-ArenaHalfWidth, 0f, z));
            }
        }

        private void CreateBoundaryLine(
            Transform parent,
            string objectName,
            Vector3 position,
            Vector3 scale)
        {
            var line = CreatePrimitive(
                PrimitiveType.Cube,
                objectName,
                position,
                scale,
                SovietRed,
                parent);
            ConfigureStreamingRenderers(line, false);
        }

        private void CreateBoundaryMarker(Transform parent, Vector3 position)
        {
            var marker = CreatePrimitive(
                PrimitiveType.Cylinder,
                "Boundary Marker",
                position + Vector3.up * 0.52f,
                new Vector3(0.13f, 0.52f, 0.13f),
                SovietRed,
                parent);
            ConfigureStreamingRenderers(marker, false);
        }

        private void BuildInfiniteWorld()
        {
            var streamObject = new GameObject("Mission Terrain Stream");
            streamObject.transform.SetParent(worldRoot.transform);
            infiniteWorld = streamObject.AddComponent<PrototypeInfiniteWorld>();
            infiniteWorld.Initialize(this, player.transform);
        }

        private void ConfigureMissionEnvironment()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color32(202, 205, 211, 255);
            RenderSettings.ambientEquatorColor = new Color32(145, 148, 155, 255);
            RenderSettings.ambientGroundColor = new Color32(91, 93, 100, 255);
            RenderSettings.ambientIntensity = 0.84f;
            RenderSettings.reflectionIntensity = 0.08f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color32(174, 178, 185, 255);
            RenderSettings.fogStartDistance = 24f;
            RenderSettings.fogEndDistance = 52f;

            var lightObject = new GameObject("Cold Directional Light");
            lightObject.transform.SetParent(worldRoot.transform);
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            var missionLight = lightObject.AddComponent<Light>();
            missionLight.type = LightType.Directional;
            missionLight.color = new Color32(235, 237, 242, 255);
            missionLight.intensity = 1.08f;
            missionLight.shadows = LightShadows.Soft;
            missionLight.shadowStrength = 0.38f;
            missionLight.shadowBias = 0.045f;
            missionLight.shadowNormalBias = 0.28f;
        }

        private void BuildDestructibleField()
        {
            destructibles.Clear();
            var previousRandomState = Random.state;
            Random.InitState(1941);
            var propIndex = 0;

            for (var cluster = 0; cluster < 10; cluster++)
            {
                var center = new Vector3(
                    Random.Range(-ArenaHalfWidth + 12f, ArenaHalfWidth - 12f),
                    0f,
                    Random.Range(-ArenaHalfHeight + 14f, ArenaHalfHeight - 14f));

                for (var item = 0; item < 9; item++)
                {
                    var angle = Random.Range(0f, Mathf.PI * 2f);
                    var radius = Mathf.Sqrt(Random.value) * Random.Range(5f, 12f);
                    var position = center +
                                   new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    position.x = Mathf.Clamp(position.x, -ArenaHalfWidth + 3f, ArenaHalfWidth - 3f);
                    position.z = Mathf.Clamp(position.z, -ArenaHalfHeight + 3f, ArenaHalfHeight - 3f);
                    if (IsReservedMissionPosition(position))
                    {
                        continue;
                    }

                    var kind = item == 8 && cluster % 2 == 0
                        ? PropKind.Rock
                        : PropKind.Tree;
                    CreateDestructibleProp(propIndex++, position, kind);
                }
            }

            for (var rock = 0; rock < 22; rock++)
            {
                var position = new Vector3(
                    Random.Range(-ArenaHalfWidth + 4f, ArenaHalfWidth - 4f),
                    0f,
                    Random.Range(-ArenaHalfHeight + 5f, ArenaHalfHeight - 5f));
                if (!IsReservedMissionPosition(position))
                {
                    CreateDestructibleProp(propIndex++, position, PropKind.Rock);
                }
            }

            var startForest = new (Vector3 Position, PropKind Kind)[]
            {
                (new Vector3(-4.2f, 0f, -86.5f), PropKind.Tree),
                (new Vector3(4.3f, 0f, -87.8f), PropKind.Tree),
                (new Vector3(-4.7f, 0f, -91f), PropKind.Rock),
                (new Vector3(4.5f, 0f, -99.6f), PropKind.Tree),
                (new Vector3(-3.9f, 0f, -102.2f), PropKind.Tree),
                (new Vector3(3.2f, 0f, -104.3f), PropKind.Rock),
                (new Vector3(-5f, 0f, -96f), PropKind.Tree),
                (new Vector3(5f, 0f, -93f), PropKind.Rock),
                (new Vector3(-2.8f, 0f, -88.5f), PropKind.Tree),
                (new Vector3(-3f, 0f, -99.5f), PropKind.Tree)
            };
            foreach (var (position, kind) in startForest)
            {
                CreateDestructibleProp(propIndex++, position, kind);
            }

            var routeProps = new (Vector3 Position, PropKind Kind)[]
            {
                (new Vector3(-37f, 0f, -45f), PropKind.Sandbags),
                (new Vector3(31f, 0f, 8f), PropKind.Crate),
                (new Vector3(-24f, 0f, 70f), PropKind.Sandbags),
                (new Vector3(35f, 0f, 94f), PropKind.Crate)
            };
            for (var i = 0; i < routeProps.Length; i++)
            {
                CreateDestructibleProp(
                    propIndex++,
                    routeProps[i].Position,
                    routeProps[i].Kind);
            }

            Random.state = previousRandomState;
        }

        private static bool IsReservedMissionPosition(Vector3 position)
        {
            var reservedPositions = new[]
            {
                (new Vector3(0f, 0f, -94f), 8f),
                (new Vector3(-40f, 0f, -48f), 6f),
                (new Vector3(35f, 0f, 10f), 6f),
                (new Vector3(-28f, 0f, 72f), 6f),
                (new Vector3(38f, 0f, 96f), 7f)
            };

            foreach (var (reservedPosition, radius) in reservedPositions)
            {
                if ((position - reservedPosition).sqrMagnitude < radius * radius)
                {
                    return true;
                }
            }

            return false;
        }

        internal PrototypeDestructible CreateDestructibleProp(
            int index,
            Vector3 position,
            PropKind kind,
            Transform parent = null,
            bool positionIsLocal = false)
        {
            var root = new GameObject($"Destructible {index:00}");
            root.transform.SetParent(parent != null ? parent : worldRoot.transform);
            if (positionIsLocal)
            {
                root.transform.localPosition = position;
                root.transform.localRotation = Quaternion.Euler(
                    0f,
                    Random.Range(0f, 360f),
                    0f);
            }
            else
            {
                root.transform.position = position;
                root.transform.rotation = Quaternion.Euler(
                    0f,
                    Random.Range(0f, 360f),
                    0f);
            }

            float health;
            float hitRadius;
            switch (kind)
            {
                case PropKind.Tree:
                    root.name = $"Tree {index:00}";
                    health = 55f;
                    hitRadius = 0.8f;
                    root.transform.localScale = Vector3.one * Random.Range(0.82f, 1.22f);
                    CreateLowPolyObject(
                        PrototypeLowPolyMeshFactory.Trunk,
                        "Trunk",
                        new Vector3(0f, 0f, 0f),
                        new Vector3(0.16f, 1.55f, 0.16f),
                        TrunkColor,
                        root.transform);
                    CreateLowPolyObject(
                        PrototypeLowPolyMeshFactory.Cone,
                        "Lower Crown",
                        new Vector3(0f, 0.24f, 0f),
                        new Vector3(0.82f, 1.2f, 0.82f),
                        GraphiteLightColor,
                        root.transform);
                    CreateLowPolyObject(
                        PrototypeLowPolyMeshFactory.Cone,
                        "Middle Crown",
                        new Vector3(0f, 0.9f, 0f),
                        new Vector3(0.64f, 1.14f, 0.64f),
                        GraphiteColor,
                        root.transform);
                    CreateLowPolyObject(
                        PrototypeLowPolyMeshFactory.Cone,
                        "Upper Crown",
                        new Vector3(0f, 1.52f, 0f),
                        new Vector3(0.46f, 1.05f, 0.46f),
                        DeepGraphiteColor,
                        root.transform);
                    break;

                case PropKind.Rock:
                    root.name = $"Rock {index:00}";
                    health = 95f;
                    hitRadius = 0.85f;
                    root.transform.localScale = Vector3.one * Random.Range(0.65f, 1.25f);
                    CreateLowPolyObject(
                        PrototypeLowPolyMeshFactory.Rock(index),
                        "Rock",
                        Vector3.zero,
                        new Vector3(0.9f, 0.58f, 0.9f),
                        index % 2 == 0 ? RockColor : GraphiteLightColor,
                        root.transform);
                    break;

                case PropKind.Crate:
                    root.name = $"Supply Crate {index:00}";
                    health = 20f;
                    hitRadius = 0.65f;
                    CreatePrimitive(
                        PrimitiveType.Cube,
                        "Crate",
                        new Vector3(0f, 0.42f, 0f),
                        new Vector3(0.85f, 0.82f, 0.85f),
                        CrateColor,
                        root.transform,
                        true);
                    break;

                default:
                    root.name = $"Sandbags {index:00}";
                    health = 80f;
                    hitRadius = 1.1f;
                    for (var bag = -1; bag <= 1; bag++)
                    {
                        CreatePrimitive(
                            PrimitiveType.Cube,
                            "Sandbag",
                            new Vector3(bag * 0.52f, 0.26f, 0f),
                            new Vector3(0.48f, 0.28f, 0.7f),
                            SandbagColor,
                            root.transform,
                            true);
                    }
                    break;
            }

            var destructible = root.AddComponent<PrototypeDestructible>();
            destructible.Initialize(this, health, hitRadius);
            destructible.Destroyed += HandleDestructibleDestroyed;
            destructibles.Add(destructible);
            return destructible;
        }

        internal GameObject CreateStreamingGroundTile(Transform parent, int index)
        {
            var ground = CreatePrimitive(
                PrimitiveType.Plane,
                $"Ground Chunk {index:00}",
                Vector3.zero,
                new Vector3(
                    PrototypeInfiniteWorld.ChunkSize / 10f,
                    1f,
                    PrototypeInfiniteWorld.ChunkSize / 10f),
                GroundColor,
                parent,
                true);
            ConfigureStreamingRenderers(ground, false);
            var groundRenderer = ground.GetComponent<Renderer>();
            if (groundRenderer != null && groundRenderer.sharedMaterial != null &&
                groundRenderer.sharedMaterial.HasProperty("_Smoothness"))
            {
                groundRenderer.sharedMaterial.SetFloat("_Smoothness", 0f);
            }

            return ground;
        }

        internal PrototypeDestructible CreateStreamingProp(
            Transform parent,
            int index,
            PropKind kind)
        {
            var prop = CreateDestructibleProp(
                index,
                Vector3.zero,
                kind,
                parent,
                true);
            ConfigureStreamingRenderers(prop.gameObject, true);
            return prop;
        }

        private static void ConfigureStreamingRenderers(
            GameObject root,
            bool receiveShadows)
        {
            if (root == null)
            {
                return;
            }

            foreach (var itemRenderer in root.GetComponentsInChildren<Renderer>(true))
            {
                itemRenderer.shadowCastingMode = ShadowCastingMode.Off;
                itemRenderer.receiveShadows = receiveShadows;
            }
        }

        internal void ReinitializeStreamingProp(
            PrototypeDestructible prop,
            Vector3 localPosition,
            float rotationDegrees,
            float scale)
        {
            if (prop == null)
            {
                return;
            }

            prop.transform.localPosition = localPosition;
            prop.transform.localRotation = Quaternion.Euler(0f, rotationDegrees, 0f);
            prop.transform.localScale = Vector3.one * scale;
            prop.Initialize(this, prop.MaxHealth, prop.HitRadius);
        }

        internal bool IsStreamingPositionReserved(Vector3 position)
        {
            const float boundaryClearance = 1.8f;
            var nearVerticalBoundary =
                Mathf.Abs(Mathf.Abs(position.x) - ArenaHalfWidth) <
                boundaryClearance &&
                Mathf.Abs(position.z) <= ArenaHalfHeight + boundaryClearance;
            var nearHorizontalBoundary =
                Mathf.Abs(Mathf.Abs(position.z) - ArenaHalfHeight) <
                boundaryClearance &&
                Mathf.Abs(position.x) <= ArenaHalfWidth + boundaryClearance;
            if (nearVerticalBoundary || nearHorizontalBoundary)
            {
                return true;
            }

            if (player != null)
            {
                var playerOffset = position - player.transform.position;
                playerOffset.y = 0f;
                if (playerOffset.sqrMagnitude < 5.5f * 5.5f)
                {
                    return true;
                }
            }

            foreach (var node in objectiveNodes)
            {
                if (node == null)
                {
                    continue;
                }

                var nodeOffset = position - node.transform.position;
                nodeOffset.y = 0f;
                if (nodeOffset.sqrMagnitude < 4.5f * 4.5f)
                {
                    return true;
                }
            }

            var extractionAnchor = extractionBeacon != null
                ? extractionPosition
                : new Vector3(38f, 0f, 96f);
            var extractionOffset = position - extractionAnchor;
            extractionOffset.y = 0f;
            if (extractionOffset.sqrMagnitude < 5f * 5f)
            {
                return true;
            }

            return false;
        }

        private void HandleDestructibleDestroyed(PrototypeDestructible destructible)
        {
            if (destructible != null)
            {
                CreatePulse(destructible.transform.position, 2.4f, 0.4f, SoftWhite);
            }
        }

        private void BuildPlayer()
        {
            var root = new GameObject("Player");
            root.transform.SetParent(worldRoot.transform);
            root.transform.position = new Vector3(0f, 0f, -94f);

            CreatePrimitive(
                PrimitiveType.Capsule,
                "Soldier",
                new Vector3(0f, 0.58f, 0f),
                new Vector3(0.38f, 0.55f, 0.38f),
                SovietRed,
                root.transform,
                true);

            CreatePrimitive(
                PrimitiveType.Sphere,
                "Helmet",
                new Vector3(0f, 1.28f, 0.02f),
                new Vector3(0.43f, 0.26f, 0.43f),
                GraphiteColor,
                root.transform,
                true);

            CreatePrimitive(
                PrimitiveType.Cube,
                "Weapon",
                new Vector3(0.34f, 0.65f, 0.2f),
                new Vector3(0.12f, 0.12f, 0.82f),
                SoftWhite,
                root.transform,
                true);

            player = root.AddComponent<PrototypeActor>();
            player.Initialize(this);
            followCamera?.ConfigureUnbounded(player.transform);
        }

        private void BuildObjectives()
        {
            objectiveNodes.Clear();
            CreateObjective("Радиоузел «Юг»", new Vector3(-40f, 0f, -48f));
            CreateObjective("Радиоузел «Центр»", new Vector3(35f, 0f, 10f));
            CreateObjective("Радиоузел «Север»", new Vector3(-28f, 0f, 72f));
        }

        private void CreateObjective(string displayName, Vector3 position)
        {
            var root = new GameObject(displayName);
            root.transform.SetParent(worldRoot.transform);
            root.transform.position = position;

            CreatePrimitive(
                PrimitiveType.Cylinder,
                "Radio Mast",
                new Vector3(0f, 0.72f, 0f),
                new Vector3(0.18f, 0.72f, 0.18f),
                GraphiteColor,
                root.transform,
                true);

            CreatePrimitive(
                PrimitiveType.Sphere,
                "Radio Signal",
                new Vector3(0f, 1.55f, 0f),
                Vector3.one * 0.34f,
                SovietRed,
                root.transform,
                true);

            var node = root.AddComponent<PrototypeObjectiveNode>();
            node.Initialize(this, displayName, 4f);
            objectiveNodes.Add(node);
            CreatePulse(position, 2.2f, 0.75f, SovietRed);
        }

        private void UpdateBeatTransport()
        {
            if (transportSuspended)
            {
                return;
            }

            var now = AudioSettings.dspTime;
            var interval = BeatInterval;

            ScheduleUpcomingBeatAudio(now, interval);

            if (now < beatEpochDspTime)
            {
                return;
            }

            var dueOrdinal =
                PrototypeBeatTransportMath.OrdinalAt(
                    now,
                    beatEpochDspTime,
                    interval);
            if (dueOrdinal <= lastTriggeredBeatOrdinal)
            {
                return;
            }

            // A slow frame or a throttled browser tab skips stale beats instead of
            // firing every missed action in one burst when execution resumes.
            lastTriggeredBeatOrdinal = dueOrdinal;
            TickBeat(dueOrdinal);
        }

        private double BeatInterval => BaseBeatInterval / tempoMultiplier;

        private void ScheduleUpcomingBeatAudio(double now, double interval)
        {
            if (beatAudioSources == null || beatAudioSources.Length == 0 || beatClip == null)
            {
                return;
            }

            if (now > beatEpochDspTime)
            {
                var earliestFutureOrdinal =
                    PrototypeBeatTransportMath.OrdinalAt(
                        now,
                        beatEpochDspTime,
                        interval) +
                    1L;
                nextScheduledAudioBeatOrdinal = System.Math.Max(
                    nextScheduledAudioBeatOrdinal,
                    earliestFutureOrdinal);
            }

            var scheduledThisFrame = 0;
            while (scheduledThisFrame < 2)
            {
                var scheduledTime =
                    PrototypeBeatTransportMath.BeatTime(
                        beatEpochDspTime,
                        nextScheduledAudioBeatOrdinal,
                        interval);
                if (scheduledTime > now + BeatScheduleLookAhead)
                {
                    break;
                }

                if (scheduledTime > now + 0.005d)
                {
                    var source = beatAudioSources[nextBeatAudioVoice];
                    nextBeatAudioVoice = (nextBeatAudioVoice + 1) % beatAudioSources.Length;
                    var slot =
                        PrototypeBeatTransportMath.SlotForOrdinal(
                            nextScheduledAudioBeatOrdinal,
                            BeatSequenceModel.SlotCount);
                    source.pitch = slot == 0 ? 1.25f : 1f;
                    source.volume = slot == 0 ? 0.08f : 0.025f;
                    source.PlayScheduled(scheduledTime);
                    scheduledThisFrame++;
                }

                nextScheduledAudioBeatOrdinal++;
            }
        }

        private void TickBeat(long beatOrdinal)
        {
            currentBeatIndex =
                PrototypeBeatTransportMath.SlotForOrdinal(
                    beatOrdinal,
                    BeatSequenceModel.SlotCount);
            var action = runSequence[currentBeatIndex];
            player.Pulse();
            TriggerAction(action);
        }

        private void ReanchorBeatTransport(double delay)
        {
            StopBeatVoices();
            beatEpochDspTime = AudioSettings.dspTime + System.Math.Max(0.05d, delay);
            lastTriggeredBeatOrdinal = -1L;
            nextScheduledAudioBeatOrdinal = 0L;
            nextBeatAudioVoice = 0;
            currentBeatIndex = -1;
            transportSuspended = false;
            if (musicAudioSource != null && musicLoopClip != null)
            {
                musicAudioSource.Stop();
                musicAudioSource.timeSamples = 0;
                musicAudioSource.pitch = Mathf.Clamp(
                    tempoMultiplier,
                    0.5f,
                    2f);
                musicAudioSource.PlayScheduled(beatEpochDspTime);
            }
        }

        private void SuspendBeatTransport()
        {
            transportSuspended = true;
            StopBeatVoices();
        }

        private void ResumeBeatTransportIfNeeded()
        {
            if (IsSimulationRunning)
            {
                ReanchorBeatTransport(BeatResumeDelay);
            }
        }

        private void StopBeatTransport()
        {
            transportSuspended = true;
            pendingBursts.Clear();
            StopBeatVoices();
        }

        private void StopBeatVoices()
        {
            if (beatAudioSources != null)
            {
                foreach (var source in beatAudioSources)
                {
                    if (source != null)
                    {
                        source.Stop();
                    }
                }
            }

            if (musicAudioSource != null)
            {
                musicAudioSource.Stop();
            }
        }

        private void HandleAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (IsSimulationRunning)
            {
                ReanchorBeatTransport(BeatResumeDelay);
            }
            else
            {
                SuspendBeatTransport();
            }
        }

        private void TriggerAction(BeatActionType action)
        {
            switch (action)
            {
                case BeatActionType.Accent:
                    pendingAccentMultiplier = Mathf.Max(pendingAccentMultiplier, 1.75f);
                    CreatePulse(player.transform.position, 1.2f, 0.22f, SovietRed);
                    return;

                case BeatActionType.Echo:
                    if (lastWeaponAction != BeatActionType.Rest)
                    {
                        ExecuteWeapon(lastWeaponAction, 0.5f);
                    }
                    return;

                case BeatActionType.Smoke:
                    player.GrantSmoke(1.4f);
                    CreatePulse(player.transform.position, 2.2f, 0.55f, SoftWhite);
                    return;

                case BeatActionType.Heal:
                    player.Heal(8f * healingMultiplier);
                    CreatePulse(player.transform.position, 1.5f, 0.4f, SovietRed);
                    return;

                case BeatActionType.Rest:
                    player.Heal(0.35f);
                    return;
            }

            var power = pendingAccentMultiplier;
            pendingAccentMultiplier = 1f;
            lastWeaponAction = action;
            ExecuteWeapon(action, power);
        }

        private void ExecuteWeapon(BeatActionType action, float actionMultiplier)
        {
            var totalMultiplier = damageMultiplier * actionMultiplier;
            switch (action)
            {
                case BeatActionType.Ppsh:
                    QueueBurst(4, 8f * totalMultiplier, 9f, player.FacingDirection);
                    break;

                case BeatActionType.Rifle:
                    FireDirectionalProjectile(
                        player.FacingDirection,
                        55f * totalMultiplier,
                        17f,
                        new Color(0.96f, 0.97f, 1f),
                        0.2f,
                        14f);
                    break;

                case BeatActionType.Grenade:
                {
                    var radius = 3.5f * grenadeRadiusMultiplier;
                    var impactPosition = ResolveDirectionalDestination(
                        player.FacingDirection,
                        12f);
                    grenadesThrown++;
                    Explode(impactPosition, radius, 50f * totalMultiplier);
                    break;
                }

                case BeatActionType.Dash:
                {
                    var direction = player.FacingDirection;
                    var dashStart = player.transform.position;
                    var dashDestination = ResolveDashDestination(direction, 4.2f);
                    if (!player.BeginDash(dashDestination, 0.12f))
                    {
                        break;
                    }

                    dashesPerformed++;
                    var sweepWidth = dashSlashEnabled ? 0.95f : 0.45f;
                    var sweepDamage = dashSlashEnabled ? 48f : 16f;
                    DamageDashSweep(
                        dashStart,
                        dashDestination,
                        sweepWidth,
                        sweepDamage * totalMultiplier,
                        dashSlashEnabled);
                    CreatePulse(
                        Vector3.Lerp(dashStart, dashDestination, 0.55f),
                        dashSlashEnabled ? 2.4f : 1.25f,
                        0.18f,
                        dashSlashEnabled ? SovietRed : SoftWhite);
                    break;
                }
            }
        }

        private Vector3 ResolveDashDestination(Vector3 direction, float distance)
        {
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : Vector3.forward;

            var start = player.transform.position;
            start.y = 0f;
            var segment = direction * Mathf.Max(0f, distance);
            var segmentLengthSqr = segment.sqrMagnitude;
            var closestProgress = 1f;

            foreach (var destructible in destructibles)
            {
                if (destructible == null || !destructible.IsAlive)
                {
                    continue;
                }

                var expandedRadius = destructible.HitRadius + 0.42f;
                var startOffset = destructible.transform.position - start;
                startOffset.y = 0f;
                if (startOffset.sqrMagnitude <= expandedRadius * expandedRadius)
                {
                    continue;
                }

                if (TryGetSegmentHitProgress(
                        start,
                        segment,
                        segmentLengthSqr,
                        destructible.transform.position,
                        expandedRadius,
                        out var progress) &&
                    progress < closestProgress)
                {
                    closestProgress = progress;
                }
            }

            var resolvedDistance = distance * closestProgress;
            if (closestProgress < 1f)
            {
                resolvedDistance = Mathf.Max(0f, resolvedDistance - 0.16f);
            }

            return ClampToArena(start + direction * resolvedDistance);
        }

        private void DamageDashSweep(
            Vector3 start,
            Vector3 end,
            float sweepWidth,
            float damage,
            bool upgradedSlash)
        {
            var enemySnapshot = enemies.ToArray();
            foreach (var enemy in enemySnapshot)
            {
                if (enemy == null || !enemy.IsAlive ||
                    !IsInsideSweptCircle(
                        start,
                        end,
                        enemy.transform.position,
                        sweepWidth + 0.5f))
                {
                    continue;
                }

                enemy.TakeDamage(damage);
            }

            foreach (var destructible in destructibles)
            {
                if (destructible == null || !destructible.IsAlive ||
                    !IsInsideSweptCircle(
                        start,
                        end,
                        destructible.transform.position,
                        sweepWidth + destructible.HitRadius))
                {
                    continue;
                }

                destructible.TakeDamage(
                    damage * (upgradedSlash ? 1.4f : 0.45f));
            }
        }

        private static bool IsInsideSweptCircle(
            Vector3 segmentStart,
            Vector3 segmentEnd,
            Vector3 point,
            float radius)
        {
            segmentStart.y = 0f;
            segmentEnd.y = 0f;
            point.y = 0f;
            var segment = segmentEnd - segmentStart;
            var lengthSqr = segment.sqrMagnitude;
            if (lengthSqr <= 0.0001f)
            {
                return (point - segmentStart).sqrMagnitude <= radius * radius;
            }

            var progress = Mathf.Clamp01(
                Vector3.Dot(point - segmentStart, segment) / lengthSqr);
            var closest = segmentStart + segment * progress;
            return (point - closest).sqrMagnitude <= radius * radius;
        }

        private void QueueBurst(
            int count,
            float projectileDamage,
            float range,
            Vector3 direction)
        {
            count = Mathf.Max(0, count);
            if (count == 0)
            {
                return;
            }

            var firstShotDirection = Quaternion.Euler(
                0f,
                BurstSpreadAngle(0, count),
                0f) * direction;
            FireDirectionalProjectile(
                firstShotDirection,
                projectileDamage,
                13f,
                new Color(0.88f, 0.9f, 0.94f),
                0.13f,
                range);
            if (count == 1)
            {
                return;
            }

            pendingBursts.Add(new PendingBurst
            {
                ShotsRemaining = count - 1,
                ShotIndex = 1,
                ShotCount = count,
                Damage = projectileDamage,
                Range = range,
                Direction = direction,
                NextShotDspTime =
                    AudioSettings.dspTime + BurstShotInterval
            });
        }

        private void UpdatePendingBursts()
        {
            var now = AudioSettings.dspTime;
            for (var i = pendingBursts.Count - 1; i >= 0; i--)
            {
                var burst = pendingBursts[i];
                if (now < burst.NextShotDspTime)
                {
                    continue;
                }

                var shotDirection = Quaternion.Euler(
                    0f,
                    BurstSpreadAngle(burst.ShotIndex, burst.ShotCount),
                    0f) * burst.Direction;
                FireDirectionalProjectile(
                    shotDirection,
                    burst.Damage,
                    13f,
                    new Color(0.88f, 0.9f, 0.94f),
                    0.13f,
                    burst.Range);

                burst.ShotIndex++;
                burst.ShotsRemaining--;
                if (burst.ShotsRemaining <= 0)
                {
                    pendingBursts.RemoveAt(i);
                    continue;
                }

                // Never catch up several sub-shots in one frame after a hitch.
                burst.NextShotDspTime = now + BurstShotInterval;
            }

            if (upgradePending && pendingBursts.Count == 0)
            {
                OpenUpgradeDraft();
            }
        }

        private static float BurstSpreadAngle(int shotIndex, int shotCount)
        {
            if (shotCount <= 1)
            {
                return 0f;
            }

            return shotIndex switch
            {
                0 => -7f,
                1 => -2f,
                2 => 2f,
                _ => 7f
            };
        }

        private void FireDirectionalProjectile(
            Vector3 direction,
            float projectileDamage,
            float projectileSpeed,
            Color color,
            float size,
            float range)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f)
            {
                direction = Vector3.forward;
            }

            direction.Normalize();
            FireProjectile(
                ResolveDirectionalDestination(direction, range),
                projectileDamage,
                projectileSpeed,
                color,
                size);

            shotsFired++;
            var muzzlePosition = player.transform.position +
                                 Vector3.up * 0.62f +
                                 direction * 0.42f;
            CreatePulse(muzzlePosition, 0.42f, 0.1f, color);
        }

        private void FireProjectile(
            Vector3 destination,
            float projectileDamage,
            float projectileSpeed,
            Color color,
            float size)
        {
            var projectile = GetProjectile();
            var projectileObject = projectile.gameObject;
            projectileObject.transform.SetParent(worldRoot.transform);
            projectileObject.transform.position = player.transform.position + Vector3.up * 0.55f;
            var shotDirection = destination - player.transform.position;
            shotDirection.y = 0f;
            projectileObject.transform.rotation = Quaternion.LookRotation(
                shotDirection.sqrMagnitude > 0.001f
                    ? shotDirection.normalized
                    : player.FacingDirection);
            projectileObject.transform.localScale =
                new Vector3(size * 0.55f, size * 0.55f, size * 3.2f);
            var itemRenderer = projectileObject.GetComponent<Renderer>();
            if (itemRenderer != null)
            {
                itemRenderer.sharedMaterial = GetOrCreateMaterial(color);
            }

            destination.y = 0.45f;
            projectileObject.SetActive(true);
            projectile.Initialize(this, destination, projectileDamage, projectileSpeed);
        }

        private Vector3 ResolveDirectionalDestination(Vector3 direction, float range)
        {
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : Vector3.forward;
            var destination = player.transform.position + direction * range;
            destination = ClampToArena(destination);
            destination.y = 0f;
            return destination;
        }

        private PrototypeProjectile GetProjectile()
        {
            while (projectilePool.Count > 0)
            {
                var pooled = projectilePool.Dequeue();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            var projectileObject = CreatePrimitive(
                PrimitiveType.Cube,
                "Projectile",
                Vector3.zero,
                Vector3.one,
                Color.white,
                worldRoot.transform);
            var projectile = projectileObject.AddComponent<PrototypeProjectile>();
            projectileObject.SetActive(false);
            return projectile;
        }

        public void ReleaseProjectile(PrototypeProjectile projectile)
        {
            if (projectile == null || worldRoot == null)
            {
                return;
            }

            projectile.gameObject.SetActive(false);
            projectile.transform.SetParent(worldRoot.transform);
            projectilePool.Enqueue(projectile);
        }

        public bool TryDamageTargetAlongSegment(
            Vector3 segmentStart,
            Vector3 segmentEnd,
            float projectileDamage)
        {
            segmentStart.y = 0f;
            segmentEnd.y = 0f;
            var segment = segmentEnd - segmentStart;
            var segmentLengthSqr = segment.sqrMagnitude;
            if (segmentLengthSqr <= 0.0001f)
            {
                return false;
            }

            PrototypeEnemy closestEnemy = null;
            PrototypeDestructible closestHit = null;
            var closestProgress = float.MaxValue;

            foreach (var enemy in enemies)
            {
                if (enemy == null || !enemy.IsAlive ||
                    !TryGetSegmentHitProgress(
                        segmentStart,
                        segment,
                        segmentLengthSqr,
                        enemy.transform.position,
                        0.72f,
                        out var progress) ||
                    progress >= closestProgress)
                {
                    continue;
                }

                closestEnemy = enemy;
                closestHit = null;
                closestProgress = progress;
            }

            foreach (var destructible in destructibles)
            {
                if (destructible == null || !destructible.IsAlive ||
                    !TryGetSegmentHitProgress(
                        segmentStart,
                        segment,
                        segmentLengthSqr,
                        destructible.transform.position,
                        destructible.HitRadius,
                        out var progress) ||
                    progress >= closestProgress)
                {
                    continue;
                }

                closestEnemy = null;
                closestHit = destructible;
                closestProgress = progress;
            }

            if (closestEnemy != null)
            {
                var hitPosition = closestEnemy.transform.position;
                closestEnemy.TakeDamage(projectileDamage);
                CreatePulse(
                    hitPosition + Vector3.up * 0.35f,
                    0.5f,
                    0.1f,
                    new Color(0.72f, 0.04f, 0.06f));
                return true;
            }

            if (closestHit == null)
            {
                return false;
            }

            closestHit.TakeDamage(projectileDamage);
            CreatePulse(
                closestHit.transform.position + Vector3.up * 0.35f,
                0.55f,
                0.12f,
                new Color(0.88f, 0.9f, 0.92f));
            return true;
        }

        private static bool TryGetSegmentHitProgress(
            Vector3 segmentStart,
            Vector3 segment,
            float segmentLengthSqr,
            Vector3 targetPosition,
            float hitRadius,
            out float progress)
        {
            var toTarget = targetPosition - segmentStart;
            toTarget.y = 0f;
            var segmentLength = Mathf.Sqrt(segmentLengthSqr);
            var direction = segment / segmentLength;
            var along = Vector3.Dot(toTarget, direction);
            var perpendicularSqr = Mathf.Max(0f, toTarget.sqrMagnitude - along * along);
            var radiusSqr = hitRadius * hitRadius;
            if (perpendicularSqr > radiusSqr)
            {
                progress = 0f;
                return false;
            }

            var halfChord = Mathf.Sqrt(radiusSqr - perpendicularSqr);
            var entryDistance = along - halfChord;
            var exitDistance = along + halfChord;
            if (exitDistance < 0f || entryDistance > segmentLength)
            {
                progress = 0f;
                return false;
            }

            progress = Mathf.Clamp01(Mathf.Max(0f, entryDistance) / segmentLength);
            return true;
        }

        private void Explode(Vector3 position, float radius, float explosionDamage)
        {
            var snapshot = enemies.ToArray();
            foreach (var enemy in snapshot)
            {
                if (enemy == null || !enemy.IsAlive)
                {
                    continue;
                }

                var offset = enemy.transform.position - position;
                offset.y = 0f;
                if (offset.sqrMagnitude <= radius * radius)
                {
                    enemy.TakeDamage(explosionDamage);
                }
            }

            foreach (var destructible in destructibles)
            {
                if (destructible == null || !destructible.IsAlive)
                {
                    continue;
                }

                var offset = destructible.transform.position - position;
                offset.y = 0f;
                var distance = offset.magnitude;
                if (distance > radius)
                {
                    continue;
                }

                var normalizedDistance = radius <= 0f ? 1f : distance / radius;
                var propDamage = explosionDamage * 1.8f *
                                 Mathf.Lerp(1f, 0.4f, normalizedDistance);
                destructible.TakeDamage(propDamage);
            }

            CreatePulse(position, radius * 2f, 0.42f, SoftWhite);
        }

        private void UpdateEnemyDirector()
        {
            if (BoundaryWarningActive)
            {
                spawnTimer = Mathf.Max(spawnTimer, 0.25f);
                return;
            }

            RecycleDistantEnemies();
            spawnTimer -= Time.deltaTime;
            if (spawnTimer > 0f || enemies.Count >= 48)
            {
                return;
            }

            var baseInterval = Mathf.Max(0.5f, 1.3f - runElapsed * 0.004f);
            if (phase == PrototypePhase.Extraction)
            {
                baseInterval = Mathf.Max(0.35f, baseInterval * 0.55f);
            }

            spawnTimer = baseInterval;
            SpawnEnemy();
        }

        private void SpawnEnemy()
        {
            if (worldRoot == null)
            {
                return;
            }

            var enemy = GetEnemy();
            var root = enemy.gameObject;
            root.transform.SetParent(worldRoot.transform);
            root.transform.position = RandomSpawnPosition();
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            root.SetActive(true);

            var danger = sectors[selectedSectorIndex].Danger;
            enemy.Initialize(
                this,
                (24f + runElapsed * 0.035f) * danger,
                Random.Range(1.65f, 2.15f) * Mathf.Lerp(1f, 1.12f, danger - 1f),
                8f * danger);
            enemies.Add(enemy);
        }

        private PrototypeEnemy GetEnemy()
        {
            while (enemyPool.Count > 0)
            {
                var pooled = enemyPool.Dequeue();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            var root = new GameObject("Enemy");
            root.transform.SetParent(worldRoot.transform);
            CreatePrimitive(
                PrimitiveType.Capsule,
                "Enemy Visual",
                new Vector3(0f, 0.5f, 0f),
                new Vector3(0.36f, 0.48f, 0.36f),
                GraphiteColor,
                root.transform,
                true);
            var enemy = root.AddComponent<PrototypeEnemy>();
            root.SetActive(false);
            return enemy;
        }

        private Vector3 RandomSpawnPosition()
        {
            if (player == null)
            {
                return Vector3.zero;
            }

            const float minimumSpawnDistance = 11.5f;
            var origin = player.transform.position;
            origin.y = 0f;

            for (var attempt = 0; attempt < 16; attempt++)
            {
                var angle = Random.Range(0f, Mathf.PI * 2f);
                var radius = Random.Range(12f, 16f);
                var candidate = origin +
                                new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                candidate.y = 0f;

                if ((candidate - origin).sqrMagnitude >= minimumSpawnDistance * minimumSpawnDistance &&
                    CombatBoundaryGeometry.Contains(
                        candidate,
                        ArenaHalfWidth,
                        ArenaHalfHeight,
                        CombatBoundarySpawnInset))
                {
                    return candidate;
                }
            }

            var towardCenter = new Vector3(-origin.x, 0f, -origin.z);
            if (towardCenter.sqrMagnitude < 0.001f)
            {
                towardCenter = Vector3.forward;
            }

            var fallback = origin + towardCenter.normalized * 14f;
            return CombatBoundaryGeometry.ClosestPointInside(
                fallback,
                ArenaHalfWidth,
                ArenaHalfHeight,
                CombatBoundarySpawnInset);
        }

        private void RecycleDistantEnemies()
        {
            if (player == null)
            {
                return;
            }

            const float recycleDistanceSqr = 36f * 36f;
            for (var i = enemies.Count - 1; i >= 0; i--)
            {
                var enemy = enemies[i];
                if (enemy == null)
                {
                    enemies.RemoveAt(i);
                    continue;
                }

                if ((enemy.transform.position - player.transform.position).sqrMagnitude <=
                    recycleDistanceSqr)
                {
                    continue;
                }

                enemies.RemoveAt(i);
                ReleaseEnemy(enemy);
            }
        }

        public void NotifyEnemyKilled(PrototypeEnemy enemy, Vector3 position)
        {
            enemies.Remove(enemy);
            kills++;
            experience++;
            CreatePulse(position, 0.8f, 0.24f, SovietRed);
            ReleaseEnemy(enemy);

            if (!upgradeOpen && !upgradePending && experience >= experienceToNext &&
                (phase == PrototypePhase.Mission || phase == PrototypePhase.Extraction))
            {
                experience -= experienceToNext;
                level++;
                experienceToNext = 8 + level * 5;
                if (pendingBursts.Count > 0)
                {
                    upgradePending = true;
                }
                else
                {
                    OpenUpgradeDraft();
                }
            }
        }

        private void ReleaseEnemy(PrototypeEnemy enemy)
        {
            if (enemy == null || worldRoot == null)
            {
                return;
            }

            enemy.gameObject.SetActive(false);
            enemy.transform.SetParent(worldRoot.transform);
            enemyPool.Enqueue(enemy);
        }

        public void NotifyObjectiveCompleted(PrototypeObjectiveNode node)
        {
            completedObjectives++;
            CreatePulse(node.transform.position, 3.1f, 0.75f, SoftWhite);

            if (completedObjectives >= objectiveNodes.Count)
            {
                BeginExtraction();
                return;
            }

            SelectNextObjective();
        }

        private void SelectNextObjective()
        {
            trackedObjective = null;
            if (player == null)
            {
                return;
            }

            var bestDistance = float.MaxValue;
            foreach (var node in objectiveNodes)
            {
                if (node == null || node.Completed)
                {
                    continue;
                }

                var distance = (node.transform.position - player.transform.position).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                trackedObjective = node;
            }
        }

        public void NotifyPlayerDied()
        {
            if (phase == PrototypePhase.Departing || phase == PrototypePhase.Debrief)
            {
                return;
            }

            EndRun(false);
        }

        private void BeginExtraction()
        {
            if (objectiveComplete)
            {
                return;
            }

            objectiveComplete = true;
            trackedObjective = null;
            phase = PrototypePhase.Extraction;
            extractionPosition = new Vector3(38f, 0f, 96f);
            extractionCountdown = ExtractionWaitDuration;
            CreateExtractionBeacon();
        }

        private void CreateExtractionBeacon()
        {
            extractionBeacon = new GameObject("Extraction Beacon");
            extractionBeacon.transform.SetParent(worldRoot.transform);
            extractionBeacon.transform.position = extractionPosition;

            CreatePrimitive(
                PrimitiveType.Cylinder,
                "Beacon",
                new Vector3(0f, 0.16f, 0f),
                new Vector3(1.45f, 0.08f, 1.45f),
                SovietRed,
                extractionBeacon.transform,
                true);

            CreatePrimitive(
                PrimitiveType.Cylinder,
                "Radio",
                new Vector3(0f, 0.65f, 0f),
                new Vector3(0.12f, 0.55f, 0.12f),
                SovietRed,
                extractionBeacon.transform,
                true);

            CreatePulse(extractionPosition, 3.2f, 1.1f, SovietRed);
        }

        private void UpdateExtraction()
        {
            var playerOffset = player.transform.position - extractionPosition;
            playerOffset.y = 0f;
            var playerInZone = playerOffset.sqrMagnitude <= 1.7f * 1.7f;

            if (!extractionCalled && playerInZone)
            {
                extractionCalled = true;
                extractionCountdown = ExtractionWaitDuration;
                CreateAircraft();
            }

            if (!extractionCalled)
            {
                return;
            }

            extractionCountdown = Mathf.Max(0f, extractionCountdown - Time.deltaTime);
            UpdateAircraftApproach();

            if (extractionCountdown <= 0f)
            {
                extractionReady = true;
            }

            if (!extractionReady)
            {
                return;
            }

            if (playerInZone)
            {
                boardingProgress += Time.deltaTime;
            }
            else
            {
                boardingProgress = Mathf.Max(0f, boardingProgress - Time.deltaTime * 0.75f);
            }

            if (boardingProgress >= 1.5f)
            {
                BeginDeparture();
            }
        }

        private void CreateAircraft()
        {
            extractionAircraft = new GameObject("Extraction Aircraft");
            extractionAircraft.transform.SetParent(worldRoot.transform);
            extractionAircraft.transform.position = extractionPosition + new Vector3(0f, 8f, 8f);

            CreatePrimitive(
                PrimitiveType.Cube,
                "Fuselage",
                Vector3.zero,
                new Vector3(0.8f, 0.45f, 2.7f),
                GraphiteColor,
                extractionAircraft.transform,
                true);

            CreatePrimitive(
                PrimitiveType.Cube,
                "Wings",
                new Vector3(0f, 0f, 0.15f),
                new Vector3(4.1f, 0.12f, 0.75f),
                GraphiteLightColor,
                extractionAircraft.transform,
                true);
        }

        private void UpdateAircraftApproach()
        {
            if (extractionAircraft == null)
            {
                return;
            }

            var normalized = 1f - extractionCountdown / ExtractionWaitDuration;
            var eased = normalized * normalized * (3f - 2f * normalized);
            var start = extractionPosition + new Vector3(0f, 8f, 8f);
            var end = extractionPosition + new Vector3(0f, 0.75f, 0f);
            extractionAircraft.transform.position = Vector3.Lerp(start, end, eased);
        }

        private void BeginDeparture()
        {
            if (phase == PrototypePhase.Departing)
            {
                return;
            }

            extracted = true;
            phase = PrototypePhase.Departing;
            departureTimer = 1.8f;
            StopBeatTransport();
            if (extractionAircraft != null)
            {
                followCamera?.ConfigureUnbounded(extractionAircraft.transform);
            }

            if (player != null)
            {
                player.gameObject.SetActive(false);
            }
        }

        private void UpdateDeparture()
        {
            departureTimer -= Time.deltaTime;
            if (extractionAircraft != null)
            {
                extractionAircraft.transform.position += new Vector3(0f, 4.2f, 3f) * Time.deltaTime;
            }

            if (departureTimer <= 0f)
            {
                EndRun(true);
            }
        }

        private void EndRun(bool successfulExtraction)
        {
            StopBeatTransport();
            extracted = successfulExtraction;
            phase = PrototypePhase.Debrief;
            upgradeOpen = false;
            upgradePending = false;
            lastContribution = FactionContributionCalculator.Calculate(objectiveComplete, extracted);
            lastSectorBefore = sectors[selectedSectorIndex].SovietControl;
            sectors[selectedSectorIndex].AddSovietControl(lastContribution);
        }

        private void OpenUpgradeDraft()
        {
            upgradePending = false;
            upgradeOpen = true;
            SuspendBeatTransport();
            var available = new List<RunUpgradeType>
            {
                RunUpgradeType.Damage,
                RunUpgradeType.Tempo,
                RunUpgradeType.Vitality,
                RunUpgradeType.GrenadeRadius
            };

            if (runSequence.HasRest())
            {
                available.Add(RunUpgradeType.AddHeal);
                available.Add(RunUpgradeType.AddAccent);
                available.Add(RunUpgradeType.AddEcho);
            }

            if (runSequence.Contains(BeatActionType.Dash) && !dashSlashEnabled)
            {
                available.Add(RunUpgradeType.DashSlash);
            }

            upgradeChoices = new RunUpgradeType[3];
            for (var i = 0; i < upgradeChoices.Length; i++)
            {
                var randomIndex = Random.Range(0, available.Count);
                upgradeChoices[i] = available[randomIndex];
                available.RemoveAt(randomIndex);
            }
        }

        private void ApplyUpgrade(RunUpgradeType upgrade)
        {
            switch (upgrade)
            {
                case RunUpgradeType.Damage:
                    damageMultiplier *= 1.25f;
                    break;
                case RunUpgradeType.Tempo:
                    tempoMultiplier *= 1.08f;
                    break;
                case RunUpgradeType.Vitality:
                    player.IncreaseMaxHealth(20f);
                    break;
                case RunUpgradeType.GrenadeRadius:
                    grenadeRadiusMultiplier *= 1.22f;
                    break;
                case RunUpgradeType.AddHeal:
                    runSequence.TryReplaceFirstRest(BeatActionType.Heal);
                    break;
                case RunUpgradeType.AddAccent:
                    runSequence.TryReplaceFirstRest(BeatActionType.Accent);
                    break;
                case RunUpgradeType.AddEcho:
                    runSequence.TryReplaceFirstRest(BeatActionType.Echo);
                    break;
                case RunUpgradeType.DashSlash:
                    dashSlashEnabled = true;
                    break;
            }

            upgradeOpen = false;
            ReanchorBeatTransport(BeatResumeDelay);
        }

        private string UpgradeTitle(RunUpgradeType upgrade)
        {
            return upgrade switch
            {
                RunUpgradeType.Damage => "Усиленные патроны",
                RunUpgradeType.Tempo => "Ускорить ритм",
                RunUpgradeType.Vitality => "Полевое снаряжение",
                RunUpgradeType.GrenadeRadius => "Больше осколков",
                RunUpgradeType.AddHeal => "Вставить: перевязка",
                RunUpgradeType.AddAccent => "Вставить: акцент",
                RunUpgradeType.AddEcho => "Вставить: эхо",
                RunUpgradeType.DashSlash => "Рассечение",
                _ => upgrade.ToString()
            };
        }

        private string UpgradeDescription(RunUpgradeType upgrade)
        {
            return upgrade switch
            {
                RunUpgradeType.Damage => "+25% к урону всего оружия",
                RunUpgradeType.Tempo => "+8% к скорости боевой ленты",
                RunUpgradeType.Vitality => "+20 максимального здоровья",
                RunUpgradeType.GrenadeRadius => "+22% к радиусу гранаты",
                RunUpgradeType.AddHeal => "Заменяет первую передышку лечением",
                RunUpgradeType.AddAccent => "Следующее оружие срабатывает сильнее",
                RunUpgradeType.AddEcho => "Повторяет последнее оружие с 50% силы",
                RunUpgradeType.DashSlash => "Рывок наносит усиленный урон всем на пути",
                _ => string.Empty
            };
        }

        private GameObject CreateLowPolyObject(
            Mesh mesh,
            string objectName,
            Vector3 localPosition,
            Vector3 localScale,
            Color color,
            Transform parent)
        {
            var created = new GameObject(objectName);
            created.transform.SetParent(parent);
            created.transform.localPosition = localPosition;
            created.transform.localRotation = Quaternion.identity;
            created.transform.localScale = localScale;
            var meshFilter = created.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
            var meshRenderer = created.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = GetOrCreateMaterial(color);
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            return created;
        }

        private GameObject CreatePrimitive(
            PrimitiveType primitiveType,
            string objectName,
            Vector3 position,
            Vector3 scale,
            Color color,
            Transform parent,
            bool positionIsLocal = false)
        {
            var created = GameObject.CreatePrimitive(primitiveType);
            created.name = objectName;
            created.transform.SetParent(parent);
            if (positionIsLocal)
            {
                created.transform.localPosition = position;
            }
            else
            {
                created.transform.position = position;
            }

            created.transform.localScale = scale;
            var itemRenderer = created.GetComponent<Renderer>();
            if (itemRenderer != null)
            {
                itemRenderer.sharedMaterial = GetOrCreateMaterial(color);
            }

            var primitiveCollider = created.GetComponent<Collider>();
            if (primitiveCollider != null)
            {
                Destroy(primitiveCollider);
            }

            return created;
        }

        private Material GetOrCreateMaterial(Color color)
        {
            var key = (Color32)color;
            if (materialCache.TryGetValue(key, out var cachedMaterial) && cachedMaterial != null)
            {
                return cachedMaterial;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            else
            {
                material.color = color;
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0f);
            }

            if (material.HasProperty("_SpecularHighlights"))
            {
                material.SetFloat("_SpecularHighlights", 0f);
            }

            if (material.HasProperty("_EnvironmentReflections"))
            {
                material.SetFloat("_EnvironmentReflections", 0f);
            }

            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            material.enableInstancing = true;
            materialCache[key] = material;
            return material;
        }

        private void CreatePulse(Vector3 position, float size, float seconds, Color color)
        {
            if (worldRoot == null)
            {
                return;
            }

            var fx = GetPulse();
            var pulse = fx.gameObject;
            pulse.transform.SetParent(worldRoot.transform);
            pulse.transform.position = position + Vector3.up * 0.04f;
            pulse.transform.rotation = Quaternion.identity;
            var itemRenderer = pulse.GetComponent<Renderer>();
            if (itemRenderer != null)
            {
                itemRenderer.sharedMaterial = GetOrCreateMaterial(color);
            }

            pulse.SetActive(true);
            fx.Initialize(this, size, seconds);
        }

        private PrototypePulseFx GetPulse()
        {
            while (pulsePool.Count > 0)
            {
                var pooled = pulsePool.Dequeue();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            var pulse = CreatePrimitive(
                PrimitiveType.Cylinder,
                "Pulse",
                Vector3.zero,
                Vector3.zero,
                Color.white,
                worldRoot.transform);
            var fx = pulse.AddComponent<PrototypePulseFx>();
            pulse.SetActive(false);
            return fx;
        }

        public void ReleasePulse(PrototypePulseFx pulse)
        {
            if (pulse == null || worldRoot == null)
            {
                return;
            }

            pulse.gameObject.SetActive(false);
            pulse.transform.SetParent(worldRoot.transform);
            pulsePool.Enqueue(pulse);
        }

        private void ClearWorld()
        {
            StopBeatTransport();
            followCamera?.ClearTarget();
            enemies.Clear();
            objectiveNodes.Clear();
            destructibles.Clear();
            enemyPool.Clear();
            projectilePool.Clear();
            pulsePool.Clear();
            player = null;
            trackedObjective = null;
            infiniteWorld = null;
            extractionBeacon = null;
            extractionAircraft = null;

            if (worldRoot != null)
            {
                Destroy(worldRoot);
                worldRoot = null;
            }
        }

        private void ReturnToFrontMap()
        {
            ClearWorld();
            phase = PrototypePhase.FrontMap;
            selectedLoadoutSlot = -1;
            upgradeOpen = false;
        }

        private void ResetCampaign()
        {
            foreach (var sector in sectors)
            {
                sector.Reset();
            }

            PlayerPrefs.Save();
        }

        private void OnGUI()
        {
            lastGuiWidth = Screen.width;
            lastGuiHeight = Screen.height;
            if (Event.current.type == EventType.Repaint)
            {
                guiRepaintCount++;
            }

            BuildGuiStyles();

            switch (phase)
            {
                case PrototypePhase.FrontMap:
                    DrawFrontMap();
                    break;
                case PrototypePhase.Mission:
                case PrototypePhase.Extraction:
                case PrototypePhase.Departing:
                    DrawMissionHud();
                    break;
                case PrototypePhase.Debrief:
                    DrawDebrief();
                    break;
            }

            if (upgradeOpen)
            {
                DrawUpgradeDraft();
            }

            if (Event.current.type == EventType.Repaint)
            {
                queuedPointerClickPending = false;
            }
        }

        private void BuildGuiStyles()
        {
            var scale = Mathf.Clamp(Mathf.Min(Screen.width / 540f, Screen.height / 960f), 0.65f, 1.4f);
            var titleFontSize = Mathf.RoundToInt(34f * scale);
            var headingFontSize = Mathf.RoundToInt(22f * scale);
            var bodyFontSize = Mathf.RoundToInt(17f * scale);
            var smallFontSize = Mathf.RoundToInt(13f * scale);
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            headingStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                normal = { textColor = new Color(0.9f, 0.91f, 0.93f) }
            };

            centeredStyle ??= new GUIStyle(bodyStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };

            smallStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.86f, 0.87f, 0.9f) }
            };

            warningStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = Color.white }
            };

            titleStyle.fontSize = titleFontSize;
            headingStyle.fontSize = headingFontSize;
            bodyStyle.fontSize = bodyFontSize;
            centeredStyle.fontSize = bodyFontSize;
            smallStyle.fontSize = smallFontSize;
            warningStyle.fontSize = headingFontSize;
        }

        private void DrawFrontMap()
        {
            var screenRect = new Rect(0f, 0f, Screen.width, Screen.height);
            DrawPanel(screenRect, new Color(0.025f, 0.026f, 0.03f, 1f));

            if (selectedLoadoutSlot >= 0)
            {
                DrawLoadoutCatalog();
                return;
            }

            if (UseWideFrontMapLayout())
            {
                DrawWideFrontMap();
                return;
            }

            var safeRect = GetSafeGuiRect();
            GetPortraitCampaignMapMetrics(
                safeRect.width,
                safeRect.height,
                out var margin,
                out _,
                out _,
                out var sectorInfoTop,
                out var slotTop,
                out var slotHeight,
                out var slotRowGap);
            var ultraCompact = safeRect.height < 450f;
            var compact = safeRect.height < 650f;
            var contentX = safeRect.x + margin;
            var width = safeRect.width - margin * 2f;
            GUI.Label(
                new Rect(
                    contentX,
                    safeRect.y + (ultraCompact ? 2f : compact ? 6f : 20f),
                    width,
                    ultraCompact ? 32f : compact ? 44f : 58f),
                "ЖИВОЙ ФРОНТ",
                titleStyle);
            GUI.Label(
                new Rect(
                    contentX,
                    safeRect.y + (ultraCompact ? 32f : compact ? 48f : 76f),
                    width,
                    ultraCompact ? 24f : compact ? 38f : 44f),
                "Тяните карту · нажмите сектор",
                centeredStyle);

            DrawCampaignMap(GetCampaignMapRect(safeRect));

            GUI.backgroundColor = Color.white;
            GUI.Label(
                new Rect(
                    contentX,
                    safeRect.y + sectorInfoTop,
                    width,
                    ultraCompact ? 30f : compact ? 38f : 46f),
                SelectedSectorSummary() +
                "\nБОЕВАЯ ЛЕНТА · выберите слот",
                centeredStyle);

            for (var i = 0; i < BeatSequenceModel.SlotCount; i++)
            {
                DrawLoadoutSlotButton(i, GetFrontMapSlotRect(i));
            }

            var actionTop =
                safeRect.y + slotTop + slotHeight * 2f + slotRowGap +
                (ultraCompact ? 8f : 12f);
            DrawFrontMapActions(
                new Rect(
                    contentX,
                    actionTop,
                    width,
                    ultraCompact ? 40f : compact ? 50f : 58f),
                new Rect(0f, 0f, 0f, 0f));
        }

        private void DrawWideFrontMap()
        {
            var safeRect = GetSafeGuiRect();
            GetWideFrontMapMetrics(
                safeRect,
                out var margin,
                out var mapRect,
                out var rightX,
                out var rightWidth,
                out var contentTop);
            var fullWidth = safeRect.width - margin * 2f;
            GUI.Label(
                new Rect(
                    safeRect.x + margin,
                    safeRect.y + 6f,
                    fullWidth,
                    38f),
                "ЖИВОЙ ФРОНТ",
                titleStyle);
            GUI.Label(
                new Rect(
                    safeRect.x + margin,
                    safeRect.y + 40f,
                    fullWidth,
                    28f),
                "Тяните карту · выберите сектор · соберите боевую ленту",
                centeredStyle);

            DrawCampaignMap(mapRect);

            DrawPanel(
                new Rect(rightX, contentTop, rightWidth, 46f),
                new Color(0.09f, 0.095f, 0.11f, 0.94f));
            GUI.Label(
                new Rect(rightX + 6f, contentTop, rightWidth - 12f, 46f),
                SelectedSectorSummary() + "\nБОЕВАЯ ЛЕНТА",
                centeredStyle);

            for (var i = 0; i < BeatSequenceModel.SlotCount; i++)
            {
                DrawLoadoutSlotButton(i, GetFrontMapSlotRect(i));
            }

            var slotBottom = GetFrontMapSlotRect(7).yMax;
            DrawFrontMapActions(
                new Rect(
                    rightX,
                    slotBottom + 12f,
                    rightWidth,
                    Mathf.Min(52f, safeRect.yMax - slotBottom - margin - 12f)),
                new Rect(0f, 0f, 0f, 0f));
        }

        private string SelectedSectorSummary()
        {
            var sector = sectors[selectedSectorIndex];
            return $"{sector.DisplayName.ToUpperInvariant()} · " +
                   $"СССР {sector.SovietControl:0}% · " +
                   $"ГЕР {sector.GermanControl:0}% · " +
                   $"ОПАСНОСТЬ {DangerLabel(sector.Danger)}";
        }

        private static string DangerLabel(float danger)
        {
            return danger < 1.08f
                ? "I"
                : danger < 1.2f
                    ? "II"
                    : "III";
        }

        private void DrawCampaignMap(Rect bounds)
        {
            DrawPanel(bounds, new Color(0.055f, 0.058f, 0.068f, 0.98f));
            var geometryRect = new Rect(
                bounds.x + 4f,
                bounds.y + 4f,
                Mathf.Max(1f, bounds.width - 8f),
                Mathf.Max(1f, bounds.height - 8f));
            DrawPanel(
                geometryRect,
                new Color(0.018f, 0.019f, 0.023f, 1f));
            lastCampaignMapGeometryRect = geometryRect;
            var mapView = GetCampaignMapView(geometryRect);

            var gridColor = new Color(0.75f, 0.77f, 0.82f, 0.055f);
            for (var index = 1; index < 5; index++)
            {
                var normalized = index / 5f;
                DrawPanel(
                    new Rect(
                        geometryRect.x +
                        geometryRect.width * normalized,
                        geometryRect.y,
                        1f,
                        geometryRect.height),
                    gridColor);
                DrawPanel(
                    new Rect(
                        geometryRect.x,
                        geometryRect.y +
                        geometryRect.height * normalized,
                        geometryRect.width,
                        1f),
                    gridColor);
            }

            if (TryConsumeCampaignMapClick(
                    geometryRect,
                    mapView,
                    out var clickedSectorIndex))
            {
                selectedSectorIndex = Mathf.Clamp(
                    clickedSectorIndex,
                    0,
                    sectors.Length - 1);
            }

            if (Event.current.type == EventType.Repaint)
            {
                DrawCampaignMapGeometry(mapView);
            }

            GUI.Label(
                new Rect(
                    geometryRect.x + 6f,
                    geometryRect.y + 2f,
                    geometryRect.width - 12f,
                    18f),
                "ЕВРОПА",
                smallStyle);

            foreach (var label in PrototypeCampaignMapLayout.Labels)
            {
                if (mapView.Zoom < label.MinimumZoom ||
                    !mapView.VisibleWorld.Contains(label.Position))
                {
                    continue;
                }

                var anchor = mapView.WorldToGui(label.Position);
                var labelRect = new Rect(
                    anchor.x - 46f,
                    anchor.y - 8f,
                    92f,
                    16f);
                if (!geometryRect.Contains(
                        new Vector2(
                            labelRect.xMin,
                            labelRect.center.y)) ||
                    !geometryRect.Contains(
                        new Vector2(
                            labelRect.xMax - 0.01f,
                            labelRect.center.y)))
                {
                    continue;
                }

                GUI.Label(
                    labelRect,
                    label.Text,
                    smallStyle);
            }

            foreach (var region in PrototypeCampaignMapLayout.Regions)
            {
                if (!mapView.VisibleWorld.Contains(
                        region.LabelPosition))
                {
                    continue;
                }

                var sector = sectors[Mathf.Clamp(
                    region.SectorIndex,
                    0,
                    sectors.Length - 1)];
                var anchor =
                    mapView.WorldToGui(region.LabelPosition);
                var selected =
                    region.SectorIndex == selectedSectorIndex;
                var labelWidth = selected
                    ? Mathf.Clamp(
                        geometryRect.width * 0.29f,
                        66f,
                        108f)
                    : Mathf.Clamp(
                        geometryRect.width * 0.2f,
                        48f,
                        72f);
                var labelHeight = selected
                    ? geometryRect.height < 150f
                        ? 24f
                        : 32f
                    : 18f;
                var labelRect = new Rect(
                    Mathf.Clamp(
                        anchor.x - labelWidth * 0.5f,
                        geometryRect.xMin + 2f,
                        geometryRect.xMax - labelWidth - 2f),
                    Mathf.Clamp(
                        anchor.y - labelHeight * 0.5f,
                        geometryRect.yMin + 2f,
                        geometryRect.yMax - labelHeight - 2f),
                    labelWidth,
                    labelHeight);
                DrawPanel(
                    labelRect,
                    new Color(0.02f, 0.021f, 0.025f, 0.74f));
                GUI.Label(
                    labelRect,
                    selected
                        ? $"◆ {sector.DisplayName.ToUpperInvariant()}\n" +
                          $"СССР {sector.SovietControl:0}%"
                        : sector.DisplayName.ToUpperInvariant(),
                    smallStyle);
            }

            DrawCampaignMapScrollIndicator(
                geometryRect,
                mapView);
        }

        private CampaignMapView GetCampaignMapView(
            Rect geometryRect)
        {
            var view =
                PrototypeCampaignMapNavigation.CreateView(
                    geometryRect,
                    campaignMapCenter,
                    campaignMapZoom);
            campaignMapCenter = view.CenterWorld;
            campaignMapZoom = view.Zoom;
            return view;
        }

        private static void DrawCampaignMapScrollIndicator(
            Rect geometryRect,
            CampaignMapView mapView)
        {
            var rail = new Rect(
                geometryRect.x + 10f,
                geometryRect.yMax - 6f,
                geometryRect.width - 20f,
                1f);
            DrawPanel(
                rail,
                new Color(0.65f, 0.67f, 0.72f, 0.18f));
            var mapBounds =
                PrototypeCampaignMapLayout.MapBounds;
            var start = Mathf.InverseLerp(
                mapBounds.xMin,
                mapBounds.xMax,
                mapView.VisibleWorld.xMin);
            var end = Mathf.InverseLerp(
                mapBounds.xMin,
                mapBounds.xMax,
                mapView.VisibleWorld.xMax);
            DrawPanel(
                new Rect(
                    Mathf.Lerp(
                        rail.xMin,
                        rail.xMax,
                        start),
                    rail.y - 0.5f,
                    Mathf.Max(
                        8f,
                        rail.width *
                        Mathf.Clamp01(end - start)),
                    2f),
                new Color(0.9f, 0.91f, 0.94f, 0.48f));

            var verticalRail = new Rect(
                geometryRect.xMax - 7f,
                geometryRect.y + 10f,
                1f,
                geometryRect.height - 20f);
            DrawPanel(
                verticalRail,
                new Color(0.65f, 0.67f, 0.72f, 0.18f));
            var verticalStart = Mathf.InverseLerp(
                mapBounds.yMin,
                mapBounds.yMax,
                mapView.VisibleWorld.yMin);
            var verticalEnd = Mathf.InverseLerp(
                mapBounds.yMin,
                mapBounds.yMax,
                mapView.VisibleWorld.yMax);
            DrawPanel(
                new Rect(
                    verticalRail.x - 0.5f,
                    Mathf.Lerp(
                        verticalRail.yMin,
                        verticalRail.yMax,
                        verticalStart),
                    2f,
                    Mathf.Max(
                        8f,
                        verticalRail.height *
                        Mathf.Clamp01(
                            verticalEnd - verticalStart))),
                new Color(0.9f, 0.91f, 0.94f, 0.48f));
        }

        private void DrawCampaignMapGeometry(
            CampaignMapView mapView)
        {
            var material = GetCampaignMapMaterial();
            if (material == null)
            {
                return;
            }

            material.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(
                0f,
                Screen.width,
                Screen.height,
                0f);

            foreach (var backdrop in PrototypeCampaignMapLayout.BackdropRegions)
            {
                var fill = backdrop.Territory switch
                {
                    CampaignTerritory.Soviet =>
                        new Color(0.3f, 0.025f, 0.06f, 0.98f),
                    CampaignTerritory.Allied =>
                        new Color(0.22f, 0.225f, 0.25f, 0.98f),
                    CampaignTerritory.Neutral =>
                        new Color(0.11f, 0.115f, 0.13f, 0.98f),
                    _ =>
                        new Color(0.15f, 0.155f, 0.175f, 0.98f)
                };
                DrawMapPolygonFill(
                    mapView,
                    backdrop.Polygon,
                    fill);
                DrawMapPolygonOutline(
                    mapView,
                    backdrop.Polygon,
                    new Color(0.66f, 0.68f, 0.72f, 0.48f),
                    1.1f);
            }

            foreach (var region in PrototypeCampaignMapLayout.Regions)
            {
                var sector = sectors[Mathf.Clamp(
                    region.SectorIndex,
                    0,
                    sectors.Length - 1)];
                DrawMapPolygonFill(
                    mapView,
                    region.Polygon,
                    new Color(0.25f, 0.255f, 0.28f, 1f));

                var splitX =
                    PrototypeCampaignMapLayout.FindVerticalSplitForRightArea(
                        region.Polygon,
                        sector.SovietControl / 100f,
                        campaignMapControlScratch);
                PrototypeCampaignMapLayout.ClipToRight(
                    region.Polygon,
                    splitX,
                    campaignMapControlScratch);
                DrawMapPolygonFill(
                    mapView,
                    campaignMapControlScratch,
                    new Color(0.63f, 0.035f, 0.105f, 1f));

                if (PrototypeCampaignMapLayout.TryGetVerticalSpan(
                        region.Polygon,
                        splitX,
                        out var minimumY,
                        out var maximumY))
                {
                    DrawMapWorldSegment(
                        mapView,
                        new Vector2(splitX, minimumY),
                        new Vector2(splitX, maximumY),
                        SoftWhite,
                        1.8f);
                }
            }

            foreach (var region in PrototypeCampaignMapLayout.Regions)
            {
                DrawMapPolygonOutline(
                    mapView,
                    region.Polygon,
                    new Color(0.82f, 0.83f, 0.86f, 0.82f),
                    1.35f);
            }

            var selectedRegion =
                PrototypeCampaignMapLayout.Regions[
                    Mathf.Clamp(
                        selectedSectorIndex,
                        0,
                        PrototypeCampaignMapLayout.Regions.Count - 1)];
            var selectedPulse =
                0.5f +
                0.5f * Mathf.Sin(Time.unscaledTime * 3.5f);
            DrawMapPolygonOutline(
                mapView,
                selectedRegion.Polygon,
                new Color(1f, 1f, 1f, Mathf.Lerp(0.72f, 1f, selectedPulse)),
                Mathf.Lerp(2.5f, 3.4f, selectedPulse));

            GL.PopMatrix();
        }

        private Material GetCampaignMapMaterial()
        {
            if (campaignMapMaterial != null)
            {
                return campaignMapMaterial;
            }

            var shader = Resources.Load<Shader>("CampaignMapVector");
            if (shader == null)
            {
                return null;
            }

            campaignMapMaterial = new Material(shader)
            {
                name = "Prototype Campaign Map Vector Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            return campaignMapMaterial;
        }

        private bool TryConsumeCampaignMapClick(
            Rect geometryRect,
            CampaignMapView mapView,
            out int sectorIndex)
        {
            sectorIndex = -1;
            if (consumedPointerFrame == Time.frameCount ||
                Time.frameCount <=
                campaignMapSuppressClickThroughFrame)
            {
                return false;
            }

            var currentEvent = Event.current;
            var hasImmediateMouseRelease =
                !campaignMapDragActive &&
                campaignMapMaximumDragDistance <=
                CampaignMapDragThreshold &&
                currentEvent != null &&
                currentEvent.type == EventType.MouseUp &&
                currentEvent.button == 0 &&
                geometryRect.Contains(currentEvent.mousePosition);
            var hasQueuedRelease =
                queuedPointerClickPending &&
                geometryRect.Contains(queuedPointerClickPosition);
            if (!hasImmediateMouseRelease && !hasQueuedRelease)
            {
                return false;
            }

            var clickPosition = hasImmediateMouseRelease
                ? currentEvent.mousePosition
                : queuedPointerClickPosition;
            var worldPoint =
                mapView.GuiToWorld(clickPosition);
            sectorIndex =
                PrototypeCampaignMapLayout.FindSectorIndex(
                    worldPoint);
            if (sectorIndex < 0)
            {
                return false;
            }

            consumedPointerFrame = Time.frameCount;
            queuedPointerClickPending = false;
            if (hasImmediateMouseRelease)
            {
                currentEvent.Use();
            }

            return true;
        }

        private void DrawMapPolygonFill(
            CampaignMapView mapView,
            IReadOnlyList<Vector2> polygon,
            Color color)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return;
            }

            PrototypeCampaignMapLayout.ClipToRect(
                polygon,
                mapView.VisibleWorld,
                campaignMapClipScratch,
                campaignMapClipWork);
            if (campaignMapClipScratch.Count < 3)
            {
                return;
            }

            var first = mapView.WorldToGui(
                campaignMapClipScratch[0]);
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            for (var index = 1;
                 index < campaignMapClipScratch.Count - 1;
                 index++)
            {
                var second = mapView.WorldToGui(
                    campaignMapClipScratch[index]);
                var third = mapView.WorldToGui(
                    campaignMapClipScratch[index + 1]);
                GL.Vertex3(first.x, first.y, 0f);
                GL.Vertex3(second.x, second.y, 0f);
                GL.Vertex3(third.x, third.y, 0f);
            }
            GL.End();
        }

        private static void DrawMapPolygonOutline(
            CampaignMapView mapView,
            IReadOnlyList<Vector2> polygon,
            Color color,
            float thickness)
        {
            if (polygon == null || polygon.Count < 2)
            {
                return;
            }

            for (var index = 0; index < polygon.Count; index++)
            {
                DrawMapWorldSegment(
                    mapView,
                    polygon[index],
                    polygon[(index + 1) % polygon.Count],
                    color,
                    thickness);
            }
        }

        private static void DrawMapWorldSegment(
            CampaignMapView mapView,
            Vector2 firstWorld,
            Vector2 secondWorld,
            Color color,
            float thickness)
        {
            if (!PrototypeCampaignMapLayout.TryClipSegmentToRect(
                    firstWorld,
                    secondWorld,
                    mapView.VisibleWorld,
                    out var clippedFirst,
                    out var clippedSecond))
            {
                return;
            }

            DrawMapSegment(
                mapView.WorldToGui(clippedFirst),
                mapView.WorldToGui(clippedSecond),
                color,
                thickness);
        }

        private static void DrawMapSegment(
            Vector2 first,
            Vector2 second,
            Color color,
            float thickness)
        {
            var direction = second - first;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var normal = new Vector2(
                -direction.y,
                direction.x).normalized *
                (thickness * 0.5f);
            var firstLeft = first - normal;
            var firstRight = first + normal;
            var secondLeft = second - normal;
            var secondRight = second + normal;

            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            GL.Vertex3(firstLeft.x, firstLeft.y, 0f);
            GL.Vertex3(firstRight.x, firstRight.y, 0f);
            GL.Vertex3(secondRight.x, secondRight.y, 0f);
            GL.Vertex3(firstLeft.x, firstLeft.y, 0f);
            GL.Vertex3(secondRight.x, secondRight.y, 0f);
            GL.Vertex3(secondLeft.x, secondLeft.y, 0f);
            GL.End();
        }

        private void DrawLoadoutSlotButton(int index, Rect rect)
        {
            GUI.backgroundColor = selectedLoadoutSlot == index
                ? new Color(0.8f, 0.05f, 0.13f)
                : baseSequence[index] == BeatActionType.Rest
                    ? new Color(0.18f, 0.19f, 0.21f)
                    : new Color(0.34f, 0.35f, 0.38f);

            if (PrototypeButton(
                    rect,
                    $"{index + 1}  {BeatActionNames.Short(baseSequence[index])}\n" +
                    BeatActionNames.Pattern(baseSequence[index])))
            {
                selectedLoadoutSlot = index;
            }
        }

        private void DrawLoadoutCatalog()
        {
            if (selectedLoadoutSlot < 0 ||
                selectedLoadoutSlot >= BeatSequenceModel.SlotCount)
            {
                selectedLoadoutSlot = -1;
                return;
            }

            DrawPanel(
                new Rect(0f, 0f, Screen.width, Screen.height),
                new Color(0.025f, 0.026f, 0.03f, 0.97f));

            GetSafeGuiVerticalBounds(
                out var safeTop,
                out _,
                out var safeContentHeight);
            var compact = safeContentHeight < 520f;
            var columns = Screen.width < 520f ? 2 : 3;
            var margin = Mathf.Max(14f, Screen.width * 0.06f);
            var width = Screen.width - margin * 2f;
            var titleHeight = compact ? 34f : 48f;
            var subtitleHeight = compact ? 28f : 38f;
            var gridTop = safeTop + margin + titleHeight + subtitleHeight + 8f;
            var gap = compact ? 6f : 10f;
            var cardHeight = compact ? 48f : 66f;
            var cardWidth = (width - gap * (columns - 1)) / columns;
            var rows = Mathf.CeilToInt(LoadoutCatalogActions.Length / (float)columns);

            GUI.Label(
                new Rect(margin, safeTop + margin, width, titleHeight),
                $"СЛОТ {selectedLoadoutSlot + 1}",
                titleStyle);
            GUI.Label(
                new Rect(
                    margin,
                    safeTop + margin + titleHeight,
                    width,
                    subtitleHeight),
                $"Сейчас: {BeatActionNames.Long(baseSequence[selectedLoadoutSlot])}",
                centeredStyle);

            for (var i = 0; i < LoadoutCatalogActions.Length; i++)
            {
                var action = LoadoutCatalogActions[i];
                var column = i % columns;
                var row = i / columns;
                var rect = new Rect(
                    margin + column * (cardWidth + gap),
                    gridTop + row * (cardHeight + gap),
                    cardWidth,
                    cardHeight);
                GUI.backgroundColor = action == baseSequence[selectedLoadoutSlot]
                    ? new Color(0.78f, 0.04f, 0.12f)
                    : action == BeatActionType.Rest
                        ? new Color(0.18f, 0.19f, 0.21f)
                        : new Color(0.34f, 0.35f, 0.38f);

                if (!PrototypeButton(
                        rect,
                        $"{LoadoutCatalogTitle(action)}\n" +
                        BeatActionNames.Pattern(action)))
                {
                    continue;
                }

                baseSequence[selectedLoadoutSlot] = action;
                selectedLoadoutSlot = -1;
                GUI.backgroundColor = Color.white;
                return;
            }

            var cancelTop = gridTop + rows * (cardHeight + gap) + 4f;
            GUI.backgroundColor = new Color(0.22f, 0.23f, 0.26f);
            if (PrototypeButton(
                    new Rect(
                        margin,
                        cancelTop,
                        width,
                        compact ? 38f : 48f),
                    "ОТМЕНА"))
            {
                selectedLoadoutSlot = -1;
            }

            GUI.backgroundColor = Color.white;
        }

        private static string LoadoutCatalogTitle(BeatActionType action)
        {
            return action switch
            {
                BeatActionType.Ppsh => "ППШ",
                BeatActionType.Rifle => "ВИНТОВКА",
                BeatActionType.Grenade => "ГРАНАТА",
                BeatActionType.Dash => "РЫВОК",
                _ => "ПУСТОЙ СЛОТ"
            };
        }

        private void DrawFrontMapActions(Rect startRect, Rect resetRect)
        {
            GUI.backgroundColor = new Color(0.74f, 0.05f, 0.13f);
            var startLabel = startRect.height <= 42f
                ? $"НАЧАТЬ · {sectors[selectedSectorIndex].DisplayName}"
                : $"НАЧАТЬ ВЫЛАЗКУ · {sectors[selectedSectorIndex].DisplayName}";
            if (PrototypeButton(
                    startRect,
                    startLabel))
            {
                StartMission(selectedSectorIndex);
            }

            if (resetRect.width > 1f && resetRect.height > 1f)
            {
                GUI.backgroundColor = new Color(0.25f, 0.26f, 0.29f);
                if (PrototypeButton(resetRect, "Сбросить тестовые проценты карты"))
                {
                    ResetCampaign();
                }
            }

            GUI.backgroundColor = Color.white;
        }

        private static bool UseWideFrontMapLayout()
        {
            var safeRect = GetSafeGuiRect();
            return UseWideFrontMapLayout(
                safeRect.width,
                safeRect.height);
        }

        private static bool UseWideFrontMapLayout(float screenWidth, float screenHeight)
        {
            return screenWidth >= 560f &&
                   screenHeight >= 340f &&
                   screenWidth > screenHeight * 1.15f;
        }

        private static void GetPortraitCampaignMapMetrics(
            float screenWidth,
            float screenHeight,
            out float margin,
            out float mapTop,
            out float mapHeight,
            out float sectorInfoTop,
            out float slotTop,
            out float slotHeight,
            out float slotRowGap)
        {
            var ultraCompact = screenHeight < 450f;
            var compact = screenHeight < 650f;
            margin = Mathf.Max(10f, screenWidth * 0.04f);
            mapTop = ultraCompact ? 58f : compact ? 92f : 130f;
            slotHeight = ultraCompact ? 32f : compact ? 48f : 58f;
            slotRowGap = ultraCompact ? 3f : compact ? 4f : 6f;
            var sectorInfoHeight =
                ultraCompact ? 30f : compact ? 38f : 46f;
            var actionHeight =
                ultraCompact ? 40f : compact ? 50f : 58f;
            var footerHeight =
                6f +
                sectorInfoHeight +
                slotHeight * 2f +
                slotRowGap +
                (ultraCompact ? 8f : 12f) +
                actionHeight +
                8f;
            var availableMapHeight =
                screenHeight - mapTop - footerHeight;
            mapHeight = Mathf.Clamp(
                availableMapHeight,
                ultraCompact ? 96f : compact ? 130f : 180f,
                ultraCompact ? 180f : compact ? 260f : 360f);
            sectorInfoTop = mapTop + mapHeight + 6f;
            slotTop = sectorInfoTop + sectorInfoHeight;
        }

        private static void GetWideFrontMapMetrics(
            Rect safeRect,
            out float margin,
            out Rect mapRect,
            out float rightX,
            out float rightWidth,
            out float contentTop)
        {
            margin = Mathf.Max(12f, safeRect.width * 0.025f);
            contentTop = safeRect.y + 74f;
            var mapWidth = Mathf.Clamp(
                safeRect.width * 0.56f,
                300f,
                safeRect.width * 0.62f);
            mapRect = new Rect(
                safeRect.x + margin,
                contentTop,
                mapWidth,
                Mathf.Max(
                    120f,
                    safeRect.yMax - contentTop - margin));
            rightX = mapRect.xMax + margin;
            rightWidth = Mathf.Max(
                1f,
                safeRect.xMax - rightX - margin);
        }

        private static Rect GetCampaignMapRect(Rect safeRect)
        {
            if (UseWideFrontMapLayout(
                    safeRect.width,
                    safeRect.height))
            {
                GetWideFrontMapMetrics(
                    safeRect,
                    out _,
                    out var mapRect,
                    out _,
                    out _,
                    out _);
                return mapRect;
            }

            GetPortraitCampaignMapMetrics(
                safeRect.width,
                safeRect.height,
                out var margin,
                out var mapTop,
                out var mapHeight,
                out _,
                out _,
                out _,
                out _);
            return new Rect(
                safeRect.x + margin,
                safeRect.y + mapTop,
                safeRect.width - margin * 2f,
                mapHeight);
        }

        private static Rect GetSafeGuiRect()
        {
            var safeArea = Screen.safeArea;
            if (safeArea.width <= 0f || safeArea.height <= 0f)
            {
                return new Rect(
                    0f,
                    0f,
                    Screen.width,
                    Screen.height);
            }

            return new Rect(
                safeArea.xMin,
                Screen.height - safeArea.yMax,
                safeArea.width,
                safeArea.height);
        }

        private static void GetSafeGuiVerticalBounds(
            out float safeTop,
            out float safeBottom,
            out float safeContentHeight)
        {
            var safeRect = GetSafeGuiRect();
            safeTop = safeRect.y;
            safeBottom = Mathf.Max(
                0f,
                Screen.height - safeRect.yMax);
            safeContentHeight = safeRect.height;
        }

        private static Rect GetFrontMapSlotRect(int index)
        {
            return GetFrontMapSlotRect(index, GetSafeGuiRect());
        }

        private static Rect GetFrontMapSlotRect(
            int index,
            float screenWidth,
            float screenHeight)
        {
            return GetFrontMapSlotRect(
                index,
                new Rect(0f, 0f, screenWidth, screenHeight));
        }

        private static Rect GetFrontMapSlotRect(int index, Rect safeRect)
        {
            if (UseWideFrontMapLayout(
                    safeRect.width,
                    safeRect.height))
            {
                GetWideFrontMapMetrics(
                    safeRect,
                    out _,
                    out _,
                    out var rightX,
                    out var rightWidth,
                    out var contentTop);
                const float gap = 6f;
                var slotWidth = (rightWidth - gap * 3f) / 4f;
                var column = index % 4;
                var row = index / 4;
                return new Rect(
                    rightX + column * (slotWidth + gap),
                    contentTop + 54f + row * 64f,
                    slotWidth,
                    58f);
            }

            GetPortraitCampaignMapMetrics(
                safeRect.width,
                safeRect.height,
                out var portraitMargin,
                out _,
                out _,
                out _,
                out var slotTop,
                out var portraitSlotHeight,
                out var portraitRowGap);
            var portraitWidth =
                safeRect.width - portraitMargin * 2f;
            const float portraitGap = 4f;
            var portraitSlotWidth =
                (portraitWidth - portraitGap * 3f) / 4f;
            var portraitColumn = index % 4;
            var portraitRow = index / 4;
            return new Rect(
                safeRect.x +
                portraitMargin +
                portraitColumn *
                (portraitSlotWidth + portraitGap),
                safeRect.y +
                slotTop +
                portraitRow *
                (portraitSlotHeight + portraitRowGap),
                portraitSlotWidth,
                portraitSlotHeight);
        }

        private void DrawMissionHud()
        {
            GetSafeGuiVerticalBounds(
                out var safeTop,
                out var safeBottom,
                out var safeContentHeight);
            var margin = Mathf.Max(12f, Screen.width * 0.03f);
            var width = Screen.width - margin * 2f;
            var compactHud = safeContentHeight < 480f;
            var narrowHud = Screen.width < 320f;
            var panelHeight = narrowHud ? 82f : compactHud ? 104f : 124f;
            var panelTop = safeTop + 12f;
            DrawPanel(
                new Rect(margin, panelTop, width, panelHeight),
                new Color(0.035f, 0.036f, 0.042f, 0.9f));

            GUI.Label(
                new Rect(
                    margin + 14f,
                    panelTop + 6f,
                    width - 28f,
                    narrowHud ? 18f : compactHud ? 22f : 30f),
                narrowHud
                    ? $"{FormatTime(runElapsed)} · УР {level}"
                    : $"{sectors[selectedSectorIndex].DisplayName} · {FormatTime(runElapsed)} · уровень {level}",
                headingStyle);

            DrawProgressBar(
                new Rect(
                    margin + 14f,
                    panelTop + (narrowHud ? 26f : compactHud ? 30f : 40f),
                    width - 28f,
                    18f),
                player == null ? 0f : player.Health / player.MaxHealth,
                new Color(0.74f, 0.04f, 0.12f),
                $"БОЕЦ {(player == null ? 0f : player.Health):0}/{(player == null ? 0f : player.MaxHealth):0}");

            GUI.Label(
                new Rect(
                    margin + 14f,
                    panelTop + (narrowHud ? 47f : compactHud ? 51f : 64f),
                    width - 28f,
                    narrowHud ? 20f : compactHud ? 38f : 48f),
                BoundaryWarningActive
                    ? BoundaryObjectiveText()
                    : narrowHud
                        ? CompactMissionObjectiveText()
                        : MissionObjectiveText(),
                BoundaryWarningActive ? warningStyle : bodyStyle);

            if (player != null && player.IsDragging)
            {
                var origin = ToGuiPosition(player.DragOrigin);
                var current = ToGuiPosition(player.DragCurrent);
                DrawPanel(new Rect(origin.x - 34f, origin.y - 34f, 68f, 68f), new Color(1f, 1f, 1f, 0.1f));
                DrawPanel(new Rect(current.x - 15f, current.y - 15f, 30f, 30f), new Color(0.9f, 0.18f, 0.14f, 0.5f));
            }

            if (BoundaryWarningActive)
            {
                DrawBoundaryWarning(
                    panelTop + panelHeight,
                    margin,
                    width,
                    narrowHud);
            }
            else
            {
                DrawMissionMinimap(margin);
            }

            var sequence = runSequence ?? baseSequence;
            const float slotGap = 3f;
            var compactSlots = Screen.width < 520f;
            var slotColumns = compactSlots ? 4 : BeatSequenceModel.SlotCount;
            var slotRows = compactSlots ? 2 : 1;
            var slotHeight = compactSlots ? 46f : 58f;
            var slotWidth = (width - slotGap * (slotColumns - 1)) / slotColumns;
            var totalSlotHeight = slotRows * slotHeight + (slotRows - 1) * slotGap;
            var slotY =
                Screen.height - safeBottom - totalSlotHeight - 16f;

            for (var i = 0; i < BeatSequenceModel.SlotCount; i++)
            {
                var active = currentBeatIndex == i;
                var color = active
                    ? new Color(0.82f, 0.04f, 0.13f, 0.96f)
                    : sequence[i] == BeatActionType.Rest
                        ? new Color(0.12f, 0.125f, 0.14f, 0.86f)
                        : new Color(0.29f, 0.3f, 0.33f, 0.9f);
                var column = i % slotColumns;
                var row = i / slotColumns;
                var rect = new Rect(
                    margin + column * (slotWidth + slotGap),
                    slotY + row * (slotHeight + slotGap),
                    slotWidth,
                    slotHeight);
                DrawPanel(rect, color);
                GUI.Label(
                    rect,
                    $"{i + 1}  {BeatActionNames.Short(sequence[i])}\n" +
                    BeatActionNames.Pattern(sequence[i]),
                    smallStyle);
            }
        }

        private string BoundaryObjectiveText()
        {
            var returnDirection = DirectionBackIntoCombatArea();
            return $"{DirectionArrow(returnDirection)} ВЕРНИТЕСЬ В ЗОНУ · " +
                   $"{BoundaryTimeRemaining:0.0}";
        }

        private void DrawBoundaryWarning(
            float hudBottom,
            float margin,
            float width,
            bool narrow)
        {
            var urgency = 1f - Mathf.Clamp01(
                BoundaryTimeRemaining / CombatBoundaryGraceDuration);
            var pulseSpeed = Mathf.Lerp(5f, 12f, urgency);
            var pulse = 0.5f + 0.5f *
                Mathf.Sin(Time.unscaledTime * pulseSpeed);
            var warningHeight = narrow ? 58f : 72f;
            var warningRect = new Rect(
                margin,
                hudBottom + 7f,
                width,
                warningHeight);
            DrawPanel(
                warningRect,
                new Color(
                    SovietRed.r,
                    SovietRed.g,
                    SovietRed.b,
                    Mathf.Lerp(0.78f, 0.96f, urgency)));

            var returnDirection = DirectionBackIntoCombatArea();
            var returnDistance = returnDirection.magnitude;
            var countdown = Mathf.CeilToInt(BoundaryTimeRemaining);
            var label = narrow
                ? $"ВНЕ ЗОНЫ · {countdown:00}\n" +
                  $"{DirectionArrow(returnDirection)} НАЗАД · {returnDistance:0} м"
                : $"ВНЕ ЗОНЫ ОПЕРАЦИИ · {countdown:00}\n" +
                  $"{DirectionArrow(returnDirection)} ВЕРНИТЕСЬ · {returnDistance:0} м";
            var previousFontSize = warningStyle.fontSize;
            warningStyle.fontSize = narrow
                ? Mathf.Max(14, previousFontSize)
                : Mathf.Max(18, previousFontSize);
            GUI.Label(warningRect, label, warningStyle);
            warningStyle.fontSize = previousFontSize;

            var frameAlpha = Mathf.Lerp(0.18f, 0.52f, urgency) *
                             Mathf.Lerp(0.72f, 1f, pulse);
            var frameThickness = Mathf.Lerp(3f, 8f, urgency);
            var frameColor = new Color(
                SovietRed.r,
                SovietRed.g,
                SovietRed.b,
                frameAlpha);
            DrawPanel(
                new Rect(0f, 0f, Screen.width, frameThickness),
                frameColor);
            DrawPanel(
                new Rect(
                    0f,
                    Screen.height - frameThickness,
                    Screen.width,
                    frameThickness),
                frameColor);
            DrawPanel(
                new Rect(0f, 0f, frameThickness, Screen.height),
                frameColor);
            DrawPanel(
                new Rect(
                    Screen.width - frameThickness,
                    0f,
                    frameThickness,
                    Screen.height),
                frameColor);
        }

        private void DrawMissionMinimap(float margin)
        {
            GetSafeGuiVerticalBounds(
                out var safeTop,
                out var safeBottom,
                out var safeContentHeight);
            var compact = Screen.width < 320f || safeContentHeight < 480f;
            const float worldAspect = 1f;
            float mapTop;
            float maximumMapHeight;
            float desiredMapWidth;
            float labelHeight;

            if (compact)
            {
                var hudPanelHeight = Screen.width < 320f ? 82f : 104f;
                const float compactSlotHeight = 46f;
                const float compactSlotGap = 3f;
                var compactSlotRows = Screen.width < 520f ? 2 : 1;
                var totalSlotHeight =
                    compactSlotRows * compactSlotHeight +
                    (compactSlotRows - 1) * compactSlotGap;
                var slotTop =
                    Screen.height - safeBottom -
                    totalSlotHeight - 16f;
                mapTop = safeTop + 12f + hudPanelHeight + 6f;
                maximumMapHeight = slotTop - mapTop - 6f;
                desiredMapWidth = Mathf.Clamp(Screen.width * 0.23f, 36f, 52f);
                labelHeight = 15f;
            }
            else
            {
                mapTop = safeTop + 146f;
                maximumMapHeight =
                    Screen.height - safeBottom - mapTop - 88f;
                desiredMapWidth = Mathf.Clamp(Screen.width * 0.22f, 94f, 148f);
                labelHeight = 24f;
            }

            if (maximumMapHeight < 44f)
            {
                return;
            }

            var mapHeight = Mathf.Min(
                desiredMapWidth * worldAspect,
                maximumMapHeight - labelHeight);
            var mapWidth = mapHeight / worldAspect;
            var framePadding = compact ? 3f : 4f;
            var outerRect = new Rect(
                Screen.width - margin - mapWidth - framePadding * 2f,
                mapTop,
                mapWidth + framePadding * 2f,
                mapHeight + labelHeight + framePadding);
            DrawPanel(outerRect, new Color(0.035f, 0.036f, 0.042f, 0.9f));
            GUI.Label(
                new Rect(
                    outerRect.x + framePadding,
                    outerRect.y,
                    outerRect.width - framePadding * 2f,
                    labelHeight),
                compact ? "↑ С" : "РАДАР  ↑ С",
                smallStyle);

            var mapRect = new Rect(
                outerRect.x + framePadding,
                outerRect.y + labelHeight,
                mapWidth,
                mapHeight);
            DrawPanel(mapRect, new Color(0.12f, 0.125f, 0.14f, 0.96f));
            DrawPanel(
                new Rect(mapRect.center.x, mapRect.y, 1f, mapRect.height),
                new Color(0.8f, 0.81f, 0.84f, 0.16f));
            DrawPanel(
                new Rect(mapRect.x, mapRect.center.y, mapRect.width, 1f),
                new Color(0.8f, 0.81f, 0.84f, 0.16f));

            foreach (var node in objectiveNodes)
            {
                if (node == null)
                {
                    continue;
                }

                var position = WorldToMinimap(node.transform.position, mapRect);
                if (node == trackedObjective && !node.Completed)
                {
                    DrawMinimapDot(
                        position,
                        compact ? 7f : 12f,
                        new Color(0.82f, 0.03f, 0.12f, 0.5f));
                }

                DrawMinimapDot(
                    position,
                    compact
                        ? node.Completed ? 3f : 4f
                        : node.Completed ? 6f : 7f,
                    node.Completed
                        ? new Color(0.38f, 0.39f, 0.42f)
                        : new Color(0.92f, 0.93f, 0.96f));
            }

            if (objectiveComplete || phase == PrototypePhase.Extraction ||
                phase == PrototypePhase.Departing)
            {
                DrawMinimapDot(
                    WorldToMinimap(extractionPosition, mapRect),
                    compact ? 5f : 9f,
                    new Color(0.82f, 0.03f, 0.12f));
            }

            if (player == null)
            {
                return;
            }

            var playerPosition = WorldToMinimap(player.transform.position, mapRect);
            DrawMinimapDot(
                playerPosition,
                compact ? 5f : 8f,
                new Color(0.82f, 0.03f, 0.12f));
            var directionPosition = WorldToMinimap(
                player.transform.position + player.FacingDirection * 6f,
                mapRect);
            DrawMinimapDot(directionPosition, compact ? 2f : 4f, Color.white);
        }

        private Vector2 WorldToMinimap(Vector3 worldPosition, Rect mapRect)
        {
            var radarCenter = player != null
                ? player.transform.position
                : Vector3.zero;
            var offset = worldPosition - radarCenter;
            var normalizedOffset = Vector2.ClampMagnitude(
                new Vector2(offset.x, offset.z) /
                (MissionRadarRange * 2f),
                0.46f);
            var normalizedX = 0.5f + normalizedOffset.x;
            var normalizedZ = 0.5f + normalizedOffset.y;
            return new Vector2(
                Mathf.Lerp(mapRect.xMin, mapRect.xMax, normalizedX),
                Mathf.Lerp(mapRect.yMax, mapRect.yMin, normalizedZ));
        }

        private static void DrawMinimapDot(Vector2 position, float size, Color color)
        {
            DrawPanel(
                new Rect(position.x - size * 0.5f, position.y - size * 0.5f, size, size),
                color);
        }

        private string CompactMissionObjectiveText()
        {
            if (phase == PrototypePhase.Departing)
            {
                return "ЭВАКУАЦИЯ УСПЕШНА";
            }

            if (phase == PrototypePhase.Extraction)
            {
                var extractionOffset = player == null
                    ? Vector3.forward
                    : extractionPosition - player.transform.position;
                return $"{DirectionArrow(extractionOffset)} ЭВАК · {extractionOffset.magnitude:0} м";
            }

            if (trackedObjective == null || trackedObjective.Completed)
            {
                return $"УЗЛЫ {completedObjectives}/{objectiveNodes.Count}";
            }

            var objectiveOffset = player == null
                ? Vector3.forward
                : trackedObjective.transform.position - player.transform.position;
            return $"{DirectionArrow(objectiveOffset)} УЗЕЛ {completedObjectives}/{objectiveNodes.Count} · " +
                   $"{objectiveOffset.magnitude:0} м";
        }

        private string MissionObjectiveText()
        {
            if (phase == PrototypePhase.Departing)
            {
                return "ЭВАКУАЦИЯ УСПЕШНА · самолёт покидает сектор";
            }

            if (phase == PrototypePhase.Extraction)
            {
                if (!extractionCalled)
                {
                    return "ЗАДАЧА ВЫПОЛНЕНА · доберитесь до красного маяка";
                }

                if (!extractionReady)
                {
                    return $"ЭВАКУАЦИЯ В ПУТИ · держитесь ещё {extractionCountdown:0.0} сек.";
                }

                return $"САМОЛЁТ ПРИБЫЛ · зайдите в зону посадки {boardingProgress:0.0}/1.5";
            }

            if (trackedObjective == null || trackedObjective.Completed)
            {
                return $"РАДИОУЗЛЫ {completedObjectives}/{objectiveNodes.Count}";
            }

            var objectiveOffset = player == null
                ? Vector3.zero
                : trackedObjective.transform.position - player.transform.position;
            var distanceMeters = objectiveOffset.magnitude;
            var direction = player == null
                ? Vector3.forward
                : objectiveOffset;
            return $"{DirectionArrow(direction)} РАДИОУЗЛЫ {completedObjectives}/{objectiveNodes.Count} · " +
                   $"{trackedObjective.DisplayName}: {distanceMeters:0} м · " +
                   $"{trackedObjective.NormalizedProgress * 100f:0}%";
        }

        private static string DirectionArrow(Vector3 direction)
        {
            var angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            if (angle < 0f)
            {
                angle += 360f;
            }

            var directionIndex = Mathf.RoundToInt(angle / 45f) % 8;
            return directionIndex switch
            {
                0 => "↑",
                1 => "↗",
                2 => "→",
                3 => "↘",
                4 => "↓",
                5 => "↙",
                6 => "←",
                _ => "↖"
            };
        }

        private void DrawUpgradeDraft()
        {
            DrawPanel(
                new Rect(0f, 0f, Screen.width, Screen.height),
                new Color(0.015f, 0.015f, 0.019f, 0.92f));

            var margin = Mathf.Max(22f, Screen.width * 0.06f);
            var width = Screen.width - margin * 2f;
            var compact = Screen.height < 650f;
            var titleY = compact ? 8f : Screen.height * 0.16f;
            var titleHeight = compact ? 44f : 58f;
            var choiceTop = compact ? 90f : Screen.height * 0.3f;
            var choiceGap = compact ? 8f : 18f;
            var choiceHeight = compact
                ? Mathf.Clamp(
                    (Screen.height - choiceTop - 16f - choiceGap * 2f) / 3f,
                    72f,
                    94f)
                : 94f;

            GUI.Label(new Rect(margin, titleY, width, titleHeight), $"УРОВЕНЬ {level}", titleStyle);
            GUI.Label(
                new Rect(margin, titleY + titleHeight, width, compact ? 30f : 40f),
                "Выберите усиление боевой ленты",
                centeredStyle);

            for (var i = 0; i < upgradeChoices.Length; i++)
            {
                var choice = upgradeChoices[i];
                var rect = new Rect(
                    margin,
                    choiceTop + i * (choiceHeight + choiceGap),
                    width,
                    choiceHeight);
                GUI.backgroundColor = new Color(0.66f, 0.04f, 0.11f);
                if (PrototypeButton(rect, $"{UpgradeTitle(choice)}\n{UpgradeDescription(choice)}"))
                {
                    ApplyUpgrade(choice);
                }
            }

            GUI.backgroundColor = Color.white;
        }

        private void DrawDebrief()
        {
            DrawPanel(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.05f, 0.05f, 0.06f, 1f));
            var margin = Mathf.Max(24f, Screen.width * 0.07f);
            var width = Screen.width - margin * 2f;
            var debriefTitle = boundaryDeath
                ? "ВЫ ПОКИНУЛИ ЗОНУ ОПЕРАЦИИ"
                : extracted
                    ? "ЭВАКУАЦИЯ УСПЕШНА"
                    : objectiveComplete
                        ? "БОЕЦ НЕ ЭВАКУИРОВАН"
                        : "ЗАДАЧА ПРОВАЛЕНА";

            GUI.Label(
                new Rect(margin, Screen.height * 0.12f, width, 64f),
                debriefTitle,
                titleStyle);

            var objectiveContribution = objectiveComplete ? 2f : 0f;
            var extractionContribution = extracted ? 3f : 0f;
            var report =
                $"Сектор: {sectors[selectedSectorIndex].DisplayName}\n\n" +
                $"Радиоузлы: {completedObjectives}/{objectiveNodes.Count}  +{objectiveContribution:0.0}%\n" +
                $"Эвакуация: {(extracted ? "успешна" : "нет")}  +{extractionContribution:0.0}%\n" +
                (boundaryDeath ? "Причина: выход из зоны операции\n" : string.Empty) +
                $"Уничтожено врагов: {kills}\n\n" +
                $"Вклад в сектор: +{lastContribution:0.0}%\n" +
                $"СССР {lastSectorBefore:0.0}%  →  {sectors[selectedSectorIndex].SovietControl:0.0}%";

            GUI.Label(
                new Rect(margin, Screen.height * 0.26f, width, Screen.height * 0.38f),
                report,
                headingStyle);

            GUI.backgroundColor = new Color(0.72f, 0.04f, 0.12f);
            if (PrototypeButton(
                    new Rect(margin, Screen.height - 164f, width, 58f),
                    "ВЕРНУТЬСЯ НА КАРТУ"))
            {
                ReturnToFrontMap();
            }

            GUI.backgroundColor = new Color(0.27f, 0.28f, 0.31f);
            if (PrototypeButton(
                    new Rect(margin, Screen.height - 96f, width, 46f),
                    "ПОВТОРИТЬ ВЫЛАЗКУ"))
            {
                StartMission(selectedSectorIndex);
            }

            GUI.backgroundColor = Color.white;
        }

        private void DrawProgressBar(Rect rect, float normalized, Color fill, string label)
        {
            DrawPanel(rect, new Color(0.11f, 0.112f, 0.125f, 0.96f));
            var inner = new Rect(rect.x + 2f, rect.y + 2f, (rect.width - 4f) * Mathf.Clamp01(normalized), rect.height - 4f);
            DrawPanel(inner, fill);
            GUI.Label(rect, label, smallStyle);
        }

        private static void DrawPanel(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private bool PrototypeButton(Rect rect, string label)
        {
            if (GUI.Button(rect, label))
            {
                consumedPointerFrame = Time.frameCount;
                queuedPointerClickPending = false;
                return true;
            }

            // Capture New Input System state from Update. Reading
            // wasReleasedThisFrame directly from OnGUI is unreliable in the macOS
            // Editor because IMGUI and Input System events use different passes.
            if (consumedPointerFrame == Time.frameCount)
            {
                return false;
            }

            if (!queuedPointerClickPending)
            {
                return false;
            }

            if (!rect.Contains(queuedPointerClickPosition))
            {
                return false;
            }

            consumedPointerFrame = Time.frameCount;
            queuedPointerClickPending = false;
            return true;
        }

        private static Vector2 ToGuiPosition(Vector2 screenPosition)
        {
            return new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        }

        private static string FormatTime(float seconds)
        {
            var total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

#if UNITY_EDITOR
        public void DebugQueueUiClick(Vector2 guiPosition)
        {
            queuedPointerClickPending = true;
            queuedPointerClickPosition = guiPosition;
        }

        public string DebugLoadoutState()
        {
            return $"selected={selectedLoadoutSlot}; sequence={string.Join(",", baseSequence.Slots)}";
        }

        public string DebugGuiState()
        {
            return $"gui={lastGuiWidth}x{lastGuiHeight}; repaints={guiRepaintCount}; pending={queuedPointerClickPending}";
        }

        public void DebugQueueLoadoutSlotClick(int index)
        {
            DebugQueueUiClick(GetFrontMapSlotRect(index, lastGuiWidth, lastGuiHeight).center);
        }

        public void DebugQueueCampaignSectorClick(int index)
        {
            if (index < 0 ||
                index >= PrototypeCampaignMapLayout.Regions.Count)
            {
                return;
            }

            var mapRect = GetCampaignMapRect(GetSafeGuiRect());
            var geometryRect = new Rect(
                mapRect.x + 4f,
                mapRect.y + 4f,
                Mathf.Max(1f, mapRect.width - 8f),
                Mathf.Max(1f, mapRect.height - 8f));
            var target =
                PrototypeCampaignMapLayout
                    .Regions[index]
                    .LabelPosition;
            campaignMapCenter = target;
            var mapView = GetCampaignMapView(geometryRect);
            DebugQueueUiClick(
                mapView.WorldToGui(target));
        }

        public string DebugCampaignState()
        {
            var mapView = lastCampaignMapGeometryRect.width > 1f
                ? GetCampaignMapView(lastCampaignMapGeometryRect)
                : default;
            return $"selected={selectedSectorIndex}; " +
                   $"sector={sectors[selectedSectorIndex].DisplayName}; " +
                   $"control={sectors[selectedSectorIndex].SovietControl:0.0}; " +
                   $"mapCenter={campaignMapCenter.x:0.0},{campaignMapCenter.y:0.0}; " +
                   $"zoom={campaignMapZoom:0.00}; " +
                   $"visible={mapView.VisibleWorld}; " +
                   $"renderer={(campaignMapMaterial == null ? "cold" : "ready")}";
        }

        public void DebugPanCampaignMap(Vector2 guiDelta)
        {
            if (lastCampaignMapGeometryRect.width <= 1f)
            {
                return;
            }

            campaignMapCenter =
                PrototypeCampaignMapNavigation.PanCenter(
                    GetCampaignMapView(
                        lastCampaignMapGeometryRect),
                    guiDelta);
        }

        public string DebugRhythmState()
        {
            var secondsToNextBeat = transportSuspended
                ? -1d
                : PrototypeBeatTransportMath.BeatTime(
                      beatEpochDspTime,
                      lastTriggeredBeatOrdinal + 1L,
                      BeatInterval) -
                  AudioSettings.dspTime;
            return $"suspended={transportSuspended}; " +
                   $"beat={currentBeatIndex}; " +
                   $"ordinal={lastTriggeredBeatOrdinal}; " +
                   $"musicPlaying={musicAudioSource != null && musicAudioSource.isPlaying}; " +
                   $"musicPitch={(musicAudioSource != null ? musicAudioSource.pitch : 0f):0.00}; " +
                   $"next={secondsToNextBeat:0.000}";
        }

        public void DebugTriggerBeatOrdinal(long ordinal)
        {
            if (runSequence == null ||
                player == null ||
                !player.IsAlive ||
                ordinal < 0L)
            {
                return;
            }

            lastTriggeredBeatOrdinal = ordinal;
            TickBeat(ordinal);
        }

        public void DebugChooseLoadoutAction(BeatActionType action)
        {
            if (selectedLoadoutSlot < 0 ||
                selectedLoadoutSlot >= BeatSequenceModel.SlotCount)
            {
                return;
            }

            foreach (var catalogAction in LoadoutCatalogActions)
            {
                if (catalogAction != action)
                {
                    continue;
                }

                baseSequence[selectedLoadoutSlot] = action;
                selectedLoadoutSlot = -1;
                return;
            }
        }

        public void DebugStartMission()
        {
            StartMission(selectedSectorIndex);
        }

        public void DebugClearEnemies()
        {
            var snapshot = enemies.ToArray();
            enemies.Clear();
            foreach (var enemy in snapshot)
            {
                ReleaseEnemy(enemy);
            }

            spawnTimer = 999f;
        }

        public void DebugSpawnEnemyAt(Vector3 position)
        {
            SpawnEnemy();
            if (enemies.Count > 0)
            {
                var enemy = enemies[enemies.Count - 1];
                enemy.transform.position = ClampToArena(position);
                enemy.Initialize(this, 50f, 0f, 0f);
            }
        }

        public void DebugSuspendRhythm()
        {
            SuspendBeatTransport();
        }

        public void DebugTriggerAction(BeatActionType action)
        {
            if (IsSimulationRunning)
            {
                TriggerAction(action);
            }
        }

        public void DebugEnableDashSlash()
        {
            dashSlashEnabled = true;
        }

        public string DebugWeaponState()
        {
            var activeProjectiles = 0;
            if (worldRoot != null)
            {
                foreach (var projectile in worldRoot.GetComponentsInChildren<PrototypeProjectile>(true))
                {
                    if (projectile != null && projectile.gameObject.activeSelf)
                    {
                        activeProjectiles++;
                    }
                }
            }

            return $"shots={shotsFired}; grenades={grenadesThrown}; dashes={dashesPerformed}; " +
                   $"pendingBursts={pendingBursts.Count}; activeProjectiles={activeProjectiles}; " +
                   $"slash={dashSlashEnabled}; " +
                   $"facing={(player == null ? Vector3.zero : player.FacingDirection)}; " +
                   $"tracked={(trackedObjective == null ? "none" : trackedObjective.DisplayName)}";
        }

        public void DebugOpenUpgradeDraft()
        {
            if (phase != PrototypePhase.Mission && phase != PrototypePhase.Extraction)
            {
                StartMission(selectedSectorIndex);
            }

            OpenUpgradeDraft();
        }

        public void DebugExplodeFirstDestructible()
        {
            foreach (var destructible in destructibles)
            {
                if (destructible == null || !destructible.IsAlive)
                {
                    continue;
                }

                Explode(destructible.transform.position, 3.5f, 60f);
                return;
            }
        }

        public string DebugEnvironmentState()
        {
            var aliveCount = 0;
            foreach (var destructible in destructibles)
            {
                if (destructible != null && destructible.IsAlive)
                {
                    aliveCount++;
                }
            }

            return $"destructibles={aliveCount}/{destructibles.Count}; " +
                   $"chunks={infiniteWorld?.ActiveChunks ?? 0}; " +
                   $"chunkCenter={infiniteWorld?.CenterCoordinate ?? Vector2Int.zero}; " +
                   $"boundary={BoundaryWarningActive}/{BoundaryTimeRemaining:0.00}; " +
                   $"player={player?.transform.position ?? Vector3.zero}; " +
                   $"camera={(gameplayCamera == null ? Vector3.zero : gameplayCamera.transform.position)}";
        }

        public void DebugAdvanceBoundary(float seconds)
        {
            if (IsSimulationRunning)
            {
                UpdateCombatBoundary(Mathf.Max(0f, seconds));
            }
        }

        public string DebugBoundaryState()
        {
            var position = player == null
                ? Vector3.zero
                : player.transform.position;
            var inside = CombatBoundaryGeometry.Contains(
                position,
                ArenaHalfWidth,
                ArenaHalfHeight);
            return $"active={BoundaryWarningActive}; " +
                   $"remaining={BoundaryTimeRemaining:0.00}; " +
                   $"inside={inside}; boundaryDeath={boundaryDeath}; " +
                   $"player={position}";
        }

        public void DebugCompleteObjectives()
        {
            var snapshot = objectiveNodes.ToArray();
            foreach (var node in snapshot)
            {
                node?.Complete();
            }
        }

        public void DebugCompleteExtraction()
        {
            if (!objectiveComplete)
            {
                DebugCompleteObjectives();
            }

            extractionCalled = true;
            extractionReady = true;
            extractionCountdown = 0f;
            if (extractionAircraft == null)
            {
                CreateAircraft();
                extractionAircraft.transform.position = extractionPosition + new Vector3(0f, 0.75f, 0f);
            }

            player.transform.position = extractionPosition;
            boardingProgress = 1.5f;
            BeginDeparture();
        }

        public void DebugChooseFirstUpgrade()
        {
            if (upgradeOpen && upgradeChoices != null && upgradeChoices.Length > 0)
            {
                ApplyUpgrade(upgradeChoices[0]);
            }
        }

        public Vector3 DebugRandomSpawnPosition()
        {
            return RandomSpawnPosition();
        }

        public string DebugState()
        {
            return $"phase={phase}; objectives={completedObjectives}/{objectiveNodes.Count}; " +
                   $"kills={kills}; level={level}; upgrade={upgradeOpen}; " +
                   $"boundary={BoundaryWarningActive}/{BoundaryTimeRemaining:0.00}; " +
                   $"contribution={lastContribution:0.0}; sector={sectors[selectedSectorIndex].SovietControl:0.0}";
        }
#endif
    }
}
