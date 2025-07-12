using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MultiStreamApp
{
    public partial class MainWindow : Window
    {
        private List<VideoStreamControl> streamControls = new();

        public MainWindow()
        {
            InitializeComponent();
            InitializeStreams();
        }

        private void InitializeStreams()
        {
            for (int i = 0; i < 3; i++)
            {
                var streamControl = new VideoStreamControl();
                StreamGrid.Children.Add(streamControl);
                streamControls.Add(streamControl);
            }

            string Url1 = "https://hdl-ws.zego.wakavideos.com/wakavideos/1052880088180203520_h264.flv";
            AddNewStream(Url1);
            //string Url2 = "https://hdl-ws.zego.wakavideos.com/wakavideos/1052905148118999040_h264.flv";
            //AddNewStream(Url2);
            string Url3 = "https://hdl-ws.zego.wakavideos.com/wakavideos/1052905670829940736_h264.flv";
            AddNewStream(Url3);

        }

        public void AddNewStream(string streamUrl)
        {
            var newControl = new VideoStreamControl();
            StreamGrid.Children.Add(newControl);
            streamControls.Add(newControl);
            newControl.LoadStream(streamUrl);

        }
    }
}
