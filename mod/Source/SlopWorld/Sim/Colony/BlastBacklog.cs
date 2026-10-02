using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Retain each annulus until its scheduled blasts drain; callers own tick budgets and sampling.
    internal sealed class BlastBacklog
    {
        sealed class Ring
        {
            public float Inner, Outer;
            public int Remaining;
        }

        readonly Queue<Ring> _rings = new Queue<Ring>();
        double _fraction;

        public bool Pending => _rings.Count > 0;

        public void Clear()
        {
            _rings.Clear();
            _fraction = 0;
        }

        public void AddRing(float inner, float outer, float cellsPerBlast)
        {
            if (outer <= inner) return;
            double owed = _fraction + Math.PI * ((double)outer * outer - (double)inner * inner) / cellsPerBlast;
            int count = (int)Math.Floor(owed);
            _fraction = owed - count;
            if (count > 0) _rings.Enqueue(new Ring { Inner = inner, Outer = outer, Remaining = count });
        }

        public bool TryTake(out float inner, out float outer)
        {
            inner = outer = 0;
            if (_rings.Count == 0) return false;
            var ring = _rings.Peek();
            inner = ring.Inner;
            outer = ring.Outer;
            if (--ring.Remaining == 0) _rings.Dequeue();
            return true;
        }
    }
}
