namespace Actors;

internal class AlwaysFalseCancellable: Cancellable {
    public bool Cancel() {
        return false;
    }
}