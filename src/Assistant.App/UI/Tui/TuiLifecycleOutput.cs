using Assistant.App.Lifecycle;
using Assistant.App.UI.Abstractions;
using Assistant.App.UI.Formatting;

namespace Assistant.App.UI.Tui;

/// <summary>
/// Lifecycle → log presenter + user workspace notifications.
/// </summary>
public sealed class TuiLifecycleOutput(
    LifecycleLogPresenter presenter,
    IUserWorkspace workspace) : ILifecycleEventHandler
{
    public void Handle(ILifecycleEvent lifecycleEvent)
    {
        presenter.Handle(lifecycleEvent);
        workspace.NotifyLifecycle(lifecycleEvent);
    }

    public void CompleteWhenIdle(LifecycleDrainBarrier barrier) => barrier.Complete();
}
