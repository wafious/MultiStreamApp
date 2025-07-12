//using FFmpeg.AutoGen;
//using NAudio.Wave;
//using System.Diagnostics;
//using System.Runtime.InteropServices;
//using System.Windows;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;
//using System.Windows.Threading;
//namespace MultiStreamApp
//{
//    public static unsafe class FFmpegStreamDecoder
//    {
//        private static bool ffmpegRegistered = false;

//        public static void StartDecoding(string url, Dispatcher dispatcher, Action<WriteableBitmap> onFrameReady)
//        {
//            if (!ffmpegRegistered)
//            {
//                ffmpeg.avdevice_register_all();
//                ffmpeg.avformat_network_init();
//                ffmpegRegistered = true;
//            }


//            Task.Run(() =>
//            {
//                Debug.WriteLine("▶ StartDecoding called");

//                AVFormatContext* formatContext = ffmpeg.avformat_alloc_context();
//                if (ffmpeg.avformat_open_input(&formatContext, url, null, null) != 0)
//                {
//                    Debug.WriteLine("❌ avformat_open_input FAILED");
//                    return;
//                }
//                Debug.WriteLine("✅ avformat_open_input SUCCESS");

//                if (ffmpeg.avformat_find_stream_info(formatContext, null) < 0)
//                {
//                    Debug.WriteLine("❌ avformat_find_stream_info FAILED");
//                    return;
//                }
//                Debug.WriteLine("✅ Stream info parsed");





//                int audioStreamIndex = -1;
//                AVCodecContext* audioCodecContext = null;
//                BufferedWaveProvider? waveProvider = null;
//                WaveOutEvent? waveOut = null;
//                AVCodec* codec = null;
//                AVCodecContext* codecContext = null;
//                int videoStreamIndex = -1;

//                for (int i = 0; i < formatContext->nb_streams; i++)
//                {
//                    var codecpar = formatContext->streams[i]->codecpar;
//                    Debug.WriteLine($"🔍 Stream[{i}] Type: {codecpar->codec_type}");

//                    if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
//                    {
//                        videoStreamIndex = i;
//                        codec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
//                        codecContext = ffmpeg.avcodec_alloc_context3(codec);
//                        ffmpeg.avcodec_parameters_to_context(codecContext, codecpar);

//                        if (ffmpeg.avcodec_open2(codecContext, codec, null) < 0)
//                        {
//                            Debug.WriteLine("❌ avcodec_open2 FAILED");
//                            return;
//                        }

//                        Debug.WriteLine("✅ Video decoder opened");
//                        break;
//                    }
//                    else if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
//                    {
//                        audioStreamIndex = i;
//                        var audioCodec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
//                        audioCodecContext = ffmpeg.avcodec_alloc_context3(audioCodec);
//                        ffmpeg.avcodec_parameters_to_context(audioCodecContext, codecpar);
//                        ffmpeg.avcodec_open2(audioCodecContext, audioCodec, null);

//                        var waveFormat = new WaveFormat(audioCodecContext->sample_rate, audioCodecContext->channels);
//                        waveProvider = new BufferedWaveProvider(waveFormat);
//                        waveOut = new WaveOutEvent();
//                        waveOut.Init(waveProvider);
//                        waveOut.Play();



//                        Debug.WriteLine("🎶 Audio decoder ready");
//                    }
//                }

//                if (videoStreamIndex == -1)
//                {
//                    Debug.WriteLine("❌ No video stream found");
//                    return;
//                }

//                AVFrame* frame = ffmpeg.av_frame_alloc();
//                AVPacket* packet = ffmpeg.av_packet_alloc();
//                SwsContext* swsContext = ffmpeg.sws_getContext(
//                    codecContext->width, codecContext->height, codecContext->pix_fmt,
//                    codecContext->width, codecContext->height, AVPixelFormat.AV_PIX_FMT_BGR24,
//                    ffmpeg.SWS_BICUBIC, null, null, null);

//                byte[] buffer = new byte[codecContext->width * codecContext->height * 3];
//                fixed (byte* pBuffer = buffer)
//                {
//                    byte_ptrArray4 dstData = new byte_ptrArray4();
//                    int_array4 dstLinesize = new int_array4();
//                    dstData[0] = pBuffer;
//                    dstLinesize[0] = codecContext->width * 3;

