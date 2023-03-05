namespace Actors;

public interface State: IAsyncDisposable {
    ValueTask StartAsync();
    ValueTask ReactAsync(Event e);
}
