using UnityEngine;

namespace Stalions.Prototype
{
    public sealed class PrototypeObjectiveNode : MonoBehaviour
    {
        private static readonly Color ActiveColor = new Color32(190, 18, 42, 255);
        private static readonly Color ChargingColor = new Color32(225, 227, 231, 255);
        private static readonly Color CompletedColor = new Color32(62, 63, 69, 255);

        private PrototypeGameController controller;
        private Renderer[] renderers;
        private MaterialPropertyBlock colorProperties;
        private float holdSeconds;
        private float progress;
        private bool completed;

        public string DisplayName { get; private set; }
        public bool Completed => completed;
        public float NormalizedProgress => holdSeconds <= 0f ? 1f : progress / holdSeconds;

        public void Initialize(
            PrototypeGameController gameController,
            string displayName,
            float requiredHoldSeconds)
        {
            controller = gameController;
            DisplayName = displayName;
            holdSeconds = requiredHoldSeconds;
            renderers = GetComponentsInChildren<Renderer>();
            colorProperties = new MaterialPropertyBlock();
            RefreshColor(ActiveColor);
        }

        private void Update()
        {
            if (completed || controller == null || controller.Phase != PrototypePhase.Mission ||
                !controller.IsSimulationRunning || controller.Player == null)
            {
                return;
            }

            var playerPosition = controller.Player.transform.position;
            var flatOffset = playerPosition - transform.position;
            flatOffset.y = 0f;

            if (flatOffset.sqrMagnitude <= 2.2f * 2.2f)
            {
                progress += Time.deltaTime;
                RefreshColor(Color.Lerp(
                    ActiveColor,
                    ChargingColor,
                    NormalizedProgress));
            }
            else
            {
                progress = Mathf.Max(0f, progress - Time.deltaTime * 0.25f);
            }

            if (progress < holdSeconds)
            {
                return;
            }

            Complete();
        }

        public void Complete()
        {
            if (completed)
            {
                return;
            }

            completed = true;
            progress = holdSeconds;
            RefreshColor(CompletedColor);
            controller.NotifyObjectiveCompleted(this);
        }

        private void RefreshColor(Color color)
        {
            if (renderers == null)
            {
                return;
            }

            foreach (var itemRenderer in renderers)
            {
                if (itemRenderer == null || itemRenderer.sharedMaterial == null)
                {
                    continue;
                }

                itemRenderer.GetPropertyBlock(colorProperties);
                var propertyName = itemRenderer.sharedMaterial.HasProperty("_BaseColor")
                    ? "_BaseColor"
                    : "_Color";
                colorProperties.SetColor(propertyName, color);
                itemRenderer.SetPropertyBlock(colorProperties);
            }
        }
    }
}
