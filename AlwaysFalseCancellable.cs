namespace Actors;

internal class AlwaysFalseCancellable: Cancellable {
    internal static readonly AlwaysFalseCancellable Singleton = new();
    public bool Cancel() {
        return false;
    }
}