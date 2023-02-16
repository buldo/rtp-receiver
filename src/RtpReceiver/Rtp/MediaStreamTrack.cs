using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RtpReceiver.Rtp;

public class MediaStreamTrack
{
    private static ILogger logger = new NullLogger<MediaStreamTrack>();

    /// <summary>
    /// The type of media stream represented by this track. Must be audio or video.
    /// </summary>
    private SDPMediaTypesEnum Kind { get; set; }

    /// <summary>
    /// The value used in the RTP Synchronisation Source header field for media packets
    /// sent using this media stream.
    /// Be careful that the RTP Synchronisation Source header field should not be changed
    /// unless specific implementations require it. By default this value is chosen randomly,
    /// with the intent that no two synchronization sources within the same RTP session
    /// will have the same SSRC.
    /// </summary>
    public uint Ssrc { get; set; }

    /// <summary>
    /// The last seqnum received from the remote peer for this stream.
    /// </summary>
    public ushort LastRemoteSeqNum { get; internal set; }

    /// <summary>
    /// The last abs-capture-time received from the remote peer for this stream.
    /// </summary>
    public TimestampPair LastAbsoluteCaptureTimestamp { get; internal set; }

    // <summary>
    ///  a=extmap - Mapping for RTP header extensions
    /// </summary>
    public Dictionary<int, RTPHeaderExtension> HeaderExtensions { get; }

    /// <summary>
    /// If the SDP remote the remote party provides "a=ssrc" attributes, as specified
    /// in RFC5576, this property will hold the values. The list can be used when
    /// an RTP/RTCP packet is received and needs to be matched against a media type or
    /// RTCP report.
    /// </summary>
    private Dictionary<uint, SDPSsrcAttribute> SdpSsrc { get; set; } = new Dictionary<uint, SDPSsrcAttribute>();

    // The value used in the RTP Sequence Number header field for media packets.
    // Although valid values are all in the range of ushort, the underlying field is of type int, because Interlocked.CompareExchange is used to increment in a fast and thread-safe manner and there is no overload for ushort.
    private int m_seqNum;

    /// <summary>
    /// Creates a lightweight class to track a media stream track within an RTP session
    /// When supporting RFC3550 (the standard RTP specification) the relationship between
    /// an RTP stream and session is 1:1. For WebRTC and RFC8101 there can be multiple
    /// streams per session.
    /// </summary>
    /// <param name="kind">The type of media for this stream. There can only be one
    /// stream per media type.</param>
    /// <param name="isRemote">True if this track corresponds to a media announcement from the
    /// remote party.</param>
    /// <param name="capabilities">The capabilities for the track being added. Where the same media
    /// type is supported locally and remotely only the mutual capabilities can be used. This will
    /// occur if we receive an SDP offer (add track initiated by the remote party) and we need
    /// to remove capabilities we don't support.</param>
    /// <param name="streamStatus">The initial stream status for the media track. Defaults to
    /// send receive.</param>
    /// <param name="ssrcAttributes">Optional. If the track is being created from an SDP announcement this
    /// parameter contains a list of the SSRC attributes that should then match the RTP header SSRC value
    /// for this track.</param>
    public MediaStreamTrack(
        SDPMediaTypesEnum kind,
        List<SDPSsrcAttribute>? ssrcAttributes = null,
        Dictionary<int, RTPHeaderExtension>? headerExtensions = null)
    {
        Kind = kind;
        HeaderExtensions = headerExtensions ?? new Dictionary<int, RTPHeaderExtension>();

        // Add the source attributes from the remote SDP to help match RTP SSRC and RTCP CNAME values against
        // RTP and RTCP packets received from the remote party.
        if (ssrcAttributes?.Count > 0)
        {
            foreach (var ssrcAttr in ssrcAttributes)
            {
                if (!SdpSsrc.ContainsKey(ssrcAttr.SSRC))
                {
                    SdpSsrc.Add(ssrcAttr.SSRC, ssrcAttr);
                }
            }
        }
    }
}