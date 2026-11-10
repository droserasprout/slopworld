using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class AuraChargeTests
    {
        public static void RepeatedPatsAccumulateMinutesEvenWhilePaused()
        {
            var charge = new AuraCharge();
            Assert.That(charge.Active(100), Is.False);
            for (int i = 0; i < 12; i++)
                charge.Pat(100);
            Assert.That(charge.Active(7299), Is.True);
            Assert.That(charge.Active(7300), Is.False);
        }

        public static void PatPreservesRemainingChargeAndRestartsAfterExpiry()
        {
            var charge = new AuraCharge();
            charge.Pat(100);
            charge.Pat(400);
            Assert.That(charge.Active(1299), Is.True);
            Assert.That(charge.Active(1300), Is.False);
            charge.Pat(2000);
            Assert.That(charge.Active(2599), Is.True);
            Assert.That(charge.Active(2600), Is.False);
        }

        public static void ChargeDoesNotOverflowNearGameTickLimit()
        {
            var charge = new AuraCharge();
            charge.Pat(int.MaxValue - 100);
            Assert.That(charge.Active(int.MaxValue), Is.True);
        }
    }
}
