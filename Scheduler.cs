namespace Actors;

internal class Scheduler {
    internal static readonly Scheduler Singleton = new();
    private readonly DateTime StartTime = DateTime.Now;
    private readonly Timer Timer;
    private readonly object Mtx = new();
    private EventScheduled? UnsafeScheduled = null;
    internal Scheduler() {
        Timer = new(o => NotifyScheduled());
    }
    internal long CalcTimestamp() {
        return (long)((DateTime.Now - StartTime).TotalMilliseconds + 0.5);
    }
    private void UpdateTimer(long timeout) {
        if(timeout < 0) {
            timeout = 0;
        }
        Timer.Change(timeout, Timeout.Infinite);
    }
    internal Cancellable PushScheduled(EventScheduled msg) {
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
    private EventScheduled? ReverseOrder(EventScheduled? src) {
        EventScheduled? dst = null;
        while(src != null) {
            var itr = src;
            src = src.Next;
            itr.Next = dst;
            dst = itr;
        }
        return dst;
    }
    private void NotifyScheduled() {
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
    internal void RemoveScheduled(Actor actor) {
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
    internal bool TryCancelScheduled(EventScheduled msg) {
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
}
