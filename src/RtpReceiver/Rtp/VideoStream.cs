using System.Net;
using Microsoft.Extensions.Logging;

namespace RtpReceiver.Rtp;

public class VideoStream : MediaStream
{
    private readonly ILogger _logger;

    private RtpVideoFramer? _rtpVideoFramer;

    public VideoStream(
        RtpSessionConfig config,
        int index,
        ILogger logger)
        : base(config, index)
    {
        _logger = logger;
        MediaType = SDPMediaTypesEnum.video;
    }

    /// <summary>
    /// Gets fired when a full video frame is reconstructed from one or more RTP packets
    /// received from the remote party.
    /// </summary>
    /// <remarks>
    ///  - Received from end point,
    ///  - The frame timestamp,
    ///  - The encoded video frame payload.
    ///  - The video format of the encoded frame.
    /// </remarks>
    public event Action<int, IPEndPoint, uint, byte[], VideoFormat>? OnVideoFrameReceivedByIndex;

    /// <summary>
    /// Indicates whether this session is using video.
    /// </summary>
    public bool HasVideo => RemoteTrack != null && RemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive;

    /// <summary>
    /// Indicates the maximum frame size that can be reconstructed from RTP packets during the depacketisation
    /// process.
    /// </summary>
    public int MaxReconstructedVideoFrameSize { get; set; } = 1048576;

    public void ProcessVideoRtpFrame(IPEndPoint endpoint, RTPPacket packet, SDPAudioVideoMediaFormat format)
    {
        if (OnVideoFrameReceivedByIndex == null)
        {
            return;
        }

        if (_rtpVideoFramer != null)
        {
            var frame = _rtpVideoFramer.GotRtpPacket(packet);
            if (frame != null)
            {
                OnVideoFrameReceivedByIndex?.Invoke(Index, endpoint, packet.Header.Timestamp, frame, format.ToVideoFormat());
            }
        }
        else
        {
            if (format.ToVideoFormat().Codec == VideoCodecsEnum.VP8 ||
                format.ToVideoFormat().Codec == VideoCodecsEnum.H264)
            {
                _logger.LogDebug("Video depacketisation codec set to {Codec} for SSRC {SyncSource}.", format.ToVideoFormat().Codec, packet.Header.SyncSource);

                _rtpVideoFramer = new RtpVideoFramer(format.ToVideoFormat().Codec, MaxReconstructedVideoFrameSize);

                var frame = _rtpVideoFramer.GotRtpPacket(packet);
                if (frame != null)
                {
                    OnVideoFrameReceivedByIndex?.Invoke(Index, endpoint, packet.Header.Timestamp, frame, format.ToVideoFormat());
                }
            }
            else
            {
                _logger.LogWarning("Video depacketisation logic for codec {formatName} has not been implemented, PR's welcome!", format.Name());
            }
        }
    }


}