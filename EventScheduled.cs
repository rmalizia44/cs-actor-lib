namespace Actors;

internal class EventScheduled: Event, Cancellable {
    public readonly Actor Actor;
    public EventScheduled? Next = null;
    public EventScheduled(Actor actor, object data, long timestamp): base(data, timestamp) {
        Actor = actor;
    }
    public bool Cancel() {
        return Scheduler.Singleton.TryCancelScheduled(this);
    }
}