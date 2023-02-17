using System.Threading.Channels;

namespace Actors;

public class Actor {
    private static readonly Cancellable CantCancel = new AlwaysFalseCancellable();
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
            var _task = old.DisposeAsync();
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
        return CantCancel;
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