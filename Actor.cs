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
    private static readonly object Mtx = new();
    private static EventScheduled? UnsafeScheduled = null;
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
        lock(Mtx) {
            if(UnsafeScheduled == null || msg.Timestamp < UnsafeScheduled.Timestamp) {
                msg.Next = UnsafeScheduled;
                UnsafeScheduled = msg;
                UpdateTimer(msg.Timestamp - CalcTimestamp());
            } else {
                EventScheduled? head = UnsafeScheduled;
                while(head.Next != null && msg.Timestamp >= head.Next.Timestamp) {
                    head = head.Next;
                }
                msg.Next = head.Next;
                head.Next = msg;
            }
        }
        return true;
    }
    private static void NotifyScheduled() {
        EventScheduled? head = null;
        lock(Mtx) {
            long currentTimestamp = CalcTimestamp();
            while(UnsafeScheduled != null && UnsafeScheduled.Timestamp < currentTimestamp) {
                var msg = UnsafeScheduled;
                UnsafeScheduled = msg.Next;
                msg.Next = head;
                head = msg;
            }
            if(UnsafeScheduled != null) {
                var timeout = UnsafeScheduled.Timestamp - currentTimestamp;
                UpdateTimer(timeout);
            }
        }
        while(head != null) {
            head.Actor.PushNow(head);
            head = head.Next;
        }
    }
    private static void RemoveScheduled(Actor actor) {
        lock(Mtx) {
            while(UnsafeScheduled != null && UnsafeScheduled.Actor == actor) {
                UnsafeScheduled = UnsafeScheduled.Next;
            }
            if(UnsafeScheduled == null) {
                return;
            }
            var head = UnsafeScheduled;
            while(head.Next != null) {
                var next = head.Next;
                if(next.Actor == actor) {
                    head.Next = next.Next;
                } else {
                    head = head.Next;
                }
            }
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