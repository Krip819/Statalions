using UnityEngine;

namespace Stalions.Prototype
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class PrototypeFollowCamera : MonoBehaviour
    {
        [Header("Framing")]
        [SerializeField, Min(1f)] private float orthographicSize = 9.6f;
        [SerializeField, Range(45f, 85f)] private float tiltDegrees = 52f;
        [SerializeField, Min(1f)] private float cameraDistance = 20f;

        [Header("Follow")]
        [SerializeField, Min(0.01f)] private float followSmoothTime = 0.18f;
        [SerializeField, Min(0f)] private float lookAheadTime = 0.32f;
        [SerializeField, Min(0f)] private float maximumLookAhead = 2.4f;
        [SerializeField, Min(0f)] private float velocitySmoothing = 10f;

        private Camera controlledCamera;
        private Transform followTarget;
        private Vector3 smoothedFocus;
        private Vector3 focusVelocity;
        private Vector3 smoothedTargetVelocity;
        private Vector3 lastTargetPosition;
        private float worldHalfWidth;
        private float worldHalfHeight;
        private bool hasTargetSample;

        public void SetFraming(float size, float tilt, float distance)
        {
            orthographicSize = Mathf.Max(1f, size);
            tiltDegrees = Mathf.Clamp(tilt, 45f, 85f);
            cameraDistance = Mathf.Max(1f, distance);
            CacheCamera();
            ConfigureProjection();

            if (followTarget != null)
            {
                ApplyCameraPose(smoothedFocus);
            }
        }

        public void Configure(Transform target, float worldHalfWidth, float worldHalfHeight)
        {
            CacheCamera();

            this.worldHalfWidth = Mathf.Max(0f, worldHalfWidth);
            this.worldHalfHeight = Mathf.Max(0f, worldHalfHeight);
            followTarget = target;
            focusVelocity = Vector3.zero;
            smoothedTargetVelocity = Vector3.zero;
            hasTargetSample = target != null;

            ConfigureProjection();

            if (target == null)
            {
                return;
            }

            lastTargetPosition = target.position;
            smoothedFocus = ClampFocusToWorld(target.position);
            ApplyCameraPose(smoothedFocus);
        }

        public void ConfigureUnbounded(Transform target)
        {
            Configure(target, 0f, 0f);
        }

        public void ClearTarget()
        {
            followTarget = null;
            focusVelocity = Vector3.zero;
            smoothedTargetVelocity = Vector3.zero;
            hasTargetSample = false;
        }

        private void Awake()
        {
            CacheCamera();
        }

        private void OnValidate()
        {
            orthographicSize = Mathf.Max(1f, orthographicSize);
            cameraDistance = Mathf.Max(1f, cameraDistance);
            followSmoothTime = Mathf.Max(0.01f, followSmoothTime);
            lookAheadTime = Mathf.Max(0f, lookAheadTime);
            maximumLookAhead = Mathf.Max(0f, maximumLookAhead);
            velocitySmoothing = Mathf.Max(0f, velocitySmoothing);

            if (controlledCamera == null)
            {
                controlledCamera = GetComponent<Camera>();
            }

            if (controlledCamera != null)
            {
                ConfigureProjection();
            }
        }

        private void LateUpdate()
        {
            if (followTarget == null)
            {
                return;
            }

            CacheCamera();
            ConfigureProjection();

            var deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            var targetPosition = followTarget.position;

            if (!hasTargetSample)
            {
                lastTargetPosition = targetPosition;
                smoothedFocus = ClampFocusToWorld(targetPosition);
                focusVelocity = Vector3.zero;
                smoothedTargetVelocity = Vector3.zero;
                hasTargetSample = true;
            }

            var measuredVelocity = (targetPosition - lastTargetPosition) / deltaTime;
            measuredVelocity.y = 0f;
            lastTargetPosition = targetPosition;

            var velocityBlend = 1f - Mathf.Exp(-velocitySmoothing * deltaTime);
            smoothedTargetVelocity = Vector3.Lerp(
                smoothedTargetVelocity,
                measuredVelocity,
                velocityBlend);

            var lookAhead = Vector3.ClampMagnitude(
                smoothedTargetVelocity * lookAheadTime,
                maximumLookAhead);
            var desiredFocus = ClampFocusToWorld(targetPosition + lookAhead);

            smoothedFocus = Vector3.SmoothDamp(
                smoothedFocus,
                desiredFocus,
                ref focusVelocity,
                followSmoothTime,
                Mathf.Infinity,
                deltaTime);
            smoothedFocus = ClampFocusToWorld(smoothedFocus);

            ApplyCameraPose(smoothedFocus);
        }

        private void CacheCamera()
        {
            if (controlledCamera == null)
            {
                controlledCamera = GetComponent<Camera>();
            }
        }

        private void ConfigureProjection()
        {
            if (controlledCamera == null)
            {
                return;
            }

            controlledCamera.orthographic = true;
            controlledCamera.orthographicSize = orthographicSize;
            controlledCamera.nearClipPlane = 0.1f;
            controlledCamera.farClipPlane = Mathf.Max(
                controlledCamera.farClipPlane,
                cameraDistance * 3f);
        }

        private Vector3 ClampFocusToWorld(Vector3 focus)
        {
            if (controlledCamera == null)
            {
                return focus;
            }

            var aspect = controlledCamera.aspect;
            if (aspect <= 0.01f)
            {
                aspect = Screen.height > 0
                    ? Screen.width / (float)Screen.height
                    : 1f;
            }

            var visibleHalfWidth = orthographicSize * aspect;
            var downwardComponent = Mathf.Max(
                0.25f,
                Mathf.Sin(tiltDegrees * Mathf.Deg2Rad));
            var visibleHalfHeight = orthographicSize / downwardComponent;

            focus.x = ClampFocusAxis(focus.x, worldHalfWidth, visibleHalfWidth);
            focus.z = ClampFocusAxis(focus.z, worldHalfHeight, visibleHalfHeight);
            return focus;
        }

        private static float ClampFocusAxis(float value, float worldExtent, float viewExtent)
        {
            if (worldExtent <= 0f)
            {
                return value;
            }

            var remainingExtent = worldExtent - viewExtent;
            return remainingExtent > 0f
                ? Mathf.Clamp(value, -remainingExtent, remainingExtent)
                : 0f;
        }

        private void ApplyCameraPose(Vector3 focus)
        {
            var rotation = Quaternion.Euler(tiltDegrees, 0f, 0f);
            var forward = rotation * Vector3.forward;
            transform.SetPositionAndRotation(
                focus - forward * cameraDistance,
                rotation);
        }
    }
}
