namespace Reactors;

public interface Reactor: IDisposable {
    Task React(object data, long timestamp);
}
