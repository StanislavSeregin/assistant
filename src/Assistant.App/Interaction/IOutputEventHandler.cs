namespace Assistant.App.Interaction;

public interface IOutputEventHandler
{
    void Handle(IOutputEvent outputEvent);
}
