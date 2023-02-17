namespace Actors;

public class Event: IComparable<Event> {
    public readonly object Data;
    public readonly long Timestamp;
    internal Event(object data, long timestamp) {
        Data = data;
        Timestamp = timestamp;
    }
    public int CompareTo(Event? other) {
        return (int)(Timestamp - other!.Timestamp);
    }
}
