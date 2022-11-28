using System.Threading.Channels;

namespace Actors;

public interface Cancellable {
    bool Cancel();
}

internal class EventScheduled: Event, Cancellable {
    public readonly Actor Actor;
    public EventScheduled? Next = null;
    public EventScheduled(Actor actor, object data, long timestamp): base(data, timestamp) {
        Actor = actor;
    }
    public bool Cancel() {
        return Actor.TryCancelScheduled(this);
    }
}

internal class DummyCancellable: Cancellable {
    public bool Cancel() {
        return false;
    }
}

public class Actor {
    private static readonly Cancellable CantCancel = new DummyCancellable();
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
    private static Cancellable PushScheduled(EventScheduled msg) {
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
        return msg;
    }
    private static EventScheduled? ReverseOrder(EventScheduled? src) {
        EventScheduled? dst = null;
        while(src != null) {
            var itr = src;
            src = src.Next;
            itr.Next = dst;
            dst = itr;
        }
        return dst;
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
        head = ReverseOrder(head);
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
    internal static bool TryCancelScheduled(EventScheduled msg) {
        lock(Mtx) {
            if(UnsafeScheduled == null) {
                return false;
            }
            if(UnsafeScheduled == msg) {
                UnsafeScheduled = UnsafeScheduled.Next;
                return true;
            }
            var head = UnsafeScheduled;
            while(head.Next != null) {
                var next = head.Next;
                if(next == msg) {
                    head.Next = next.Next;
                    return true;
                }
                head = head.Next;
            }
        }
        return false;
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
    public Cancellable Send(object data, int delay = 0) {
        long timestamp = CalcTimestamp();
        if(delay > 0) {
            return PushScheduled(
                new EventScheduled(this, data, timestamp + delay)
            );
        } else {
            return PushNow(
                new Event(data, timestamp)
            );
        }
    }
    public void Kill() {
        RemoveScheduled(this);
        Queue.Writer.TryComplete();
    }
    private Cancellable PushNow(Event msg) {
        Queue.Writer.TryWrite(msg);
        return CantCancel;
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