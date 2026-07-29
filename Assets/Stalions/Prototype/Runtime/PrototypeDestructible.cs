using System;
using UnityEngine;

namespace Stalions.Prototype
{
    [DisallowMultipleComponent]
    public sealed class PrototypeDestructible : MonoBehaviour
    {
        private const float HitReactionRecovery = 7f;
        private const float DestructionDuration = 0.32f;

        private PrototypeGameController controller;
        private Vector3 intactScale;
        private Quaternion intactRotation;
        private float health;
        private float maxHealth;
        private float hitReaction;
        private float destructionAge;
        private float leanDirection;
        private bool alive;

        public event Action<PrototypeDestructible> Destroyed;

        public bool IsAlive => alive;
        public float Health => health;
        public float MaxHealth => maxHealth;
        public float HealthNormalized => maxHealth <= 0f ? 0f : health / maxHealth;
        public float HitRadius { get; private set; } = 0.65f;

        private void Awake()
        {
            intactScale = transform.localScale;
            intactRotation = transform.localRotation;
            leanDirection = transform.position.x >= 0f ? 1f : -1f;
        }

        public void Initialize(
            PrototypeGameController gameController,
            float initialHealth,
            float hitRadius = 0.65f)
        {
            controller = gameController;
            intactScale = transform.localScale;
            intactRotation = transform.localRotation;
            maxHealth = Mathf.Max(0.01f, initialHealth);
            health = maxHealth;
            HitRadius = Mathf.Max(0.1f, hitRadius);
            hitReaction = 0f;
            destructionAge = 0f;
            alive = true;

            transform.localScale = intactScale;
            transform.localRotation = intactRotation;
            gameObject.SetActive(true);
        }

        public void Initialize(float initialHealth)
        {
            Initialize(null, initialHealth);
        }

        public void TakeDamage(float amount)
        {
            if (!alive || amount <= 0f)
            {
                return;
            }

            health = Mathf.Max(0f, health - amount);
            hitReaction = 1f;

            if (health > 0f)
            {
                return;
            }

            alive = false;
            destructionAge = 0f;
            Destroyed?.Invoke(this);
        }

        private void Update()
        {
            if (controller != null && !controller.IsSimulationRunning)
            {
                return;
            }

            if (alive)
            {
                UpdateHitReaction();
                return;
            }

            UpdateDestruction();
        }

        private void UpdateHitReaction()
        {
            hitReaction = Mathf.MoveTowards(
                hitReaction,
                0f,
                HitReactionRecovery * Time.deltaTime);

            var punchScale = new Vector3(
                1f + hitReaction * 0.12f,
                1f - hitReaction * 0.10f,
                1f + hitReaction * 0.12f);

            transform.localScale = Vector3.Scale(intactScale, punchScale);
            transform.localRotation = intactRotation
                * Quaternion.Euler(0f, 0f, leanDirection * hitReaction * 4f);
        }

        private void UpdateDestruction()
        {
            destructionAge += Time.deltaTime;
            var t = Mathf.Clamp01(destructionAge / DestructionDuration);
            var collapse = 1f - t;
            var outwardPunch = 1f + Mathf.Sin(t * Mathf.PI) * 0.18f;

            transform.localScale = Vector3.Scale(
                intactScale,
                new Vector3(
                    outwardPunch * collapse,
                    collapse * collapse,
                    outwardPunch * collapse));
            transform.localRotation = intactRotation
                * Quaternion.Euler(0f, 0f, leanDirection * t * 24f);

            if (t >= 1f)
            {
                gameObject.SetActive(false);
            }
        }
    }
}
