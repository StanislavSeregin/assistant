namespace AssistantV2.App.Lifecycle;

public interface ILifecycleEventHandler
{
    void Handle(ILifecycleEvent lifecycleEvent);

    void CompleteWhenIdle(LifecycleDrainBarrier barrier);
}
