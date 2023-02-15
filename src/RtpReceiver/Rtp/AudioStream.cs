namespace RtpReceiver.Rtp;

public class AudioStream : MediaStream
{
    /// <summary>
    /// Gets fired when the remote SDP is received and the set of common audio formats is set.
    /// </summary>
    public event Action<int, List<AudioFormat>> OnAudioFormatsNegotiatedByIndex;

    /// <summary>
    /// Indicates whether this session is using audio.
    /// </summary>
    public bool HasAudio => RemoteTrack != null && RemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive;

    public AudioStream(RtpSessionConfig config, int index) : base(config, index)
    {
        MediaType = SDPMediaTypesEnum.audio;
    }
}