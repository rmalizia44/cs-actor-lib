# Actor Library

Tries to implement the (Re)Actor Pattern in C#, as described by Sergey Ignatchenko

## Reactors Can

```cs
class MyState: State {
    private Actor Self;
    private Actor Observer;
    public MyState(Actor self, Actor observer) {
        Self = self;
        Observer = observer;
    }
    public async ValueTask StartAsync() {
        // ...
    }
    public async ValueTask DisposeAsync() {
        // ...
    }
    public async ValueTask ReactAsync(Event e) {
        // ...
    }
}
```

### Send messages to other Reactors

```cs
Observer.Send("hello");
```

### Post timer events to themselves

```cs
Self.Send("update", 1000);
```

### Initiate non-blocking calls

```cs
await Task.Delay(100);
```

### Request creation of other Reactors

```cs
var actor = new Actor();
var state = new FirstState(actor);
var task = actor.Start(state);
await task;
```