//                    Debug.WriteLine("🎥 Starting read loop");
//                    while (ffmpeg.av_read_frame(formatContext, packet) >= 0)
//                    {
//                        if (packet->stream_index != videoStreamIndex)
//                        {
//                            ffmpeg.av_packet_unref(packet);
//                            continue;
//                        } else if (packet->stream_index == audioStreamIndex && audioCodecContext != null)
//                        {
//                            if (ffmpeg.avcodec_send_packet(audioCodecContext, packet) == 0)
//                            {
//                                AVFrame* audioFrame = ffmpeg.av_frame_alloc();
//                                while (ffmpeg.avcodec_receive_frame(audioCodecContext, audioFrame) == 0)
//                                {
//                                    int bytesPerSample = ffmpeg.av_get_bytes_per_sample(audioCodecContext->sample_fmt);
//                                    if (bytesPerSample <= 0) continue;

//                                    int sampleCount = audioFrame->nb_samples * audioCodecContext->channels;
//                                    int bufferSize = sampleCount * bytesPerSample;

//                                    byte[] sampleBuffer = new byte[bufferSize];
//                                    Marshal.Copy((IntPtr)audioFrame->data[0], sampleBuffer, 0, bufferSize);
//                                    waveProvider?.AddSamples(sampleBuffer, 0, sampleBuffer.Length);

//                                    Debug.WriteLine($"🔊 Played {sampleCount} samples");
//                                }
//                                ffmpeg.av_frame_free(&audioFrame);
//                            }
//                        }

//                        int sendResult = ffmpeg.avcodec_send_packet(codecContext, packet);
//                        if (sendResult != 0)
//                        {
//                            Debug.WriteLine("⚠️ avcodec_send_packet failed with code " + sendResult);
//                            ffmpeg.av_packet_unref(packet);
//                            continue;
//                        }

//                        while (ffmpeg.avcodec_receive_frame(codecContext, frame) == 0)
//                        {
//                            Debug.WriteLine("✅ Frame received: " + frame->width + "x" + frame->height);

//                            ffmpeg.sws_scale(
//                                swsContext,
//                                frame->data,
//                                frame->linesize,
//                                0,
//                                codecContext->height,
//                                dstData,
//                                dstLinesize);

//                            int w = codecContext->width;
//                            int h = codecContext->height;
//                            dispatcher.Invoke(() =>
//                            {
//                                var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr24, null);
//                                bmp.WritePixels(new Int32Rect(0, 0, w, h), buffer, w * 3, 0);
//                                onFrameReady(bmp);
//                                Debug.WriteLine("🖼️ Bitmap dispatched to UI");
//                            });
//                        }

//                        ffmpeg.av_packet_unref(packet);
//                    }
//                }

//                Debug.WriteLine("🏁 Decoding finished");
//                ffmpeg.av_frame_free(&frame);
//                ffmpeg.av_packet_free(&packet);
//                ffmpeg.avcodec_free_context(&codecContext);
//                ffmpeg.avformat_close_input(&formatContext);
//                ffmpeg.avformat_free_context(formatContext);
//                waveOut?.Stop();
//                waveOut?.Dispose();
//                waveProvider = null;
//                ffmpeg.avcodec_free_context(&audioCodecContext);
//            });

//        }
//    }

