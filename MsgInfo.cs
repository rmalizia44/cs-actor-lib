namespace Reactors;

internal class MsgInfo: IComparable<MsgInfo> {
    public object Data;
    public readonly long Timestamp;
    public MsgInfo(object data, long timestamp) {
        Data = data;
        Timestamp = timestamp;
    }
    public int CompareTo(MsgInfo? other) {
        return (int)(Timestamp - other!.Timestamp);
    }
}
