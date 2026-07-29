using UnityEngine;

namespace Stalions.Prototype
{
    public sealed class PrototypeEnemy : MonoBehaviour
    {
        private PrototypeGameController controller;
        private float health;
        private float moveSpeed;
        private float contactDamage;
        private float attackCooldown;
        private bool alive;
        private int generation;

        public bool IsAlive => alive;
        public float Health => health;
        public int Generation => generation;

        public void Initialize(
            PrototypeGameController gameController,
            float initialHealth,
            float speed,
            float damage)
        {
            controller = gameController;
            health = initialHealth;
            moveSpeed = speed;
            contactDamage = damage;
            attackCooldown = 0f;
            alive = true;
            generation++;
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            if (!alive || controller == null || !controller.IsSimulationRunning)
            {
                return;
            }

            var player = controller.Player;
            if (player == null || !player.IsAlive)
            {
                return;
            }

            var offset = player.transform.position - transform.position;
            offset.y = 0f;
            var distance = offset.magnitude;

            if (distance > 0.85f)
            {
                transform.position += offset.normalized * (moveSpeed * Time.deltaTime);
                transform.forward = Vector3.Slerp(transform.forward, offset.normalized, 10f * Time.deltaTime);
            }
            else
            {
                attackCooldown -= Time.deltaTime;
                if (attackCooldown <= 0f)
                {
                    attackCooldown = 0.75f;
                    player.TakeDamage(contactDamage);
                }
            }
        }

        public void TakeDamage(float amount)
        {
            if (!alive)
            {
                return;
            }

            health -= amount;
            transform.localScale = Vector3.one * 1.12f;
            if (health > 0f)
            {
                return;
            }

            alive = false;
            controller.NotifyEnemyKilled(this, transform.position);
        }

        private void LateUpdate()
        {
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, 14f * Time.deltaTime);
        }
    }
}
