using System.Threading.Channels;

namespace Actors;

internal class EventScheduled: Event {
    public readonly Actor Actor;
    public EventScheduled? Next = null;
    public EventScheduled(Actor actor, object data, long timestamp): base(data, timestamp) {
        Actor = actor;
    }
}

public class Actor {
    private static readonly DateTime StartTime = DateTime.Now;
    private static readonly Timer Timer = new(o => NotifyScheduled());
    private static readonly List<EventScheduled> UnsafeMsgScheduled = new();
    private static long CalcTimestamp() {
        return (long)((DateTime.Now - StartTime).TotalMilliseconds + 0.5);
    }
    private static void UpdateTimer(long timeout) {
        if(timeout < 0) {
            timeout = 0;
        }
        Timer.Change(timeout, Timeout.Infinite);
    }
    private static bool PushScheduled(EventScheduled msg) {
        lock(UnsafeMsgScheduled) {
            var idx = UnsafeMsgScheduled.BinarySearch(msg);
            if(idx < 0) {
                idx = ~idx;
            }
            UnsafeMsgScheduled.Insert(idx, msg);
            if(idx == 0) {
                UpdateTimer(msg.Timestamp - CalcTimestamp());
            }
        }
        return true;
    }
    private static void NotifyScheduled() {
        EventScheduled? ready = null;
        lock(UnsafeMsgScheduled) {
            long currentTimestamp = CalcTimestamp();
            while(UnsafeMsgScheduled.Any() && UnsafeMsgScheduled[0].Timestamp < currentTimestamp) {
                var msg = UnsafeMsgScheduled[0];
                UnsafeMsgScheduled.RemoveAt(0);
                msg.Next = ready;
                ready = msg;
            }
            if(UnsafeMsgScheduled.Any()) {
                var timeout = UnsafeMsgScheduled[0].Timestamp - currentTimestamp;
                UpdateTimer(timeout);
            }
        }
        while(ready != null) {
            ready.Actor.PushNow(ready);
            ready = ready.Next;
        }
    }
    private static void RemoveScheduled(Actor actor) {
        lock(UnsafeMsgScheduled) {
            UnsafeMsgScheduled.RemoveAll(msg => msg.Actor == actor);
        }
    }
    private readonly Channel<Event> Queue;
    private State State = null!;
    private Task Task = null!;
    public Actor() {
        Queue = Channel.CreateUnbounded<Event>();
    }
    public Task Reset(State state) {
        var old = State;
        State = state;
        if(old == null) {
            var scheduler = new ConcurrentExclusiveSchedulerPair()
                .ExclusiveScheduler;
            Task = Task.Factory.StartNew(
                () => Loop(),
                CancellationToken.None,
                TaskCreationOptions.None,
                scheduler
            ).Unwrap();
        } else {
            old.Dispose();
        }
        return Task;
    }
    public bool Send(object data, int delay = 0) {
        long timestamp = CalcTimestamp();
        if(delay > 0) {
            return PushScheduled(new EventScheduled(this, data, timestamp + delay));
        } else {
            return PushNow(new Event(data, timestamp));
        }
    }
    public void Kill() {
        RemoveScheduled(this);
        Queue.Writer.TryComplete();
    }
    private bool PushNow(Event msg) {
        return Queue.Writer.TryWrite(msg);
    }
    private async Task Loop() {
        bool running = true;
        while(running) {
            try {
                await foreach(var e in Queue.Reader.ReadAllAsync()) {
                    await State.React(e);
                }
                running = false;
            } catch (Exception e) {
                Console.WriteLine(e);
            }
        }
        try {
            State.Dispose();
        } catch (Exception e) {
            Console.WriteLine(e);
        }
    }
}