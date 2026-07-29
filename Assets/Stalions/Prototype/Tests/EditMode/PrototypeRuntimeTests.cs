using NUnit.Framework;
using UnityEngine;

namespace Stalions.Prototype.Tests
{
    public sealed class PrototypeRuntimeTests
    {
        [Test]
        public void Destructible_UsesConfiguredHitRadiusAndDiesOnce()
        {
            var root = new GameObject("Destructible Test");
            try
            {
                var destructible = root.AddComponent<PrototypeDestructible>();
                var destroyedCount = 0;
                destructible.Destroyed += _ => destroyedCount++;
                destructible.Initialize(null, 100f, 1.25f);

                destructible.TakeDamage(25f);

                Assert.That(destructible.IsAlive, Is.True);
                Assert.That(destructible.Health, Is.EqualTo(75f));
                Assert.That(destructible.HitRadius, Is.EqualTo(1.25f));

                destructible.TakeDamage(75f);
                destructible.TakeDamage(10f);

                Assert.That(destructible.IsAlive, Is.False);
                Assert.That(destroyedCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Actor_FaceDirectionUpdatesFallbackAim()
        {
            var root = new GameObject("Actor Test");
            try
            {
                var actor = root.AddComponent<PrototypeActor>();

                actor.FaceDirection(Vector3.right);

                Assert.That(Vector3.Dot(actor.FacingDirection, Vector3.right), Is.GreaterThan(0.999f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Enemy_ChangesGenerationWhenPoolObjectIsReinitialized()
        {
            var root = new GameObject("Enemy Test");
            try
            {
                var enemy = root.AddComponent<PrototypeEnemy>();
                enemy.Initialize(null, 10f, 1f, 1f);
                var firstGeneration = enemy.Generation;

                enemy.Initialize(null, 10f, 1f, 1f);

                Assert.That(enemy.Generation, Is.Not.EqualTo(firstGeneration));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Actor_BeginDashStoresUnboundedDestination()
        {
            var root = new GameObject("Dash Actor Test");
            try
            {
                var actor = root.AddComponent<PrototypeActor>();
                var destination = new Vector3(220f, 0f, 300f);

                var started = actor.BeginDash(destination, 0.12f);

                Assert.That(started, Is.True);
                Assert.That(actor.IsDashing, Is.True);
                Assert.That(actor.DashDestination, Is.EqualTo(destination));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Actor_KillImmediatelyIgnoresProtectionAndStopsDash()
        {
            var root = new GameObject("Boundary Death Actor Test");
            try
            {
                var actor = root.AddComponent<PrototypeActor>();
                actor.GrantSmoke(10f);
                actor.BeginDash(new Vector3(4f, 0f, 0f), 1f);

                actor.KillImmediately();

                Assert.That(actor.IsAlive, Is.False);
                Assert.That(actor.Health, Is.Zero);
                Assert.That(actor.IsDashing, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void InfiniteWorld_UsesDeterministicChunkCoordinatesAndDecor()
        {
            var worldPosition = new Vector3(200f, 0f, 300f);
            var coordinate = PrototypeInfiniteWorld.WorldToChunk(worldPosition);
            var first = PrototypeInfiniteWorld.DecorLocalPosition(coordinate, 3);
            var second = PrototypeInfiniteWorld.DecorLocalPosition(coordinate, 3);

            Assert.That(coordinate, Is.EqualTo(new Vector2Int(6, 9)));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(Mathf.Abs(first.x), Is.LessThanOrEqualTo(13.5f));
            Assert.That(Mathf.Abs(first.z), Is.LessThanOrEqualTo(13.5f));
        }
    }
}
