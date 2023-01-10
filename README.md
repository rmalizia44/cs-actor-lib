# Actor Library

Tries to implement the (Re)Actor Pattern in C#, as described by Sergey Ignatchenko

## Reactors Can

```c#
class MyState: State {
    private Actor Self;
    private Actor Observer;
    public MyState(Actor self, Actor observer) {
        Self = self;
        Observer = observer;
        // ...
    }
    public void Dispose() {
        // ...
    }
    public async Task React(Event e) {
        // ...
    }
}
```

### Send messages to other Reactors

```c#
Observer.Send("hello");
```

### Post timer events to themselves

```c#
Self.Send("update", 1000);
```

### Initiate non-blocking calls

```c#
await Task.Delay(100);
```

### Request creation of other Reactors

```c#
var actor = new Actor();
var state = new FirstState(actor);
var task = actor.Reset(state);
await task;
```
