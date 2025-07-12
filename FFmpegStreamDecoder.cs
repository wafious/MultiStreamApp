using FFmpeg.AutoGen;
using NAudio.Wave;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace MultiStreamApp
{
    public static unsafe class FFmpegStreamDecoder
    {
        private static bool ffmpegRegistered = false;

        public static void StartDecoding(string url, Dispatcher dispatcher, Action<WriteableBitmap> onFrameReady)
        {
            if (!ffmpegRegistered)
            {
                ffmpeg.avdevice_register_all();
                ffmpeg.avformat_network_init();
                ffmpegRegistered = true;
            }


            Task.Run(() =>
            {
                Debug.WriteLine("▶ StartDecoding called");

                AVFormatContext* formatContext = ffmpeg.avformat_alloc_context();
                if (ffmpeg.avformat_open_input(&formatContext, url, null, null) != 0)
                {
                    Debug.WriteLine("❌ avformat_open_input FAILED");
                    return;
                }
                Debug.WriteLine("✅ avformat_open_input SUCCESS");

                if (ffmpeg.avformat_find_stream_info(formatContext, null) < 0)
                {
                    Debug.WriteLine("❌ avformat_find_stream_info FAILED");
                    return;
                }
                Debug.WriteLine("✅ Stream info parsed");





                int audioStreamIndex = -1;
                AVCodecContext* audioCodecContext = null;
                BufferedWaveProvider? waveProvider = null;
                WaveOutEvent? waveOut = null;
                AVCodec* codec = null;
                AVCodecContext* codecContext = null;
                int videoStreamIndex = -1;

                for (int i = 0; i < formatContext->nb_streams; i++)
                {
                    var codecpar = formatContext->streams[i]->codecpar;
                    Debug.WriteLine($"🔍 Stream[{i}] Type: {codecpar->codec_type}");

                    if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                    {
                        videoStreamIndex = i;
                        codec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
                        codecContext = ffmpeg.avcodec_alloc_context3(codec);
                        ffmpeg.avcodec_parameters_to_context(codecContext, codecpar);

                        if (ffmpeg.avcodec_open2(codecContext, codec, null) < 0)
                        {
                            Debug.WriteLine("❌ avcodec_open2 FAILED");
                            return;
                        }

                        Debug.WriteLine("✅ Video decoder opened");
                        break;
                    }
                    else if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                    {
                        audioStreamIndex = i;
                        var audioCodec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
                        audioCodecContext = ffmpeg.avcodec_alloc_context3(audioCodec);
                        ffmpeg.avcodec_parameters_to_context(audioCodecContext, codecpar);
                        ffmpeg.avcodec_open2(audioCodecContext, audioCodec, null);

                        var waveFormat = new WaveFormat(audioCodecContext->sample_rate, audioCodecContext->channels);
                        waveProvider = new BufferedWaveProvider(waveFormat);
                        waveOut = new WaveOutEvent();
                        waveOut.Init(waveProvider);
                        waveOut.Play();



                        Debug.WriteLine("🎶 Audio decoder ready");
                    }
                }

                if (videoStreamIndex == -1)
                {
                    Debug.WriteLine("❌ No video stream found");
                    return;
                }

                AVFrame* frame = ffmpeg.av_frame_alloc();
                AVPacket* packet = ffmpeg.av_packet_alloc();
                SwsContext* swsContext = ffmpeg.sws_getContext(
                    codecContext->width, codecContext->height, codecContext->pix_fmt,
                    codecContext->width, codecContext->height, AVPixelFormat.AV_PIX_FMT_BGR24,
                    ffmpeg.SWS_BICUBIC, null, null, null);

                byte[] buffer = new byte[codecContext->width * codecContext->height * 3];
                fixed (byte* pBuffer = buffer)
                {
                    byte_ptrArray4 dstData = new byte_ptrArray4();
                    int_array4 dstLinesize = new int_array4();
                    dstData[0] = pBuffer;
                    dstLinesize[0] = codecContext->width * 3;

                    Debug.WriteLine("🎥 Starting read loop");
                    while (ffmpeg.av_read_frame(formatContext, packet) >= 0)
                    {
                        if (packet->stream_index != videoStreamIndex)
                        {
                            ffmpeg.av_packet_unref(packet);
                            continue;
                        } else if (packet->stream_index == audioStreamIndex && audioCodecContext != null)
                        {
                            if (ffmpeg.avcodec_send_packet(audioCodecContext, packet) == 0)
                            {
                                AVFrame* audioFrame = ffmpeg.av_frame_alloc();
                                while (ffmpeg.avcodec_receive_frame(audioCodecContext, audioFrame) == 0)
                                {
                                    int bytesPerSample = ffmpeg.av_get_bytes_per_sample(audioCodecContext->sample_fmt);
                                    if (bytesPerSample <= 0) continue;

                                    int sampleCount = audioFrame->nb_samples * audioCodecContext->channels;
                                    int bufferSize = sampleCount * bytesPerSample;

                                    byte[] sampleBuffer = new byte[bufferSize];
                                    Marshal.Copy((IntPtr)audioFrame->data[0], sampleBuffer, 0, bufferSize);
                                    waveProvider?.AddSamples(sampleBuffer, 0, sampleBuffer.Length);

                                    Debug.WriteLine($"🔊 Played {sampleCount} samples");
                                }
                                ffmpeg.av_frame_free(&audioFrame);
                            }
                        }

                        int sendResult = ffmpeg.avcodec_send_packet(codecContext, packet);
                        if (sendResult != 0)
                        {
                            Debug.WriteLine("⚠️ avcodec_send_packet failed with code " + sendResult);
                            ffmpeg.av_packet_unref(packet);
                            continue;
                        }

                        while (ffmpeg.avcodec_receive_frame(codecContext, frame) == 0)
                        {
                            Debug.WriteLine("✅ Frame received: " + frame->width + "x" + frame->height);

                            ffmpeg.sws_scale(
                                swsContext,
                                frame->data,
                                frame->linesize,
                                0,
                                codecContext->height,
                                dstData,
                                dstLinesize);

                            int w = codecContext->width;
                            int h = codecContext->height;
                            dispatcher.Invoke(() =>
                            {
                                var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr24, null);
                                bmp.WritePixels(new Int32Rect(0, 0, w, h), buffer, w * 3, 0);
                                onFrameReady(bmp);
                                Debug.WriteLine("🖼️ Bitmap dispatched to UI");
                            });
                        }

                        ffmpeg.av_packet_unref(packet);
                    }
                }

                Debug.WriteLine("🏁 Decoding finished");
                ffmpeg.av_frame_free(&frame);
                ffmpeg.av_packet_free(&packet);
                ffmpeg.avcodec_free_context(&codecContext);
                ffmpeg.avformat_close_input(&formatContext);
                ffmpeg.avformat_free_context(formatContext);
                waveOut?.Stop();
                waveOut?.Dispose();
                waveProvider = null;
                ffmpeg.avcodec_free_context(&audioCodecContext);
            });

        }
    }

}