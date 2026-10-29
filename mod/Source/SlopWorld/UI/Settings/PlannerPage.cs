using Verse;

namespace SlopWorld
{
    public sealed class PlannerPage : DaemonConfigPage
    {
        readonly UiAreaResize _summary = new UiAreaResize(100f);
        readonly UiAreaResize _plan = new UiAreaResize(130f);
        readonly UiAreaResize _execute = new UiAreaResize(130f);
        protected override string SavedMessage => "Planner settings saved.";

        protected override void DrawFields(Listing_Standard l)
        {
            var p = _cfg.Planner;
            var defaults = _cfg.FactoryDefaults?.Planner;
            UiLayout.Note(
                l,
                "Planner actions send the saved TODO text to OpenRouter. Credentials are shared with Summaries. Execute prepares a work order, then starts the selected worker template; the template controls the worker's model."
            );
            l.Label("OpenRouter model");
            p.Model = UiControls.Field(l, "planner.model", p.Model, defaultValue: defaults?.Model);
            l.Label("Summarize prompt");
            p.SummarizePrompt = UiControls.Area(
                l,
                100f,
                "planner.summary",
                p.SummarizePrompt,
                defaultValue: defaults?.SummarizePrompt,
                resize: _summary
            );
            l.Label("Plan prompt");
            p.PlanPrompt = UiControls.Area(
                l,
                130f,
                "planner.plan",
                p.PlanPrompt,
                defaultValue: defaults?.PlanPrompt,
                resize: _plan
            );
            l.Label("Execute prompt");
            p.ExecutePrompt = UiControls.Area(
                l,
                130f,
                "planner.execute",
                p.ExecutePrompt,
                defaultValue: defaults?.ExecutePrompt,
                resize: _execute
            );
            UiLayout.Note(
                l,
                "POC: manual execution only. Dates and recurrence metadata are preserved, but do not schedule workers."
            );
        }
    }
}
