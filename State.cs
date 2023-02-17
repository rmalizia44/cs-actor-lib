namespace Actors;

public interface State: IAsyncDisposable {
    ValueTask ReactAsync(Event e);
}
