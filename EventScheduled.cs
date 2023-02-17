namespace Actors;

internal class EventScheduled: Event, Cancellable {
    internal readonly Actor Actor;
    internal EventScheduled? Next = null;
    internal EventScheduled(Actor actor, object data, long timestamp): base(data, timestamp) {
        Actor = actor;
    }
    public bool Cancel() {
        return Scheduler.Singleton.TryCancelScheduled(this);
    }
}