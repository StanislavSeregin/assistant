using Assistant.App.Lifecycle;
using Assistant.App.UI.Formatting;
using Assistant.App.UI.Tui.Log;

namespace Assistant.App.UI.Tui;

/// <summary>
/// Lifecycle → presenter → coalescing log sink. No console gate; UI stays responsive.
/// </summary>
public sealed class TuiLifecycleOutput(LifecycleLogPresenter presenter) : ILifecycleEventHandler
{
    public void Handle(ILifecycleEvent lifecycleEvent) => presenter.Handle(lifecycleEvent);

    public void CompleteWhenIdle(LifecycleDrainBarrier barrier) => barrier.Complete();
}
