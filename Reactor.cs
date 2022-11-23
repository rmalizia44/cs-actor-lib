namespace Reactors;

public interface Reactor {
    Task Start(Actor self);
    Task Terminate();
    Task React(object data, long timestamp);
}
