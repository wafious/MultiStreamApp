using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows;

namespace MultiStreamApp
{
    public partial class VideoStreamControl : UserControl
    {

        string _streamUrl;

        public VideoStreamControl(string streamUrl)
        {
            InitializeComponent();
            this.Loaded += VideoStreamControl_Loaded;
            _streamUrl = streamUrl;

        }

        private void VideoStreamControl_Loaded(object sender, RoutedEventArgs e)
        {
            string testStream = _streamUrl;
            LoadStream(testStream);
        }

        public void LoadStream(string url)
        {
            FFmpegStreamDecoder.StartDecoding(url, this.Dispatcher, OnFrameDecoded);
        }

        private void OnFrameDecoded(WriteableBitmap bitmap)
        {
            VideoSurface.Source = bitmap;
        }
    }
}
