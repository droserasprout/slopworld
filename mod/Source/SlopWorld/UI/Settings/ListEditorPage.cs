namespace SlopWorld
{
    // Settings pages that edit daemon config share the same scrollable form and footer.
    // Subclasses only supply the fields that make up their row body.
    public abstract class ListEditorPage : DaemonConfigPage
    {
        protected abstract override string SavedMessage { get; }
        protected override bool ShowEditButton => true;
    }
}
