using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RtpReceiver.Rtp;

public class VideoStream : MediaStream
{
    protected static ILogger logger = new NullLogger<VideoStream>();

    protected RtpVideoFramer RtpVideoFramer;

    /// <summary>
    /// Gets fired when the remote SDP is received and the set of common video formats is set.
    /// </summary>
    public event Action<int, List<VideoFormat>> OnVideoFormatsNegotiatedByIndex;

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
    public event Action<int, IPEndPoint, uint, byte[], VideoFormat> OnVideoFrameReceivedByIndex;

    /// <summary>
    /// Indicates whether this session is using video.
    /// </summary>
    public bool HasVideo
    {
        get
        {
            return RemoteTrack != null && RemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive;
        }
    }

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

        if (RtpVideoFramer != null)
        {
            var frame = RtpVideoFramer.GotRtpPacket(packet);
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
                logger.LogDebug($"Video depacketisation codec set to {format.ToVideoFormat().Codec} for SSRC {packet.Header.SyncSource}.");

                RtpVideoFramer = new RtpVideoFramer(format.ToVideoFormat().Codec, MaxReconstructedVideoFrameSize);

                var frame = RtpVideoFramer.GotRtpPacket(packet);
                if (frame != null)
                {
                    OnVideoFrameReceivedByIndex?.Invoke(Index, endpoint, packet.Header.Timestamp, frame, format.ToVideoFormat());
                }
            }
            else
            {
                logger.LogWarning($"Video depacketisation logic for codec {format.Name()} has not been implemented, PR's welcome!");
            }
        }
    }

    public VideoStream(RtpSessionConfig config, int index) : base(config, index)
    {
        MediaType = SDPMediaTypesEnum.video;
    }
}