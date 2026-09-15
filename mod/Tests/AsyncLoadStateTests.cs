using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class AsyncLoadStateTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("operation gate invalidates older tokens", GateTokens);
            yield return ("late callbacks cannot mutate a newer load", LateCallbacks);
            yield return ("invalidation stops loading without a replacement", Invalidate);
            yield return ("current operations accept multiple callbacks", MultipleCallbacks);
            yield return ("failed loads retain the old value but clear availability", OldValuePolicy);
            yield return ("preview fields apply together after the generation check", PreviewFields);
        }

        static void GateTokens()
        {
            var gate = new OperationGate();
            int first = gate.Begin();
            int second = gate.Begin();

            AssertEx.False(gate.IsCurrent(first), "starting a new operation invalidates the old token");
            AssertEx.True(gate.IsCurrent(second), "the newest token is current");
            gate.Invalidate();
            AssertEx.False(gate.IsCurrent(second), "invalidation has no replacement operation");
            AssertEx.False(gate.IsCurrent(0), "the default token is never current");
        }

        static void LateCallbacks()
        {
            var state = new AsyncLoadState<string>();
            Action<string> firstOk = null;
            Action<string> firstFail = null;
            Action<string> secondOk = null;
            Action<string> secondFail = null;
            int loaded = 0;

            state.Load((ok, fail) => { firstOk = ok; firstFail = fail; }, _ => loaded++);
            state.Load((ok, fail) => { secondOk = ok; secondFail = fail; }, _ => loaded++);
            firstOk("old");
            firstFail("old error");

            AssertEx.True(state.Loading, "late callbacks leave the newer load pending");
            AssertEx.False(state.HasValue, "late callbacks do not publish an old value");
            AssertEx.Equal(null, state.Error, "late callbacks do not publish an old error");
            AssertEx.Equal(0, loaded, "a stale callback does not run the loaded hook");

            secondOk("new");
            AssertEx.Equal("new", state.Value, "the current callback publishes its value");
            AssertEx.True(state.HasValue, "the current value is available");
            AssertEx.False(state.Loading, "the current success ends loading");
            AssertEx.Equal(1, loaded, "the current loaded hook runs once");
            secondFail("late same-operation error");
            AssertEx.Equal("late same-operation error", state.Error,
                "a current error callback retains the existing callback contract");
        }

        static void Invalidate()
        {
            var state = new AsyncLoadState<string>();
            Action<string> ok = null;
            Action<string> fail = null;
            state.Load((success, error) => { ok = success; fail = error; });
            state.Invalidate();
            ok("stale");
            fail("stale error");

            AssertEx.False(state.Loading, "invalidation ends a load with no replacement");
            AssertEx.False(state.HasValue, "invalidation does not invent a value");
            AssertEx.Equal(null, state.Value, "stale success cannot set a value");
            AssertEx.Equal(null, state.Error, "stale error cannot set an error");
        }

        static void MultipleCallbacks()
        {
            var state = new AsyncLoadState<int>();
            Action<int> ok = null;
            int loaded = 0;
            state.Load((success, _) => ok = success, _ => loaded++);
            ok(1);
            ok(2);

            AssertEx.Equal(2, state.Value, "callbacks from one current operation are accepted");
            AssertEx.Equal(2, loaded, "each current callback reaches the loaded hook");
        }

        static void OldValuePolicy()
        {
            var state = new AsyncLoadState<string>();
            Action<string> ok = null;
            Action<string> fail = null;
            state.Load((success, error) => { ok = success; fail = error; });
            ok("retained");
            state.Load((success, error) => fail = error);
            fail("failed");

            AssertEx.Equal("retained", state.Value, "the state retains the old value on failure");
            AssertEx.False(state.HasValue, "failure clears availability for callers that require it");
            AssertEx.False(state.Loading, "failure ends loading");
            AssertEx.Equal("failed", state.Error, "failure publishes the current error");
        }

        static void PreviewFields()
        {
            var state = new AsyncLoadState<JVal>();
            Action<JVal> oldReply = null, newReply = null;
            string text = null, breadcrumb = null;
            Action<JVal> apply = j =>
            {
                text = j["text"].AsString();
                breadcrumb = j["breadcrumb"].AsString();
            };
            state.Load((ok, _) => oldReply = ok, apply);
            state.Load((ok, _) => newReply = ok, apply);
            newReply(JVal.Parse("{\"text\":\"new\",\"breadcrumb\":\"new crumb\"}"));
            oldReply(JVal.Parse("{\"text\":\"old\",\"breadcrumb\":\"old crumb\"}"));
            AssertEx.Equal("new", text, "old response cannot replace the document");
            AssertEx.Equal("new crumb", breadcrumb, "old response cannot replace the breadcrumb");
            state.Invalidate();
            newReply(JVal.Parse("{\"text\":\"closed\",\"breadcrumb\":\"closed crumb\"}"));
            AssertEx.Equal("new crumb", breadcrumb, "closed preview rejects both fields");
        }
    }
}