//}

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
        private static unsafe byte[] ResampleAudioFrame(AVFrame* frame, AVCodecContext* ctx)
        {
            if (ctx == null || frame == null) return null;

            SwrContext* swr = ffmpeg.swr_alloc();
            ffmpeg.av_opt_set_int(swr, "in_channel_layout", (long)ctx->ch_layout.u.mask, 0);
            ffmpeg.av_opt_set_int(swr, "out_channel_layout", (long)ffmpeg.AV_CH_LAYOUT_STEREO, 0);
            ffmpeg.av_opt_set_int(swr, "in_sample_rate", ctx->sample_rate, 0);
            ffmpeg.av_opt_set_int(swr, "out_sample_rate", ctx->sample_rate, 0);
            ffmpeg.av_opt_set_sample_fmt(swr, "in_sample_fmt", ctx->sample_fmt, 0);
            ffmpeg.av_opt_set_sample_fmt(swr, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);
            ffmpeg.swr_init(swr);

            int dstNbSamples = (int)ffmpeg.av_rescale_rnd(ffmpeg.swr_get_delay(swr, ctx->sample_rate) + frame->nb_samples,
                                                     ctx->sample_rate, ctx->sample_rate, AVRounding.AV_ROUND_UP);

            int bufferSize = ffmpeg.av_samples_get_buffer_size(null, 2, dstNbSamples, AVSampleFormat.AV_SAMPLE_FMT_S16, 1);
            byte* outBuffer = (byte*)ffmpeg.av_malloc((ulong)bufferSize);

            byte** outPtrs = stackalloc byte*[1];
            outPtrs[0] = outBuffer;

            byte* inPtr = frame->data[0];
            
                byte** inPtrs = stackalloc byte*[1];
                inPtrs[0] = inPtr;

                int converted = ffmpeg.swr_convert(swr, outPtrs, dstNbSamples, inPtrs, frame->nb_samples);
                if (converted <= 0)
                {
                    ffmpeg.swr_free(&swr);
                    ffmpeg.av_free(outBuffer);
                    return null;
                }

                byte[] output = new byte[converted * 2 * ffmpeg.av_get_bytes_per_sample(AVSampleFormat.AV_SAMPLE_FMT_S16)];
                Marshal.Copy((IntPtr)outBuffer, output, 0, output.Length);

                ffmpeg.swr_free(&swr);
                ffmpeg.av_free(outBuffer);
                return output;
            
        }

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

                AVFormatContext* formatContext = ffmpeg.avformat_alloc_context();
                if (ffmpeg.avformat_open_input(&formatContext, url, null, null) != 0)
                {
                    Debug.WriteLine("❌ avformat_open_input FAILED");
                    return;
                }

                if (ffmpeg.avformat_find_stream_info(formatContext, null) < 0)
                {
                    Debug.WriteLine("❌ avformat_find_stream_info FAILED");
                    return;
                }

                int videoStreamIndex = -1;
                int audioStreamIndex = -1;

                AVCodecContext* videoCodecContext = null;
                AVCodecContext* audioCodecContext = null;
                BufferedWaveProvider waveProvider = null;
                WaveOutEvent waveOut = null;

                for (int i = 0; i < formatContext->nb_streams; i++)
                {
                    AVStream* stream = formatContext->streams[i];
                    var codecpar = stream->codecpar;
                    Debug.WriteLine($"🔍 Stream[{i}] Type: {codecpar->codec_type}");

                    if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO && videoStreamIndex == -1)
                    {
                        videoStreamIndex = i;
                        AVCodec* codec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
                        videoCodecContext = ffmpeg.avcodec_alloc_context3(codec);
                        ffmpeg.avcodec_parameters_to_context(videoCodecContext, codecpar);
                        if (ffmpeg.avcodec_open2(videoCodecContext, codec, null) < 0)
                        {
                            Debug.WriteLine("❌ avcodec_open2 for video FAILED");
                            return;
                        }
                        Debug.WriteLine("✅ Video decoder opened");
                    }

                    else if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO && audioStreamIndex == -1)
                    {
                        audioStreamIndex = i;
                        AVCodec* codec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
                        audioCodecContext = ffmpeg.avcodec_alloc_context3(codec);
                        ffmpeg.avcodec_parameters_to_context(audioCodecContext, codecpar);
                        if (ffmpeg.avcodec_open2(audioCodecContext, codec, null) < 0)
                        {
                            Debug.WriteLine("❌ avcodec_open2 for audio FAILED");
                            continue;
                        }

                        var waveFormat = new WaveFormat(audioCodecContext->sample_rate, 2);
                        waveProvider = new BufferedWaveProvider(waveFormat);
                        waveOut = new WaveOutEvent();
                        Debug.WriteLine($"Sample Format: {audioCodecContext->sample_fmt}");
                        waveOut.Init(waveProvider);
                        waveOut.Play();

                        Debug.WriteLine("✅ Audio decoder opened");
                    }
                }

                if (videoStreamIndex == -1)
                {
                    Debug.WriteLine("❌ No video stream found");
                    return;
                }

                AVFrame* frame = ffmpeg.av_frame_alloc();
                AVPacket* packet = ffmpeg.av_packet_alloc();
                AVFrame* audioFrame = ffmpeg.av_frame_alloc();
                SwsContext* swsContext = ffmpeg.sws_getContext(
                    videoCodecContext->width, videoCodecContext->height, videoCodecContext->pix_fmt,
                    videoCodecContext->width, videoCodecContext->height, AVPixelFormat.AV_PIX_FMT_BGR24,
                    ffmpeg.SWS_BICUBIC, null, null, null);

                byte[] buffer = new byte[videoCodecContext->width * videoCodecContext->height * 3];
                fixed (byte* pBuffer = buffer)
                {
                    byte_ptrArray4 dstData = new byte_ptrArray4();
                    int_array4 dstLinesize = new int_array4();
                    dstData[0] = pBuffer;
                    dstLinesize[0] = videoCodecContext->width * 3;

                    byte** outPtrs = stackalloc byte*[1]; byte** inPtrs = stackalloc byte*[1];

                    while (ffmpeg.av_read_frame(formatContext, packet) >= 0)
                    {
                        if (packet->stream_index == videoStreamIndex)
                        {
                            if (ffmpeg.avcodec_send_packet(videoCodecContext, packet) == 0)
                            {
                                while (ffmpeg.avcodec_receive_frame(videoCodecContext, frame) == 0)
                                {

                                    ffmpeg.sws_scale(
                                        swsContext,
                                        frame->data,
                                        frame->linesize,
                                        0,
                                        videoCodecContext->height,
                                        dstData,
                                        dstLinesize);

                                    int w = videoCodecContext->width;
                                    int h = videoCodecContext->height;

                                    dispatcher.Invoke(() =>
                                    {
                                        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr24, null);
                                        bmp.WritePixels(new Int32Rect(0, 0, w, h), buffer, w * 3, 0);
                                        onFrameReady(bmp);
                                    });
                                }
                            }
                        }
                        else if (packet->stream_index == audioStreamIndex && audioCodecContext != null)
                        {
                            if (ffmpeg.avcodec_send_packet(audioCodecContext, packet) == 0)
                            {

                                while (ffmpeg.avcodec_receive_frame(audioCodecContext, audioFrame) == 0)
                                {
                                    SwrContext* swr = ffmpeg.swr_alloc();
                                    ffmpeg.av_opt_set_int(swr, "in_channel_layout", (long)audioCodecContext->ch_layout.u.mask, 0);
                                    ffmpeg.av_opt_set_int(swr, "out_channel_layout", (long)ffmpeg.AV_CH_LAYOUT_STEREO, 0);
                                    ffmpeg.av_opt_set_int(swr, "in_sample_rate", audioCodecContext->sample_rate, 0);
                                    ffmpeg.av_opt_set_int(swr, "out_sample_rate", audioCodecContext->sample_rate, 0);
                                    ffmpeg.av_opt_set_sample_fmt(swr, "in_sample_fmt", audioCodecContext->sample_fmt, 0);
                                    ffmpeg.av_opt_set_sample_fmt(swr, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);
                                    ffmpeg.swr_init(swr);

                                    int dstSamples = ffmpeg.swr_get_out_samples(swr, audioFrame->nb_samples);
                                    int bufferSize = ffmpeg.av_samples_get_buffer_size(null, 2, dstSamples, AVSampleFormat.AV_SAMPLE_FMT_S16, 1);
                                    byte* outBuffer = (byte*)ffmpeg.av_malloc((ulong)bufferSize);

                                    outPtrs[0] = outBuffer;
                                    inPtrs[0] = audioFrame->data[0];

                                    int converted = ffmpeg.swr_convert(swr, outPtrs, dstSamples, inPtrs, audioFrame->nb_samples);
                                    if (converted > 0)
                                    {
                                        byte[] finalAudio = new byte[converted * 2 * ffmpeg.av_get_bytes_per_sample(AVSampleFormat.AV_SAMPLE_FMT_S16)];
                                        Marshal.Copy((IntPtr)outBuffer, finalAudio, 0, finalAudio.Length);
                                        waveProvider?.AddSamples(finalAudio, 0, finalAudio.Length);
                                    }

                                    ffmpeg.swr_free(&swr);
                                    ffmpeg.av_free(outBuffer);
                                }
                                ffmpeg.av_frame_free(&audioFrame);
                            }
                        }


                        ffmpeg.av_packet_unref(packet);
                    }
                }

                // Cleanup
                waveOut?.Stop();
                waveOut?.Dispose();

                ffmpeg.av_frame_free(&frame);
                ffmpeg.av_packet_free(&packet);

                ffmpeg.avcodec_free_context(&videoCodecContext);
                ffmpeg.avcodec_free_context(&audioCodecContext);
                ffmpeg.avformat_close_input(&formatContext);
                ffmpeg.avformat_free_context(formatContext);

            });
        }
    }
}
