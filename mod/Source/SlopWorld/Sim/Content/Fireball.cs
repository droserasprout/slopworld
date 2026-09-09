using RimWorld;
using Verse;

namespace SlopWorld
{
    public class Fireball : Projectile
    {
        const float ExplosionRadius = 2.5f;

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            var map = Map;
            var pos = Position;
            var source = launcher;
            base.Impact(hitThing, blockedByShield);
            if (map != null)
                GenExplosion.DoExplosion(pos, map, ExplosionRadius, DamageDefOf.Flame, source);
        }
    }
}
