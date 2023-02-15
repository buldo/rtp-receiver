namespace RtpReceiver.Rtp;

public class SDPMediaTypes
{
    public static SDPMediaTypesEnum GetSDPMediaType(string mediaType)
    {
        return (SDPMediaTypesEnum)Enum.Parse(typeof(SDPMediaTypesEnum), mediaType, true);
    }
    public static SDPMediaTypesEnum GetSDPMediaType(int mediaType)
    {
        return (SDPMediaTypesEnum)mediaType;
    }
}