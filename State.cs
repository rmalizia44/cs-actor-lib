namespace Actors;

public interface State: IDisposable {
    Task React(Event e);
}
