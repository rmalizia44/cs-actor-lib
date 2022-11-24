using System.Threading.Channels;

namespace Actors;

public class Actor {
    private static readonly Exception Exception = new NotImplementedException();
    private readonly DateTime StartTime;
    private readonly Timer Timer;
    private readonly Channel<Event> Queue;
    private readonly List<Event> UnsafeMsgScheduled = new();
    private State State = null!;
    private Task Task = Task.FromException(Exception);
    public Actor() {
        StartTime = DateTime.Now;
        Timer = new(o => NotifyTimer());
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
            return PostDelay(new Event(data, timestamp + delay));
        } else {
            return PushNow(new Event(data, timestamp));
        }
    }
    public void Kill() {
        Queue.Writer.Complete();
    }
    private long CalcTimestamp() {
        return (long)((DateTime.Now - StartTime).TotalMilliseconds + 0.5);
    }
    private bool PushNow(Event msg) {
        return Queue.Writer.TryWrite(msg);
    }
    private bool PostDelay(Event msg) {
        long firstTimestamp = -1;
        lock(UnsafeMsgScheduled) {
            var idx = UnsafeMsgScheduled.BinarySearch(msg);
            if(idx < 0) {
                idx = ~idx;
            }
            UnsafeMsgScheduled.Insert(idx, msg);
            if(idx == 0) {
                firstTimestamp = msg.Timestamp;
            }
        }
        if(firstTimestamp >= 0) {
            UpdateTimer(firstTimestamp - CalcTimestamp());
        }
        return true;
    }
    private void UpdateTimer(long timeout) {
        if(timeout < 0) {
            timeout = 0;
        }
        Timer.Change(timeout, Timeout.Infinite);
    }
    private void NotifyTimer() {
        Console.WriteLine("NotifyTimer");
        List<Event> MsgReady = new();
        long currentTimestamp = CalcTimestamp();
        long nextTimestamp = -1;
        lock(UnsafeMsgScheduled) {
            while(UnsafeMsgScheduled.Any() && UnsafeMsgScheduled[0].Timestamp < currentTimestamp) {
                Console.WriteLine("MsgReady");
                MsgReady.Add(UnsafeMsgScheduled[0]);
                UnsafeMsgScheduled.RemoveAt(0);
            }
            if(UnsafeMsgScheduled.Any()) {
                nextTimestamp = UnsafeMsgScheduled[0].Timestamp;
            }
        }
        foreach(var msg in MsgReady) {
            PushNow(msg);
        }
        if(nextTimestamp >= 0) {
            UpdateTimer(nextTimestamp - currentTimestamp);
        }
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