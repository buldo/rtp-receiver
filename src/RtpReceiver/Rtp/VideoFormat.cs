namespace RtpReceiver.Rtp;

public struct VideoFormat
{
    private const int DynamicIdMax = 127;
    private const int DefaultClockRate = 90000;

    public static readonly VideoFormat Empty = new()
    {
        ClockRate = DefaultClockRate
    };

    /// <summary>
    /// Creates a new video format based on a well known codec.
    /// </summary>
    public VideoFormat(VideoCodecsEnum codec, int formatID, int clockRate = DefaultClockRate, string parameters = null)
        : this(formatID, codec.ToString(), clockRate, parameters)
    { }

    /// <summary>
    /// Creates a new video format based on a dynamic codec (or an unsupported well known codec).
    /// </summary>
    public VideoFormat(int formatId, string formatName, int clockRate = DefaultClockRate, string parameters = null)
    {
        if (formatId < 0)
        {
            // Note format ID's less than the dynamic start range are allowed as the codec list
            // does not currently support all well known codecs.
            throw new ApplicationException("The format ID for an VideoFormat must be greater than 0.");
        }
        else if (formatId > DynamicIdMax)
        {
            throw new ApplicationException($"The format ID for an VideoFormat exceeded the maximum allowed vale of {DynamicIdMax}.");
        }
        else if (string.IsNullOrWhiteSpace(formatName))
        {
            throw new ApplicationException($"The format name must be provided for a VideoFormat.");
        }
        else if (clockRate <= 0)
        {
            throw new ApplicationException($"The clock rate for a VideoFormat must be greater than 0.");
        }

        FormatId = formatId;
        FormatName = formatName;
        ClockRate = clockRate;
        Parameters = parameters;

        if (Enum.TryParse<VideoCodecsEnum>(FormatName, out var videoCodec))
        {
            Codec = videoCodec;
        }
        else
        {
            Codec = VideoCodecsEnum.Unknown;
        }
    }

    public VideoCodecsEnum Codec { get; }

    /// <summary>
    /// The format ID for the codec. If this is a well known codec it should be set to the
    /// value from the codec enum. If the codec is a dynamic it must be set between 96–127
    /// inclusive.
    /// </summary>
    public int FormatId { get; }

    /// <summary>
    /// The official name for the codec. This field is critical for dynamic codecs
    /// where it is used to match the codecs in the SDP offer/answer.
    /// </summary>
    public string FormatName { get; }

    /// <summary>
    /// The rate used by decoded samples for this video format.
    /// </summary>
    /// <remarks>
    /// Example, 90000 is the clock rate:
    /// a=rtpmap:102 H264/90000
    /// </remarks>
    public int ClockRate { get; private init; }

    /// <summary>
    /// This is the "a=fmtp" format parameter that will be set in the SDP offer/answer.
    /// This field should be set WITHOUT the "a=fmtp:0" prefix.
    /// </summary>
    /// <remarks>
    /// Example:
    /// a=fmtp:102 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f"
    /// </remarks>
    public string Parameters { get; }
}