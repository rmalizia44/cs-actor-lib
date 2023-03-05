using System.Threading.Channels;

namespace Actors;

public class Actor {
    private readonly Channel<Event> Queue;
    private readonly TaskScheduler TaskScheduler;
    private State State = null!;
    public Actor() {
        Queue = Channel.CreateUnbounded<Event>();
        TaskScheduler = new ConcurrentExclusiveSchedulerPair()
                .ExclusiveScheduler;
    }
    public Task Start(State state) {
        return Spawn(async () => {
            if(State != null) {
                throw new Exception("actor already initialized");
            }
            State = state;
            await Loop();
        });
    }
    public Task Reset(State state) {
        return Spawn(async () => {
            await State.DisposeAsync();
            State = state;
            await State.StartAsync();
        });
    }
    public Cancellable Send(object data, int delay = 0) {
        long timestamp = Scheduler.Singleton.CalcTimestamp();
        if(delay > 0) {
            return Scheduler.Singleton.PushScheduled(
                new EventScheduled(this, data, timestamp + delay)
            );
        } else {
            return PushNow(
                new Event(data, timestamp)
            );
        }
    }
    public void Kill() {
        Scheduler.Singleton.RemoveScheduled(this);
        Queue.Writer.TryComplete();
    }
    internal Cancellable PushNow(Event msg) {
        Queue.Writer.TryWrite(msg);
        return AlwaysFalseCancellable.Singleton;
    }
    private Task Spawn(Func<Task> func) {
        return Task.Factory.StartNew(
                func,
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler
            ).Unwrap();
    }
    private async Task Loop() {
        try {
            await State.StartAsync();
            await foreach(var e in Queue.Reader.ReadAllAsync()) {
                await State.ReactAsync(e);
            }
            await State.DisposeAsync();
        } catch (Exception e) {
            Console.WriteLine(e);
        }
    }
}