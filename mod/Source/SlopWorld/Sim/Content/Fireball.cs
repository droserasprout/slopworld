using RimWorld;
using Verse;

namespace SlopWorld
{
    public class Fireball : Projectile
    {
        const float ExplosionRadius = 2.5f;

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            // Capture the impact context before the base handler destroys the projectile.
            var impactMap = Map;
            var impactPosition = Position;
            var instigator = launcher;
            base.Impact(hitThing, blockedByShield);
            if (impactMap != null)
                GenExplosion.DoExplosion(impactPosition, impactMap, ExplosionRadius, DamageDefOf.Flame, instigator);
        }
    }
}
