namespace RtpReceiver.Rtp;

public struct SDPApplicationMediaFormat
{
    public string ID;

    public string Rtpmap;

    public string Fmtp;

    public SDPApplicationMediaFormat(string id)
    {
        ID = id;
        Rtpmap = null;
        Fmtp = null;
    }

    public SDPApplicationMediaFormat(string id, string rtpmap, string fmtp)
    {
        ID = id;
        Rtpmap = rtpmap;
        Fmtp = fmtp;
    }

    /// <summary>
    /// Creates a new media format based on an existing format but with a different ID.
    /// The typical case for this is during the SDP offer/answer exchange the dynamic format ID's for the
    /// equivalent type need to be adjusted by one party.
    /// </summary>
    /// <param name="id">The ID to set on the new format.</param>
    public SDPApplicationMediaFormat WithUpdatedID(string id) =>
        new SDPApplicationMediaFormat(id, Rtpmap, Fmtp);

    public SDPApplicationMediaFormat WithUpdatedRtpmap(string rtpmap) =>
        new SDPApplicationMediaFormat(ID, rtpmap, Fmtp);

    public SDPApplicationMediaFormat WithUpdatedFmtp(string fmtp) =>
        new SDPApplicationMediaFormat(ID, Rtpmap, fmtp);
}