namespace Assistant.App.Lifecycle;

public interface ILifecycleEventHandler
{
    void Handle(ILifecycleEvent lifecycleEvent);

    void CompleteWhenIdle(LifecycleDrainBarrier barrier);
}
