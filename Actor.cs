using System.Threading.Channels;

namespace Actors;

public class Actor {
    private static Exception Exception = new NotImplementedException();
    private readonly DateTime StartTime;
    private readonly Timer Timer;
    private readonly Channel<MsgInfo> Queue;
    private readonly List<MsgInfo> UnsafeMsgScheduled = new();
    private readonly List<MsgInfo> MsgReady = new();
    private State State = null!;
    private Task Task = Task.FromException(Exception);
    public Actor() {
        StartTime = DateTime.Now;
        Timer = new(o => NotifyTimer());
        Queue = Channel.CreateUnbounded<MsgInfo>();
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
            return PostDelay(new MsgInfo(data, timestamp + delay));
        } else {
            return PushNow(new MsgInfo(data, timestamp));
        }
    }
    public void Kill() {
        Queue.Writer.Complete();
    }
    private long CalcTimestamp() {
        return (long)((DateTime.Now - StartTime).TotalMilliseconds + 0.5);
    }
    private bool PushNow(MsgInfo msg) {
        return Queue.Writer.TryWrite(msg);
    }
    private bool PostDelay(MsgInfo msg) {
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
        long currentTimestamp = CalcTimestamp();
        long nextTimestamp = -1;
        lock(UnsafeMsgScheduled) {
            MsgInfo? next;
            do {
                MsgReady.Add(UnsafeMsgScheduled[0]);
                UnsafeMsgScheduled.RemoveAt(0);
                next = UnsafeMsgScheduled.FirstOrDefault();
            } while(next != null && next.Timestamp < currentTimestamp);
            if(next != null) {
                nextTimestamp = next.Timestamp;
            }
        }
        foreach(var msg in MsgReady) {
            PushNow(msg);
        }
        MsgReady.Clear();
        if(nextTimestamp >= 0) {
            UpdateTimer(nextTimestamp - currentTimestamp);
        }
    }
    private async Task Loop() {
        bool running = true;
        while(running) {
            try {
                await foreach(var msg in Queue.Reader.ReadAllAsync()) {
                    await State.React(msg.Data, msg.Timestamp);
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