using DinoHunt.Core;
using NUnit.Framework;

namespace DinoHunt.Tests
{
    public class DeterministicRandomTests
    {
        [Test]
        public void SameSeed_ProducesIdenticalSequence()
        {
            var a = new DeterministicRandom(88213);
            var b = new DeterministicRandom(88213);

            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextULong(), b.NextULong(), $"diverged at draw {i}");
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new DeterministicRandom(1);
            var b = new DeterministicRandom(2);

            bool anyDifferent = false;
            for (int i = 0; i < 16; i++)
                if (a.NextULong() != b.NextULong()) { anyDifferent = true; break; }

            Assert.IsTrue(anyDifferent, "sequential seeds 1 and 2 produced identical output");
        }

        [Test]
        public void NextFloat_StaysInUnitInterval()
        {
            var rng = new DeterministicRandom(42);
            for (int i = 0; i < 100000; i++)
            {
                float f = rng.NextFloat();
                Assert.GreaterOrEqual(f, 0f);
                Assert.Less(f, 1f);
            }
        }

        [Test]
        public void NextInt_RespectsBounds()
        {
            var rng = new DeterministicRandom(7);
            for (int i = 0; i < 100000; i++)
            {
                int v = rng.NextInt(-5, 5);
                Assert.GreaterOrEqual(v, -5);
                Assert.Less(v, 5);
            }
        }

        [Test]
        public void NextInt_EmptyRange_ReturnsMin()
        {
            var rng = new DeterministicRandom(7);
            Assert.AreEqual(3, rng.NextInt(3, 3));
            Assert.AreEqual(3, rng.NextInt(3, 1));
        }

        [Test]
        public void State_RoundTrips_ResumesSameStream()
        {
            var rng = new DeterministicRandom(99);
            for (int i = 0; i < 10; i++) rng.NextULong();

            ulong saved = rng.State;
            ulong expectedNext = rng.NextULong();

            var restored = new DeterministicRandom(0) { State = saved };
            Assert.AreEqual(expectedNext, restored.NextULong());
        }

        [Test]
        public void Fork_ProducesIndependentStreamDeterministically()
        {
            var a = new DeterministicRandom(500);
            var b = new DeterministicRandom(500);

            var forkA = a.Fork();
            var forkB = b.Fork();

            for (int i = 0; i < 100; i++)
                Assert.AreEqual(forkA.NextULong(), forkB.NextULong());
        }
    }
}
