using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RtpReceiver;
using Terminal.Gui;

Application.Run<ExampleWindow>();
Application.Shutdown();

public class ExampleWindow : Window
{
    private readonly Receiver _receiver;

    private int _framesCount = 0;

    public TextField FramesCountText;

    public ExampleWindow()
    {
        Title = "Example App (Ctrl+Q to quit)";

        // Create input components and labels
        var frameCountLabel = new Label
        {
            Text = "Received frames:"
        };

        FramesCountText = new TextField("")
        {
            X = Pos.Right(frameCountLabel) + 1,
            Width = Dim.Fill(),
            ReadOnly = true,
            Text = _framesCount.ToString()
        };

        // Add the views to the Window
        Add(frameCountLabel, FramesCountText);

        _receiver = new Receiver(new IPEndPoint(IPAddress.Any, 5600), new NullLogger<Receiver>());
        _receiver.OnVideoFrameReceivedByIndex += ReceiverOnOnVideoFrameReceivedByIndex;
        _receiver.Start();
    }

    private void ReceiverOnOnVideoFrameReceivedByIndex(int arg1, IPEndPoint arg2, uint arg3, byte[] arg4)
    {
        _framesCount++;
        Application.MainLoop.Invoke(() => {
            FramesCountText.Text = _framesCount.ToString();
        });
    }
}