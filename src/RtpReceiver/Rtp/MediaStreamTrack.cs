namespace RtpReceiver.Rtp;

public class MediaStreamTrack
{
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

    // <summary>
    ///  a=extmap - Mapping for RTP header extensions
    /// </summary>
    public Dictionary<int, RTPHeaderExtension> HeaderExtensions { get; }

    /// <summary>
    /// Creates a lightweight class to track a media stream track within an RTP session
    /// When supporting RFC3550 (the standard RTP specification) the relationship between
    /// an RTP stream and session is 1:1. For WebRTC and RFC8101 there can be multiple
    /// streams per session.
    /// </summary>
    public MediaStreamTrack(Dictionary<int, RTPHeaderExtension>? headerExtensions = null)
    {
        HeaderExtensions = headerExtensions ?? new Dictionary<int, RTPHeaderExtension>();
    }
}