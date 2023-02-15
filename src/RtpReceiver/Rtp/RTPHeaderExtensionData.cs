namespace RtpReceiver.Rtp;

public class RTPHeaderExtensionData
{
    public RTPHeaderExtensionData(int id, byte[] data, RTPHeaderExtensionType type)
    {
        Id = id;
        Data = data;
        Type = type;
    }
    public int Id { get; }
    public byte[] Data { get; }
    public RTPHeaderExtensionType Type { get; }

    public RTPHeaderExtensionUri.Type? GetUriType(Dictionary<int, RTPHeaderExtension> map)
    {
        return !map.ContainsKey(Id) ? null : map[Id].Type;
    }


    public ulong? GetNtpTimestamp(Dictionary<int, RTPHeaderExtension> extensions)
    {
        var extensionType = GetUriType(extensions);
        if (extensionType != RTPHeaderExtensionUri.Type.AbsCaptureTime)
        {
            return null;
        }

        return GetUlong(0);
    }

    public ulong? GetUlong(int offset)
    {
        if (offset + sizeof(ulong) - 1 >= Data.Length)
        {
            return null;
        }

        return BitConverter.IsLittleEndian ?
            NetConvert.DoReverseEndian(BitConverter.ToUInt64(Data, offset)) :
            BitConverter.ToUInt64(Data, offset);
    }
}