namespace RtpReceiver.Rtp;

public class RTPHeaderExtension
{
    public RTPHeaderExtension(int id, string uri)
    {
        Id = id;
        Uri = uri;
    }
    public int Id { get; }
    public string Uri { get; }

    public RTPHeaderExtensionUri.Type? Type => RTPHeaderExtensionUri.GetType(Uri);
}