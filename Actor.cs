using System.Threading.Channels;

namespace Actors;

public class Actor {
    private readonly Channel<Event> Queue;
    private readonly TaskScheduler TaskScheduler;
    private State State = null!;
    private Task Task = null!;
    public Actor() {
        Queue = Channel.CreateUnbounded<Event>();
        TaskScheduler = new ConcurrentExclusiveSchedulerPair()
                .ExclusiveScheduler;
    }
    // TODO: not thread safe, can only be used in initialization and in self ReactAsync method
    public Task Reset(State state) {
        var old = State;
        State = state;
        if(old == null) {
            Task = Task.Factory.StartNew(
                async () => await Loop(),
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler
            ).Unwrap();
        } else {
            Task.Factory.StartNew(
                async () => await old.DisposeAsync(),
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler
            ).Unwrap();
        }
        return Task;
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
    private async Task Loop() {
        bool running = true;
        while(running) {
            try {
                await foreach(var e in Queue.Reader.ReadAllAsync()) {
                    await State.ReactAsync(e);
                }
                running = false;
            } catch (Exception e) {
                Console.WriteLine(e);
            }
        }
        try {
            await State.DisposeAsync();
        } catch (Exception e) {
            Console.WriteLine(e);
        }
    }
}