﻿namespace RtpReceiver.Rtp;

public enum AudioCodecsEnum
{
    PCMU,
    GSM,
    G723,
    DVI4,
    LPC,
    PCMA,
    G722,
    L16,
    QCELP,
    CN,
    MPA,
    G728,
    G729,
    OPUS,

    PCM_S16LE,  // PCM signed 16-bit little-endian (equivalent to FFmpeg s16le). For use with Azure, not likely to be supported in VoIP/WebRTC.

    Unknown
}