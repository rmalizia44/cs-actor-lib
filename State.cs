namespace Actors;

public interface State: IDisposable {
    Task React(object data, long timestamp);
}
