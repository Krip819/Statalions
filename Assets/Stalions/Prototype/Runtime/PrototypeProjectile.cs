using UnityEngine;

namespace Stalions.Prototype
{
    public sealed class PrototypeProjectile : MonoBehaviour
    {
        private PrototypeGameController controller;
        private Vector3 destination;
        private float damage;
        private float speed;
        private float lifetime;

        public void Initialize(
            PrototypeGameController gameController,
            Vector3 shotDestination,
            float projectileDamage,
            float projectileSpeed)
        {
            controller = gameController;
            destination = shotDestination;
            damage = projectileDamage;
            speed = projectileSpeed;
            lifetime = 2.5f;
        }

        private void Update()
        {
            if (controller == null || !controller.IsSimulationRunning)
            {
                return;
            }

            lifetime -= Time.deltaTime;
            if (lifetime <= 0f)
            {
                Release();
                return;
            }

            var previousPosition = transform.position;
            var nextPosition = Vector3.MoveTowards(
                previousPosition,
                destination,
                speed * Time.deltaTime);

            if (controller.TryDamageTargetAlongSegment(
                    previousPosition,
                    nextPosition,
                    damage))
            {
                Release();
                return;
            }

            transform.position = nextPosition;

            if ((transform.position - destination).sqrMagnitude > 0.12f)
            {
                return;
            }

            Release();
        }

        private void Release()
        {
            var owner = controller;
            controller = null;
            owner?.ReleaseProjectile(this);
        }
    }
}
