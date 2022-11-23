using System.Threading.Channels;

namespace Reactors;

public class Actor {
    private readonly Reactor Reactor;
    private readonly Channel<MsgInfo> Queue;
    private readonly DateTime StartTime;
    private readonly Timer Timer;
    private readonly List<MsgInfo> UnsafeMsgScheduled = new();
    private readonly List<MsgInfo> MsgReady = new();
    public readonly Task Task;
    public Actor(Reactor reactor) {
        Reactor = reactor;
        Queue = Channel.CreateUnbounded<MsgInfo>();
        StartTime = DateTime.Now;
        Timer = new(o => NotifyTimer());
        var scheduler = new ConcurrentExclusiveSchedulerPair()
            .ExclusiveScheduler;
        Task = Task.Factory.StartNew(
            () => Loop(),
            CancellationToken.None,
            TaskCreationOptions.None,
            scheduler
        ).Unwrap();
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
        if(nextTimestamp >= 0) {
            UpdateTimer(nextTimestamp - currentTimestamp);
        }
        foreach(var msg in MsgReady) {
            PushNow(msg);
        }
        MsgReady.Clear();
    }
    private async Task Loop() {
        try {
            await Reactor.Start(this);
            bool running = true;
            while(running) {
                try {
                    await foreach(var msg in Queue.Reader.ReadAllAsync()) {
                        await Reactor.React(msg.Data, msg.Timestamp);
                    }
                    running = false;
                } catch (Exception e) {
                    Console.WriteLine(e);
                }
            }
            try {
                await Reactor.Terminate();
            } catch (Exception e) {
                Console.WriteLine(e);
            }
        } catch (Exception e) {
            Console.WriteLine(e);
        }
    }
}