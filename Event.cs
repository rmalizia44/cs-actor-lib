namespace Actors;

public struct Event: IComparable<Event> {
    public readonly object Data;
    public readonly long Timestamp;
    public Event(object data, long timestamp) {
        Data = data;
        Timestamp = timestamp;
    }
    public int CompareTo(Event other) {
        return (int)(Timestamp - other.Timestamp);
    }
}
