namespace RtpReceiver.Rtp;

public static class AudioVideoWellKnown
{
    public static readonly Dictionary<SDPWellKnownMediaFormatsEnum, VideoFormat> WellKnownVideoFormats =
        new()
        {
            { SDPWellKnownMediaFormatsEnum.CELB,     new VideoFormat(VideoCodecsEnum.CELB, 24, 90000)},
            { SDPWellKnownMediaFormatsEnum.JPEG,     new VideoFormat(VideoCodecsEnum.JPEG, 26, 90000)},
            { SDPWellKnownMediaFormatsEnum.NV,       new VideoFormat(VideoCodecsEnum.NV,   28, 90000)},
            { SDPWellKnownMediaFormatsEnum.H261,     new VideoFormat(VideoCodecsEnum.H261, 31, 90000)},
            { SDPWellKnownMediaFormatsEnum.MPV,      new VideoFormat(VideoCodecsEnum.MPV,  32, 90000)},
            { SDPWellKnownMediaFormatsEnum.MP2T,     new VideoFormat(VideoCodecsEnum.MP2T, 33, 90000)},
            { SDPWellKnownMediaFormatsEnum.H263,     new VideoFormat(VideoCodecsEnum.H263, 34, 90000)}
        };
}