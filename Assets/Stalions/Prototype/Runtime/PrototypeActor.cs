using UnityEngine;
using UnityEngine.InputSystem;

namespace Stalions.Prototype
{
    public sealed class PrototypeActor : MonoBehaviour
    {
        private const float JoystickRadiusScreenFraction = 0.18f;
        private const float MinimumJoystickRadiusPixels = 72f;
        private const float MaximumJoystickRadiusPixels = 240f;

        private PrototypeGameController controller;
        private Vector2 dragOrigin;
        private Vector2 dragCurrent;
        private bool dragging;
        private float smokeUntil;
        private float damageCooldownUntil;
        private float moveSpeed = 5.8f;
        private Vector3 dashStart;
        private Vector3 dashDestination;
        private float dashElapsed;
        private float dashDuration;
        private bool dashing;

        public float MaxHealth { get; private set; } = 100f;
        public float Health { get; private set; } = 100f;
        public bool IsAlive => Health > 0f;
        public bool IsDragging => dragging;
        public bool IsDashing => dashing;
        public Vector3 DashDestination => dashDestination;
        public Vector2 DragOrigin => dragOrigin;
        public Vector2 DragCurrent => dragCurrent;
        public Vector3 FacingDirection
        {
            get
            {
                var direction = transform.forward;
                direction.y = 0f;
                return direction.sqrMagnitude > 0.001f
                    ? direction.normalized
                    : Vector3.forward;
            }
        }

        public void Initialize(PrototypeGameController gameController)
        {
            controller = gameController;
            MaxHealth = 100f;
            Health = MaxHealth;
        }

        private void Update()
        {
            if (dashing)
            {
                UpdateDash();
                return;
            }

            if (controller == null || !controller.IsSimulationRunning || !IsAlive)
            {
                ResetDragging();
                return;
            }

            var input = ReadMovementInput();
            var movement = new Vector3(input.x, 0f, input.y);
            transform.position = controller.ClampToArena(
                transform.position + movement * (moveSpeed * Time.deltaTime));

            if (movement.sqrMagnitude > 0.01f)
            {
                FaceDirection(movement, 15f * Time.deltaTime);
            }
        }

        public bool BeginDash(Vector3 destination, float duration)
        {
            if (!IsAlive || dashing)
            {
                return false;
            }

            destination.y = 0f;
            dashStart = transform.position;
            dashStart.y = 0f;
            dashDestination = destination;
            dashDuration = Mathf.Max(0.01f, duration);
            dashElapsed = 0f;
            dashing = true;
            damageCooldownUntil = Mathf.Max(
                damageCooldownUntil,
                Time.time + dashDuration + 0.04f);

            var direction = dashDestination - dashStart;
            if (direction.sqrMagnitude > 0.001f)
            {
                FaceDirection(direction);
            }

            return true;
        }

        private void UpdateDash()
        {
            dashElapsed += Time.deltaTime;
            var normalized = Mathf.Clamp01(dashElapsed / dashDuration);
            var eased = 1f - Mathf.Pow(1f - normalized, 3f);
            transform.position = Vector3.Lerp(dashStart, dashDestination, eased);

            if (normalized >= 1f)
            {
                transform.position = dashDestination;
                dashing = false;
            }
        }

        public void FaceDirection(Vector3 direction, float blend = 1f)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f)
            {
                return;
            }

            transform.forward = Vector3.Slerp(
                transform.forward,
                direction.normalized,
                Mathf.Clamp01(blend));
        }

        private Vector2 ReadMovementInput()
        {
            var movement = Vector2.zero;
            var pointer = Pointer.current;

            if (pointer == null)
            {
                ResetDragging();
            }
            else
            {
                var position = pointer.position.ReadValue();
                if (pointer.press.wasPressedThisFrame)
                {
                    dragging = true;
                    dragOrigin = position;
                    dragCurrent = position;
                }

                if (dragging && pointer.press.isPressed)
                {
                    dragCurrent = position;
                    movement = Vector2.ClampMagnitude(
                        (dragCurrent - dragOrigin) / GetJoystickRadiusPixels(),
                        1f);
                }
                else if (dragging)
                {
                    ResetDragging();
                }
            }

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                var keyboardMovement = new Vector2(
                    (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                    (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));

                if (keyboardMovement.sqrMagnitude > 0f)
                {
                    movement = keyboardMovement.normalized;
                }
            }

            return movement;
        }

        private static float GetJoystickRadiusPixels()
        {
            var safeArea = Screen.safeArea;
            var safeWidth = safeArea.width > 0f ? safeArea.width : Screen.width;
            var safeHeight = safeArea.height > 0f ? safeArea.height : Screen.height;
            var shortestSafeSide = Mathf.Min(safeWidth, safeHeight);

            return Mathf.Clamp(
                shortestSafeSide * JoystickRadiusScreenFraction,
                MinimumJoystickRadiusPixels,
                MaximumJoystickRadiusPixels);
        }

        private void ResetDragging()
        {
            dragging = false;
            dragOrigin = Vector2.zero;
            dragCurrent = Vector2.zero;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                ResetDragging();
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                ResetDragging();
            }
        }

        private void OnDisable()
        {
            ResetDragging();
            dashing = false;
        }

        public void TakeDamage(float amount)
        {
            if (!IsAlive || Time.time < smokeUntil || Time.time < damageCooldownUntil)
            {
                return;
            }

            damageCooldownUntil = Time.time + 0.16f;
            Health = Mathf.Max(0f, Health - amount);
            transform.localScale = Vector3.one * 1.08f;

            if (!IsAlive)
            {
                controller.NotifyPlayerDied();
            }
        }

        public void KillImmediately()
        {
            if (!IsAlive)
            {
                return;
            }

            Health = 0f;
            dashing = false;
            controller?.NotifyPlayerDied();
        }

        public void Heal(float amount)
        {
            if (!IsAlive)
            {
                return;
            }

            Health = Mathf.Min(MaxHealth, Health + amount);
        }

        public void IncreaseMaxHealth(float amount)
        {
            MaxHealth += amount;
            Health = Mathf.Min(MaxHealth, Health + amount);
        }

        public void GrantSmoke(float seconds)
        {
            smokeUntil = Mathf.Max(smokeUntil, Time.time + seconds);
        }

        public void Pulse()
        {
            transform.localScale = Vector3.one * 1.12f;
        }

        private void LateUpdate()
        {
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, 12f * Time.deltaTime);
        }
    }
}
