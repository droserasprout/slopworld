using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class SearchSubmissionTests
    {
        public static void RequestsRemainBoundedAcrossQueryReplacement()
        {
            var work = new BoundedWork(2);
            var gate = new OperationGate();
            var pending = new List<Action>();
            long old = gate.Begin();
            for (int i = 0; i < 20; i++) work.Add(() => gate.IsCurrent(old), done => pending.Add(done));
            Assert.That(pending.Count, Is.EqualTo(2));
            long current = gate.Begin();
            for (int i = 0; i < 3; i++) work.Add(() => gate.IsCurrent(current), done => pending.Add(done));
            pending[0](); pending[0]();
            Assert.That(pending.Count, Is.EqualTo(3), "one completion releases one slot, skipping stale queued scopes");
            pending[1]();
            Assert.That(pending.Count, Is.EqualTo(4));
            pending[2]();
            Assert.That(pending.Count, Is.EqualTo(5));
            var query = new SearchSubmission(" submitted ", true, false, true, false);
            Assert.That(query.Query, Is.EqualTo("submitted"));
            Assert.That(query.Regex && query.WholeWord && !query.CaseSensitive && !query.IncludeIgnored, Is.True);
        }
    }
}
