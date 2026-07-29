using UnityEngine;

namespace Stalions.Prototype
{
    public sealed class PrototypePulseFx : MonoBehaviour
    {
        private PrototypeGameController controller;
        private float age;
        private float duration;
        private Vector3 targetScale;

        public void Initialize(PrototypeGameController gameController, float size, float seconds)
        {
            controller = gameController;
            age = 0f;
            duration = seconds;
            targetScale = new Vector3(size, 0.06f, size);
            transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            age += Time.deltaTime;
            var t = duration <= 0f ? 1f : Mathf.Clamp01(age / duration);
            transform.localScale = Vector3.Lerp(Vector3.zero, targetScale, t);

            if (age >= duration)
            {
                var owner = controller;
                controller = null;
                owner?.ReleasePulse(this);
            }
        }
    }
}
