using FFmpeg.AutoGen;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MultiStreamApp
{
    public static unsafe class FFmpegStreamDecoder
    {

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        public class NativeWaveOut
        {
            [DllImport("winmm.dll")]
            public static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID, ref WAVEFORMATEX lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint dwFlags);

            [DllImport("winmm.dll")]
            public static extern int waveOutPrepareHeader(IntPtr hWaveOut, ref WAVEHDR lpWaveHdr, uint uSize);

            [DllImport("winmm.dll")]
            public static extern int waveOutWrite(IntPtr hWaveOut, ref WAVEHDR lpWaveHdr, uint uSize);

            [DllImport("winmm.dll")]
            public static extern int waveOutUnprepareHeader(IntPtr hWaveOut, ref WAVEHDR lpWaveHdr, uint uSize);

            [DllImport("winmm.dll")]
            public static extern int waveOutClose(IntPtr hWaveOut);
        }
        private static IntPtr hWaveOut = IntPtr.Zero;

        private static void InitWaveOut(int sampleRate, int channels)
        {
            WAVEFORMATEX format = new WAVEFORMATEX
            {
                wFormatTag = 1, // PCM
                nChannels = (ushort)channels,
                nSamplesPerSec = (uint)sampleRate,
                wBitsPerSample = 16,
                nBlockAlign = (ushort)(channels * 2),
                nAvgBytesPerSec = (uint)(sampleRate * channels * 2),
                cbSize = 0
            };

            NativeWaveOut.waveOutOpen(out hWaveOut, -1, ref format, IntPtr.Zero, IntPtr.Zero, 0);
        }

        private static void PlayPCMBuffer(byte[] buffer)
        {
            GCHandle pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            WAVEHDR header = new WAVEHDR
            {
                lpData = pinnedBuffer.AddrOfPinnedObject(),
                dwBufferLength = (uint)buffer.Length,
                dwFlags = 0,
                dwLoops = 0
            };

            NativeWaveOut.waveOutPrepareHeader(hWaveOut, ref header, (uint)Marshal.SizeOf(typeof(WAVEHDR)));
            NativeWaveOut.waveOutWrite(hWaveOut, ref header, (uint)Marshal.SizeOf(typeof(WAVEHDR)));

            // Optional cleanup delay — real apps would track wave completion
            Task.Delay(500).ContinueWith(_ =>
            {
                NativeWaveOut.waveOutUnprepareHeader(hWaveOut, ref header, (uint)Marshal.SizeOf(typeof(WAVEHDR)));
                pinnedBuffer.Free();
            });
        }



        private static bool ffmpegRegistered = false;
        private static MemoryStream wavData = new MemoryStream();

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
                if (ffmpeg.avformat_open_input(&formatContext, url, null, null) != 0) return;
                if (ffmpeg.avformat_find_stream_info(formatContext, null) < 0) return;

                int videoStreamIndex = -1;
                int audioStreamIndex = -1;
                AVCodecContext* videoCodecContext = null;
                AVCodecContext* audioCodecContext = null;

                for (int i = 0; i < formatContext->nb_streams; i++)
                {
                    var codecpar = formatContext->streams[i]->codecpar;

                    if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO && videoStreamIndex == -1)
                    {
                        videoStreamIndex = i;
                        var codec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
                        videoCodecContext = ffmpeg.avcodec_alloc_context3(codec);
                        ffmpeg.avcodec_parameters_to_context(videoCodecContext, codecpar);
                        ffmpeg.avcodec_open2(videoCodecContext, codec, null);
                    }
                    else if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO && audioStreamIndex == -1)
                    {
                        audioStreamIndex = i;
                        var codec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
                        audioCodecContext = ffmpeg.avcodec_alloc_context3(codec);
                        ffmpeg.avcodec_parameters_to_context(audioCodecContext, codecpar);
                        ffmpeg.avcodec_open2(audioCodecContext, codec, null);
                        InitWaveOut(audioCodecContext->sample_rate, audioCodecContext->ch_layout.nb_channels);
                        //ffmpeg.avcodec_flush_buffers(audioCodecContext);

                    }
                }

                AVPacket* packet = ffmpeg.av_packet_alloc();
                AVFrame* frame = ffmpeg.av_frame_alloc();
                SwsContext* swsContext = null;
                byte[] buffer = null;

                int wavSampleRate = audioCodecContext->sample_rate;
                const int wavChannels = 2;
                wavData = new MemoryStream();

                while (ffmpeg.av_read_frame(formatContext, packet) >= 0)
                {
                    if (packet->stream_index == videoStreamIndex)
                    {
                        if (videoCodecContext == null) continue;

                        if (ffmpeg.avcodec_send_packet(videoCodecContext, packet) == 0)
                        {
                            while (ffmpeg.avcodec_receive_frame(videoCodecContext, frame) == 0)
                            {
                                if (swsContext == null)
                                {
                                    swsContext = ffmpeg.sws_getContext(
                                        videoCodecContext->width, videoCodecContext->height, videoCodecContext->pix_fmt,
                                        videoCodecContext->width, videoCodecContext->height, AVPixelFormat.AV_PIX_FMT_BGR24,
                                        ffmpeg.SWS_BILINEAR, null, null, null);

                                    buffer = new byte[videoCodecContext->width * videoCodecContext->height * 3];
                                }

                                fixed (byte* pBuffer = buffer)
                                {
                                    byte_ptrArray4 dstData = new byte_ptrArray4();
                                    int_array4 dstLinesize = new int_array4();
                                    dstData[0] = pBuffer;
                                    dstLinesize[0] = videoCodecContext->width * 3;

                                    ffmpeg.sws_scale(
                                        swsContext, frame->data, frame->linesize,
                                        0, videoCodecContext->height, dstData, dstLinesize);

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
                    }
                    else if (packet->stream_index == audioStreamIndex)
                    {
                        if (audioCodecContext == null) continue;

                        if (ffmpeg.avcodec_send_packet(audioCodecContext, packet) == 0)
                        {
                            AVFrame* audioFrame = ffmpeg.av_frame_alloc();
                            while (ffmpeg.avcodec_receive_frame(audioCodecContext, audioFrame) == 0)
                            {
                                byte[] pcm = ResampleToStereoS16(audioFrame, audioCodecContext);
                                if (pcm != null) wavData.Write(pcm, 0, pcm.Length);
                                if (pcm != null && pcm.Length > 0)
                                {
                                    PlayPCMBuffer(pcm);
                                }


                            }
                            ffmpeg.av_frame_free(&audioFrame);
                        }
                    }

                    ffmpeg.av_packet_unref(packet);
                }

                SaveWavFile("output.wav", wavData.ToArray(), wavSampleRate, wavChannels);

                // Cleanup
                ffmpeg.av_frame_free(&frame);
                ffmpeg.av_packet_free(&packet);
                ffmpeg.avcodec_free_context(&videoCodecContext);
                ffmpeg.avcodec_free_context(&audioCodecContext);
                ffmpeg.avformat_close_input(&formatContext);
                ffmpeg.avformat_free_context(formatContext);
            });
        }

        private static unsafe byte[] ResampleToStereoS16(AVFrame* frame, AVCodecContext* ctx)
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

            int dstSamples = ffmpeg.swr_get_out_samples(swr, frame->nb_samples);
            int bufferSize = ffmpeg.av_samples_get_buffer_size(null, 2, dstSamples, AVSampleFormat.AV_SAMPLE_FMT_S16, 1);
            byte* outBuffer = (byte*)ffmpeg.av_malloc((ulong)bufferSize);

            byte** outPtrs = stackalloc byte*[1];
            outPtrs[0] = outBuffer;

            byte** inPtrs = stackalloc byte*[1];
            inPtrs[0] = frame->data[0];

            int converted = ffmpeg.swr_convert(swr, outPtrs, dstSamples, inPtrs, frame->nb_samples);
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

        private static void SaveWavFile(string path, byte[] pcmData, int sampleRate, int channels)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);
            int byteRate = sampleRate * channels * 2;
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + pcmData.Length);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)channels);
            bw.Write(sampleRate);
            bw.Write(byteRate);
            bw.Write((short)(channels * 2));
            bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(pcmData.Length);
            bw.Write(pcmData);
        }
    }
}
