// decode.cpp : ���� DLL Ӧ�ó���ĵ���������
//

#include "stdafx.h"
#include "uvc.h"

#include <stdio.h>
#include <process.h>
#include <queue>

#include <ppl.h>

extern "C"
{
#include "libavcodec/avcodec.h"
#include "libavdevice/avdevice.h"
#include "libavformat/avformat.h"
#include "libavutil/imgutils.h"
#include "libavutil/opt.h"
#include "libavutil/pixfmt.h"
#include "libswscale/swscale.h"
#include <ws2tcpip.h>
}

#include <iostream>
#include <fstream>
#include <cassert>
#include <algorithm>
#include <emmintrin.h>  // SSE2 for SIMD vectorization

PlayStateChangeCallbackFunc playStateChangeCallbackFunc = nullptr;

YuvDataCallbackFunc yuvDataCallbackFunc = nullptr;
VideoDataCallbackFunc videoDataCallbackFunc = nullptr;
//RawDataCallbackFunc rawDataCallbackFunc = nullptr;  // 新增：RAW数据回调
const void *user_data_ptr;

Concurrency::reader_writer_lock lockForRecordingFrameQueue;
Concurrency::event pushFrameEvent;
std::queue<AVFrame*> recordingFrameQueue;
bool isRecording = false;
const int MAX_RECORDING_QUEUE_SIZE = 120;  // 最大录制队列长度（约 4 秒@30fps），防止 OOM
int droppedRecordingFrames = 0;            // 因队列满而丢弃的帧计数（用于日志）

// 插值缓冲区池（预分配，避免每帧 new/delete 导致堆碎片和锁竞争）
// 使用 4 缓冲环形队列，避免读写竞态（回调处理慢时新帧覆盖正在读取的缓冲区）
// 使用 16 字节对齐内存，优化 SSE2 SIMD 性能
static uint8_t* interpolationBuffer[4] = { nullptr, nullptr, nullptr, nullptr };
static size_t interpolationBufferSize[4] = { 0, 0, 0, 0 };
static volatile LONG interpolationBufferIndex = 0;  // 原子切换缓冲区//ע��:��д�˱����������

// ������RAW��֡��ȡ������ԭ�Ӳ������̰߳�ȫ��
volatile LONG isCaptureRawFrame = 0; // 0-δ������1-������ȡ
char g_rawSavePath[256] = { 0 };      // RAW�ļ�����·��
Concurrency::reader_writer_lock lockForRawCapture; // ·����д��

struct CodecThreadParam
{
    bool isNeededTranscode = false;
};

AVFormatContext* pOutputFormatCtx;
AVStream* outStream;
AVCodecContext* pOutputCodecCtx;
AVCodec* pOutputCodec;

//�����õı���
AVFormatContext	*pInStreamFormatCtx;
bool isPlaying = false; //ע��:�˱�������Ҫԭ�Ӷ�д
Concurrency::event stopPlayingEvent;
int isMode = 1;
int rawScaleDown = 0;             // 用户配置的 rawScaleDown（通过 SetRawScaleDown 设置）
int activeScaleDown = 0;          // 实际用于解析的 rawScaleDown（与设备同步）
int colScaleDown = 0;             // 用户配置的列降采样（通过 SetRawScaleDown 设置）
int lastRawScalePacked = -1;      // 上次收到的打包值（用于去重）
int activeColScaleDown = 0;       // 实际用于解析的列降采样（与设备同步）
int isFirstFrame = 0;
static char savedDevicePath[512] = { 0 };  // 保存 OpenInput 的 filepath

// 帧完整性跟踪
volatile LONG frameSequenceNumber = 0;    // 帧序号计数器
int lastValidPacketRows = 0;              // 上一帧的有效行数（用于连续性检查）
int lastDetectedPacketSize = 0;           // 上一帧检测到的包大小（用于跳过冗余检测）

// 格式检测防抖机制（防止单帧噪声导致格式误判）
int pendingRowSD = -1;                    // 待确认的行降采样
int pendingColSD = -1;                    // 待确认的列降采样
int pendingPacketSize = 0;                // 待确认的包大小
int pendingDetectionCount = 0;            // 连续检测到相同格式的次数
const int FORMAT_DEBOUNCE_FRAMES = 3;     // 需要连续 N 帧匹配才确认格式变更

int				videoindex;
AVCodecContext	*pInStreamCodecCtx;
AVCodec			*pInStreamCodec;

SwsContext *frameScaleCtx = NULL;

#define UNROUND(x) ((unsigned int)(x + 0.5))
#define CLIP_PIXEL(val, low, high) (((val) < (low)) ? (low) : (((val) >= (high)) ? (high) : (val)))

#define MAX_F_BIT(x)     ((1<<(x)) - 1)
#define HIGH_VAL_12BIT   MAX_F_BIT(12)
#define HIGH_VAL_10BIT   MAX_F_BIT(10)
#define HIGH_VAL_8BIT    MAX_F_BIT(8)

// ==============================================================
// UVC 数据格式判断工具
// ==============================================================
enum UvcDataType {
    UVC_DATA_UNKNOWN = 0,
    UVC_DATA_MJPEG,        // 压缩 JPEG
    UVC_DATA_H264,         // 压缩 H.264
    UVC_DATA_YUYV422,      // 未压缩 YUV 4:2:2
    UVC_DATA_UYVY422,      // 未压缩 YUV 4:2:2 (字节序不同)
    UVC_DATA_YUV420P,      // 未压缩 YUV 4:2:0 平面
    UVC_DATA_NV12,         // 未压缩 YUV 4:2:0 半平面
    UVC_DATA_RGB24,        // 未压缩 RGB
    UVC_DATA_GRAY8         // 未压缩灰度
};

static const char* UvcDataTypeName(UvcDataType type) {
    switch (type) {
        case UVC_DATA_MJPEG:   return "MJPEG (compressed)";
        case UVC_DATA_H264:    return "H.264 (compressed)";
        case UVC_DATA_YUYV422: return "YUYV422 (uncompressed)";
        case UVC_DATA_UYVY422: return "UYVY422 (uncompressed)";
        case UVC_DATA_YUV420P: return "YUV420P (uncompressed)";
        case UVC_DATA_NV12:    return "NV12 (uncompressed)";
        case UVC_DATA_RGB24:   return "RGB24 (uncompressed)";
        case UVC_DATA_GRAY8:   return "GRAY8 (uncompressed)";
        default:               return "UNKNOWN";
    }
}

static UvcDataType DetectUvcDataType(AVCodecID codecId, AVPixelFormat pixFmt,
                                      int packetSize, int width, int height) {
    // Calculate theoretical sizes
    int yuyvSize = width * height * 2;
    int yuv420Size = width * height * 3 / 2;
    int rgb24Size = width * height * 3;

    // DirectShow often reports wrong codecId
    // Priority: pixFmt > packetSize > codecId

    // 1. If pixFmt is explicit YUV/RGB, trust it over codecId
    if (pixFmt == AV_PIX_FMT_YUYV422) return UVC_DATA_YUYV422;
    if (pixFmt == AV_PIX_FMT_UYVY422) return UVC_DATA_UYVY422;
    if (pixFmt == AV_PIX_FMT_YUV420P) return UVC_DATA_YUV420P;
    if (pixFmt == AV_PIX_FMT_NV12 || pixFmt == AV_PIX_FMT_NV21) return UVC_DATA_NV12;
    if (pixFmt == AV_PIX_FMT_RGB24 || pixFmt == AV_PIX_FMT_BGR24) return UVC_DATA_RGB24;
    if (pixFmt == AV_PIX_FMT_GRAY8) return UVC_DATA_GRAY8;

    // 2. If pixFmt invalid, infer from packet size
    if (pixFmt == AV_PIX_FMT_NONE || pixFmt >= AV_PIX_FMT_NB) {
        if (abs(packetSize - yuyvSize) < yuyvSize * 0.1)   return UVC_DATA_YUYV422;
        if (abs(packetSize - yuv420Size) < yuv420Size * 0.1) return UVC_DATA_YUV420P;
        if (abs(packetSize - rgb24Size) < rgb24Size * 0.1)   return UVC_DATA_RGB24;
        return UVC_DATA_UNKNOWN;
    }

    // 3. If codecId says compressed but packet size near uncompressed, driver is lying
    if (codecId == AV_CODEC_ID_MJPEG || codecId == AV_CODEC_ID_H264) {
        if (packetSize >= yuyvSize * 3 / 4) {
            return UVC_DATA_YUYV422;  // Actually raw YUYV
        }
        if (packetSize >= yuv420Size * 3 / 4) {
            return UVC_DATA_YUV420P;  // Actually raw YUV420
        }
        // Really compressed
        if (codecId == AV_CODEC_ID_MJPEG) return UVC_DATA_MJPEG;
        if (codecId == AV_CODEC_ID_H264)  return UVC_DATA_H264;
    }

    // 4. RAWVIDEO codec
    if (codecId == AV_CODEC_ID_RAWVIDEO) {
        switch (pixFmt) {
            case AV_PIX_FMT_YUYV422: return UVC_DATA_YUYV422;
            case AV_PIX_FMT_UYVY422: return UVC_DATA_UYVY422;
            case AV_PIX_FMT_YUV420P: return UVC_DATA_YUV420P;
            case AV_PIX_FMT_NV12:    return UVC_DATA_NV12;
            case AV_PIX_FMT_RGB24:   return UVC_DATA_RGB24;
            case AV_PIX_FMT_GRAY8:   return UVC_DATA_GRAY8;
            default:
                if (abs(packetSize - yuyvSize) < yuyvSize * 0.1) return UVC_DATA_YUYV422;
                return UVC_DATA_UNKNOWN;
        }
    }
    return UVC_DATA_UNKNOWN;
}

void Yuv420toRgb(unsigned int w, unsigned int h, unsigned char **yuv420_img, unsigned char *rgb_img) {
    int r, g, b;
    
    for (unsigned int i = 0; i < h; i++) {
        for (unsigned int j = 0; j < w; j++) {

            r = yuv420_img[0][i*w + j] * 256 + (yuv420_img[1][i / 2 * w / 2 + j / 2] - 128) * 1 + (yuv420_img[2][i / 2 * w / 2 + j / 2] - 128) * 358;
            g = yuv420_img[0][i*w + j] * 256 - (yuv420_img[1][i / 2 * w / 2 + j / 2] - 128) * 88 - (yuv420_img[2][i / 2 * w / 2 + j / 2] - 128) * 183;
            b = yuv420_img[0][i*w + j] * 256 + (yuv420_img[1][i / 2 * w / 2 + j / 2] - 128) * 454 - (yuv420_img[2][i / 2 * w / 2 + j / 2] - 128) * 2;
            rgb_img[(i*w + j) * 3 + 0] = CLIP_PIXEL((r >> 8), 0, HIGH_VAL_8BIT);
            rgb_img[(i*w + j) * 3 + 1] = CLIP_PIXEL((g >> 8), 0, HIGH_VAL_8BIT);
            rgb_img[(i*w + j) * 3 + 2] = CLIP_PIXEL((b >> 8), 0, HIGH_VAL_8BIT);
        }
    }
}

int flush_encoder(AVFormatContext *fmt_ctx, unsigned int stream_index){
    int ret;
    int got_frame;
    AVPacket enc_pkt;
    if (!(fmt_ctx->streams[stream_index]->codec->codec->capabilities &
        CODEC_CAP_DELAY))
        return 0;
    while (1) {
        enc_pkt.data = NULL;
        enc_pkt.size = 0;
        av_init_packet(&enc_pkt);
        ret = avcodec_encode_video2(fmt_ctx->streams[stream_index]->codec, &enc_pkt,
            NULL, &got_frame);
        av_frame_free(NULL);
        if (ret < 0)
            break;
        if (!got_frame){
            ret = 0;
            break;
        }
        printf("Flush Encoder: Succeed to encode 1 frame!\tsize:%5d\n", enc_pkt.size);
        /* mux encoded frame */
        ret = av_interleaved_write_frame(fmt_ctx, &enc_pkt);
        if (ret < 0)
            break;
    }

    return ret;
}

unsigned int __stdcall RecordThread(void *param)
{
    int picture_size = avpicture_get_size(pOutputCodecCtx->pix_fmt, pOutputCodecCtx->width, pOutputCodecCtx->height);
    AVPacket* encodedPacket = av_packet_alloc();
    av_new_packet(encodedPacket, picture_size);

    CodecThreadParam threaParam = *(CodecThreadParam*)param;
    delete param;

    int framecnt = 0; // ¼��֡��
    int ret = 0;

    AVRational inStreamTimeBase = pInStreamFormatCtx->streams[videoindex]->time_base;
    int64_t calc_duration = (double)AV_TIME_BASE / av_q2d(pInStreamFormatCtx->streams[videoindex]->r_frame_rate);

    while (true)
    {
        pushFrameEvent.wait();
        if (!isRecording)
        {
            break;
        }
        lockForRecordingFrameQueue.lock();

        if (recordingFrameQueue.empty())
        {
            break;
        }

        AVFrame* pFrame = recordingFrameQueue.front();
        assert(pFrame->width > 0);
        recordingFrameQueue.pop();
        if (recordingFrameQueue.empty())
        {
            pushFrameEvent.reset();
        }

        lockForRecordingFrameQueue.unlock();

        if (threaParam.isNeededTranscode)
        {
            AVFrame	*pEncodeFrame = av_frame_alloc();
            uint8_t* picture_buf = (uint8_t *)av_malloc(picture_size);
            avpicture_fill((AVPicture *)pEncodeFrame, picture_buf, pOutputCodecCtx->pix_fmt, pOutputCodecCtx->width, pOutputCodecCtx->height);

            ret = sws_scale(frameScaleCtx,
                (const uint8_t * const *)pFrame->data,
                pFrame->linesize,
                0,
                pFrame->height,
                pEncodeFrame->data,
                pEncodeFrame->linesize);

            av_frame_free(&pFrame);
            pFrame = pEncodeFrame;
        }

        int got_picture = 0;
        //Encode
        ret = avcodec_encode_video2(pOutputCodecCtx, encodedPacket, pFrame, &got_picture);
        av_frame_free(&pFrame);
        if (ret < 0){
            printf("Failed to encode! \n");
            return -1;
        }
        if (got_picture == 1){
            printf("Succeed to encode frame: %5d\tsize:%5d\n", framecnt, encodedPacket->size);
            framecnt++;

            if (encodedPacket->pts == AV_NOPTS_VALUE){
                //Write PTS  
                //Duration between 2 frames (us)  
                //Parameters  
                encodedPacket->pts = (double)(framecnt*calc_duration) / (double)(av_q2d(inStreamTimeBase)*AV_TIME_BASE);
                encodedPacket->dts = encodedPacket->pts;
                encodedPacket->duration = (double)calc_duration / (double)(av_q2d(inStreamTimeBase)*AV_TIME_BASE);
            }

            encodedPacket->pts = av_rescale_q_rnd(encodedPacket->pts, inStreamTimeBase,
                outStream->time_base, (AVRounding)(AV_ROUND_NEAR_INF | AV_ROUND_PASS_MINMAX));
            encodedPacket->dts = av_rescale_q_rnd(encodedPacket->dts, inStreamTimeBase,
                outStream->time_base, (AVRounding)(AV_ROUND_NEAR_INF | AV_ROUND_PASS_MINMAX));
            encodedPacket->duration = av_rescale_q(encodedPacket->duration, inStreamTimeBase, outStream->time_base);
            encodedPacket->pos = -1;

            ret = av_interleaved_write_frame(pOutputFormatCtx, encodedPacket);
            av_free_packet(encodedPacket);
        }
    }

    //Flush Encoder
    //ret = flush_encoder(pFormatCtx, 0);
    //if (ret < 0) {
    //    printf("Flushing encoder failed\n");
    //    return -1;
    //}

    //Write file trailer
    av_write_trailer(pOutputFormatCtx);

    //Clean
    if (outStream){
        avcodec_close(outStream->codec);
    }
    avio_close(pOutputFormatCtx->pb);
    avformat_free_context(pOutputFormatCtx);
    pushFrameEvent.set();

    sws_freeContext(frameScaleCtx);

    return 0;
}

unsigned int __stdcall DecodeThread(void* param)
{
    AVFrame* pFrame = av_frame_alloc();
    AVPacket* packet = (AVPacket*)av_malloc(sizeof(AVPacket));
    int ret = 0, got_picture = 0;

    SwsContext* scaleContext = nullptr;
    AVFrame* frameForRecordForTranscode = nullptr;

    // ✅ 安全检查：确保 pInStreamFormatCtx 有效
    if (!pInStreamFormatCtx || videoindex == -1) {
        printf("[DecodeThread] ERROR: Invalid stream context or video index!\n");
        av_frame_free(&pFrame);
        av_packet_free(&packet);
        return -1;
    }

    int picture_size = avpicture_get_size(AV_PIX_FMT_RGB24, pInStreamFormatCtx->streams[videoindex]->codec->width,
        pInStreamFormatCtx->streams[videoindex]->codec->height);
    frameForRecordForTranscode = av_frame_alloc();
    uint8_t* picture_buf = (uint8_t*)av_malloc(picture_size);
    avpicture_fill((AVPicture*)frameForRecordForTranscode, picture_buf, AV_PIX_FMT_RGB24,
        pInStreamFormatCtx->streams[videoindex]->codec->width, pInStreamFormatCtx->streams[videoindex]->codec->height);

    scaleContext = sws_getContext(pInStreamFormatCtx->streams[videoindex]->codec->width,
        pInStreamFormatCtx->streams[videoindex]->codec->height,
        pInStreamFormatCtx->streams[videoindex]->codec->pix_fmt,
        pInStreamFormatCtx->streams[videoindex]->codec->width,
        pInStreamFormatCtx->streams[videoindex]->codec->height, AV_PIX_FMT_RGB24,
        SWS_BICUBIC,
        NULL,
        NULL,
        NULL);

    if (playStateChangeCallbackFunc != nullptr)
    {
        playStateChangeCallbackFunc(true);
    }

    int retryCount = 0;
    int consecutiveDecodeFailures = 0;  // 连续解码失败次数
    const int MAX_DECODE_FAILURES = 10; // 最大连续失败次数阈值


    while (true)
    {
        // ✅ 双重检查：防止在检查 isPlaying 后资源被释放
        if (!isPlaying || !pInStreamFormatCtx)
        {
            break;
        }

        // ✅ 安全检查：确保指针有效再调用
        if (av_read_frame(pInStreamFormatCtx, packet) < 0)
        {
            if (retryCount < 100)
            {
                retryCount++;
                Sleep(20);
                continue;
            }
            else
            {
                break;
            }
        }
        retryCount = 0;
        if (packet->stream_index != videoindex)
            continue;

        // ✅ 修复4: packet->data 空指针检查 (移至此处，避免破坏 if-else 结构)
        if (!packet->data || packet->size <= 0) {
            av_packet_unref(packet);
            continue;
        }

        // ✅ 再次检查：确保在获取 packet 后上下文仍然有效
        if (!pInStreamFormatCtx || videoindex < 0 || videoindex >= pInStreamFormatCtx->nb_streams) {
            av_packet_unref(packet);
            break;
        }

        AVCodecID codecId = pInStreamFormatCtx->streams[videoindex]->codec->codec_id;
        AVPixelFormat pixFmt = pInStreamCodecCtx->pix_fmt;
        int width = pInStreamCodecCtx->width;
        int height = pInStreamCodecCtx->height;

        // Skip decode for uncompressed formats, callback directly
        if (isMode == 0 || isMode == 2)
        {
            isFirstFrame = 1;
            // Uncompressed data: callback directly without decode
            if (videoDataCallbackFunc != nullptr && isPlaying)  // Check isPlaying
            {
                // Calculate data size
                int dataSize = 0;
                
                if (isMode == 0)
                {
                    dataSize = width * height * 2;
                }
                else if (isMode == 2)
                {
                    dataSize = width * height;
                }
                else
                {
                    dataSize = packet->size;
				}

                // Callback raw data - handle rawScaleDown/colScaleDown for Bayer data with 2D interpolation
                if (dataSize > 0) {
                    // 线程安全：快照全局变量
                    int currentMode = isMode;
                    int currentRowSD = rawScaleDown;   // 行降采样
                    int currentColSD = colScaleDown;   // 列降采样

                    // 只要行或列任一需要插值，进入 Bayer 处理路径
                    if ((currentRowSD >= 1 && currentRowSD <= 6) || (currentColSD >= 1 && currentColSD <= 6))
                    {
                        // Bayer 2D interpolation: received data has missing rows AND columns
                        // Bayer format is 2x2 pattern (e.g., RGGB):
                        //   Row 0 (even): R G R G R G...
                        //   Row 1 (odd):  G B G B G B...
                        // Even rows/cols have same color pattern, odd rows/cols have same color pattern.
                        // Interpolation must use rows/cols with same parity (even->even, odd->odd).
                        //
                        // IMPORTANT: packet->data is COMPACT 2D - only contains valid (row, col) intersections.
                        // rawScaleDown=1: keep 2 rows, skip 2 rows (groupRow=4)
                        // colScaleDown=1: keep 2 cols, skip 2 cols (groupCol=4)
                        // Each group has 2x2=4 valid pixels in compact data.

                        // RAW10 (isMode=0): 2 bytes/pixel (16-bit little-endian, low 10 bits valid)
                        // RAW8  (isMode=2): 1 byte/pixel (8-bit grayscale)
                        int bytesPerPixel = (currentMode == 0) ? 2 : 1;
                        int totalRows = height;
                        int totalCols = width;

                        // 行分组模型
                        int keepRows = 2;
                        int skipRows = currentRowSD * 2;
                        int groupSizeRow = keepRows + skipRows;  // sd=0时为2，sd=1时为4...

                        // 列分组模型
                        int keepCols = 2;
                        int skipCols = currentColSD * 2;
                        int groupSizeCol = keepCols + skipCols;  // sd=0时为2，sd=1时为4...

                        // 计算有效行数
                        int totalGroupsRow = (totalRows + groupSizeRow - 1) / groupSizeRow;
                        int totalValidRows = 0;
                        for (int g = 0; g < totalGroupsRow; g++) {
                            int groupStart = g * groupSizeRow;
                            int rowsInGroup = (groupStart + keepRows <= totalRows) ? keepRows : (totalRows - groupStart);
                            if (rowsInGroup > 0) totalValidRows += rowsInGroup;
                        }

                        // 计算有效列数
                        int totalGroupsCol = (totalCols + groupSizeCol - 1) / groupSizeCol;
                        int totalValidCols = 0;
                        for (int g = 0; g < totalGroupsCol; g++) {
                            int groupStart = g * groupSizeCol;
                            int colsInGroup = (groupStart + keepCols <= totalCols) ? keepCols : (totalCols - groupStart);
                            if (colsInGroup > 0) totalValidCols += colsInGroup;
                        }

                        // 预期包大小 = 有效行 × 有效列 × 每像素字节数
                        int compactRowBytes = totalValidCols * bytesPerPixel;  // 压缩行宽度
                        int expectedPacketSize = totalValidRows * compactRowBytes;
                        int rowBytes = totalCols * bytesPerPixel;  // 输出行宽（全宽）
                        int requiredSize = totalRows * rowBytes;
                        
                        // Detect actual device (rowSD, colSD) from packet->size
                        // This keeps activeScaleDown/activeColScaleDown in sync with device's actual output format
                        int actualDataSize = packet->size;
                        int detectedRowSD = -1;
                        int detectedColSD = -1;
                        bool needDetection = (actualDataSize != lastDetectedPacketSize);

                        // Skip detection if packet size unchanged and we have a previous result
                        if (!needDetection && (activeScaleDown >= 0 || activeColScaleDown >= 0)) {
                            // Reuse previous detection result
                            detectedRowSD = activeScaleDown;
                            detectedColSD = activeColScaleDown;
                            
                            // 如果存在待确认的格式变更，说明设备已回退到活跃格式
                            // 清理pending状态，防止计数器残留导致后续误判
                            if (pendingRowSD >= 0) {
                                printf("[UVC] Format change cancelled (reverted to active format, cached path)\n");
                                pendingRowSD = -1;
                                pendingColSD = -1;
                                pendingDetectionCount = 0;
                            }
                        }
                        else
                        {
                            // Helper lambda: calculate valid rows for a given rowSD
                            auto calcValidRows = [&](int rSD) -> int {
                                int testKeepR = 2, testSkipR = rSD * 2, testGroupR = testKeepR + testSkipR;
                                if (testGroupR <= 0) return 0;
                                int testTotalGroupsR = (totalRows + testGroupR - 1) / testGroupR;
                                int testTotalValidRows = 0;
                                for (int g = 0; g < testTotalGroupsR; g++) {
                                    int gs = g * testGroupR;
                                    int rInG = (gs + testKeepR <= totalRows) ? testKeepR : (totalRows - gs);
                                    if (rInG > 0) testTotalValidRows += rInG;
                                }
                                return testTotalValidRows;
                            };

                            // Helper lambda: calculate valid cols for a given colSD
                            auto calcValidCols = [&](int cSD) -> int {
                                int testKeepC = 2, testSkipC = cSD * 2, testGroupC = testKeepC + testSkipC;
                                if (testGroupC <= 0) return 0;
                                int testTotalGroupsC = (totalCols + testGroupC - 1) / testGroupC;
                                int testTotalValidCols = 0;
                                for (int g = 0; g < testTotalGroupsC; g++) {
                                    int gs = g * testGroupC;
                                    int cInG = (gs + testKeepC <= totalCols) ? testKeepC : (totalCols - gs);
                                    if (cInG > 0) testTotalValidCols += cInG;
                                }
                                return testTotalValidCols;
                            };

                            // Helper lambda: try a specific (rSD, cSD) combination
                            auto tryDetect = [&](int rSD, int cSD) -> bool {
                                int validRows = calcValidRows(rSD);
                                int validCols = calcValidCols(cSD);
                                int testPacketSize = validRows * validCols * bytesPerPixel;
                                if (actualDataSize == testPacketSize) {
                                    detectedRowSD = rSD;
                                    detectedColSD = cSD;
                                    return true;
                                }
                                return false;
                            };

                            // Strategy A: First try user-configured combination (most likely correct)
                            if (tryDetect(rawScaleDown, colScaleDown)) {
                                // User config matches
                            }
                            // Strategy B: Then try previous active combination (format usually continuous)
                            else if (activeScaleDown != rawScaleDown || activeColScaleDown != colScaleDown) {
                                tryDetect(activeScaleDown, activeColScaleDown);
                            }

                            // Fallback: exhaustive search for all (rSD, cSD) combinations
                            if (detectedRowSD < 0) {
                                for (int rSD = 0; rSD <= 6; rSD++) {
                                    int testTotalValidRows = calcValidRows(rSD);
                                    if (testTotalValidRows == 0) continue;

                                    for (int cSD = 0; cSD <= 6; cSD++) {
                                        int testTotalValidCols = calcValidCols(cSD);
                                        if (testTotalValidCols == 0) continue;

                                        int testPacketSize = testTotalValidRows * testTotalValidCols * bytesPerPixel;
                                        if (actualDataSize == testPacketSize) {
                                            detectedRowSD = rSD;
                                            detectedColSD = cSD;
                                            goto detection_done;
                                        }
                                    }
                                }
                            }
                            detection_done:

                            // 防抖机制：格式变更需要连续 N 帧匹配才确认，避免单帧噪声导致误判
                            // 但如果用户主动修改了配置（rawScaleDown/colScaleDown），立即接受新格式
                            if (detectedRowSD >= 0) {
                                bool isFormatChange = (detectedRowSD != activeScaleDown || detectedColSD != activeColScaleDown);
                                bool isUserConfigChange = (detectedRowSD == rawScaleDown && detectedColSD == colScaleDown);
                                
                                if (isFormatChange) {
                                    // 检测到格式变更
                                    if (isUserConfigChange) 
                                    {
                                        // 用户主动修改配置，立即接受，无需防抖
                                        lastDetectedPacketSize = actualDataSize;
                                        printf("[UVC] User config change accepted: rowSD=%d, colSD=%d\n",
                                               detectedRowSD, detectedColSD);
                                        // 重置待确认状态
                                        pendingRowSD = -1;
                                        pendingColSD = -1;
                                        pendingDetectionCount = 0;
                                    } 
                                    else 
                                    {
                                        // 设备自动切换格式，进入防抖流程
                                        if (detectedRowSD == pendingRowSD && detectedColSD == pendingColSD) {
                                            // 与待确认格式一致，计数器递增
                                            pendingDetectionCount++;
                                            printf("[UVC] Format change pending: rowSD=%d, colSD=%d (count=%d/%d)\n",
                                                   detectedRowSD, detectedColSD, pendingDetectionCount, FORMAT_DEBOUNCE_FRAMES);
                                        } else {
                                            // 新的待确认格式，重置计数器
                                            pendingRowSD = detectedRowSD;
                                            pendingColSD = detectedColSD;
                                            pendingPacketSize = actualDataSize;
                                            pendingDetectionCount = 1;
                                            printf("[UVC] New format detected: rowSD=%d, colSD=%d (waiting for confirmation)\n",
                                                   detectedRowSD, detectedColSD);
                                        }
                                        
                                        if (pendingDetectionCount >= FORMAT_DEBOUNCE_FRAMES) {
                                            // 防抖通过，确认格式变更
                                            lastDetectedPacketSize = actualDataSize;
                                            printf("[UVC] Format change confirmed: rowSD=%d, colSD=%d\n",
                                                   detectedRowSD, detectedColSD);
                                            // 重置待确认状态
                                            pendingRowSD = -1;
                                            pendingColSD = -1;
                                            pendingDetectionCount = 0;
                                        } 
                                        else 
                                        {
                                            // 防抖未通过：actualDataSize 已是新格式大小，但 expectedPacketSize
                                            // 会按旧格式计算，导致 dataSize < expectedPacketSize 误触发 Fallback，
                                            // 上层收到未经插值的乱码紧凑数据。安全做法是丢弃本帧。
                                            printf("[UVC] Debounce not met, dropping frame (actual=%d, active rowSD=%d colSD=%d)\n",
                                                   actualDataSize, activeScaleDown, activeColScaleDown);
                                            av_packet_unref(packet);
                                            // 让出 CPU 时间片，避免防抖期间空转占用 100% CPU
                                            Sleep(0);
                                            continue;
                                        }
                                    }
                                } 
                                else 
                                {
                                    // 格式未变更，直接缓存
                                    lastDetectedPacketSize = actualDataSize;
                                    // 如果有待确认的格式变更，说明设备又切回来了，取消待确认
                                    if (pendingRowSD >= 0) {
                                        printf("[UVC] Format change cancelled (reverted to active format)\n");
                                        pendingRowSD = -1;
                                        pendingColSD = -1;
                                        pendingDetectionCount = 0;
                                    }
                                }
                            }
                        }

                        // Critical: Use detected parameters for all subsequent calculations
                        // This fixes the mismatch between user config and actual device format
                        if (detectedRowSD >= 0) currentRowSD = detectedRowSD;
                        if (detectedColSD >= 0) currentColSD = detectedColSD;

                        // Recalculate group parameters with detected values
                        skipRows = currentRowSD * 2;
                        groupSizeRow = keepRows + skipRows;
                        skipCols = currentColSD * 2;
                        groupSizeCol = keepCols + skipCols;

                        // Recalculate valid row count
                        totalGroupsRow = (totalRows + groupSizeRow - 1) / groupSizeRow;
                        totalValidRows = 0;
                        for (int g = 0; g < totalGroupsRow; g++) {
                            int groupStart = g * groupSizeRow;
                            int rowsInGroup = (groupStart + keepRows <= totalRows) ? keepRows : (totalRows - groupStart);
                            if (rowsInGroup > 0) totalValidRows += rowsInGroup;
                        }

                        // Recalculate valid col count
                        totalGroupsCol = (totalCols + groupSizeCol - 1) / groupSizeCol;
                        totalValidCols = 0;
                        for (int g = 0; g < totalGroupsCol; g++) 
                        {
                            int groupStart = g * groupSizeCol;
                            int colsInGroup = (groupStart + keepCols <= totalCols) ? keepCols : (totalCols - groupStart);
                            if (colsInGroup > 0) totalValidCols += colsInGroup;
                        }

                        // Recalculate expected packet size
                        compactRowBytes = totalValidCols * bytesPerPixel;
                        expectedPacketSize = totalValidRows * compactRowBytes;

                        // Update activeScaleDown/activeColScaleDown when device format changes
                        if (detectedRowSD >= 0 && detectedRowSD != activeScaleDown) {
                            printf("[UVC] Device format detected: rowSD=%d, colSD=%d (packet=%d bytes)\n",
                                   detectedRowSD, detectedColSD, actualDataSize);
                            activeScaleDown = detectedRowSD;
                        }
                        if (detectedColSD >= 0 && detectedColSD != activeColScaleDown) {
                            activeColScaleDown = detectedColSD;
                        }
                        
                        dataSize = actualDataSize;
                        
                        // 边界检查：基于分辨率而非硬编码大小
                        if (width <= 0 || height <= 0 || width > 16384 || height > 16384) {
                            printf("[UVC] Invalid dimensions: %dx%d - dropping frame\n", width, height);
                            // 无效尺寸时丢弃帧，不回调上层以避免 AccessViolationException
                            av_packet_unref(packet);
                            continue;
                        }
                        else
                        {
                            // 验证 1: 数据大小下限检查
                            if (dataSize < expectedPacketSize) {
                                printf("[UVC] Packet too small: expected %d, got %d (frame #%ld) - dropping frame\n", 
                                       expectedPacketSize, dataSize, frameSequenceNumber + 1);
                                // 丢弃不完整帧，避免上层越界访问导致 AccessViolationException 和 UI 卡死
                                // 不回调上层，因为数据量严重不足（如 2953 vs 2073600）会导致缓冲区越界
                                av_packet_unref(packet);
                                continue;
                            }
                            // 验证 2: 数据大小上限检查（允许少量余量，如 UVC 头部）
                            else if (dataSize > expectedPacketSize + rowBytes * 4) {
                                printf("[UVC] Packet too large: expected %d, got %d (frame #%ld)\n", 
                                       expectedPacketSize, dataSize, frameSequenceNumber + 1);
                                // 截断到预期大小，使用有效数据部分
                                dataSize = expectedPacketSize;
                            }
                            else
                            {
                                // Acquire interpolation buffer from pre-allocated pool (quad buffering)
                                // This avoids per-frame new/delete which causes heap fragmentation and lock contention
                                uint8_t* interpolatedData = nullptr;
                                bool allocFailed = false;
                                {
                                    //int bufIdx = (int)InterlockedIncrement(&interpolationBufferIndex) % 4;
                                    uint32_t currentIdx = (uint32_t)(InterlockedIncrement(&interpolationBufferIndex) & 0x7FFFFFFF);
                                    int bufIdx = (int)(currentIdx & 3);
                                    
                                    // Expand buffer if needed (rare, only when resolution increases)
                                    if (interpolationBufferSize[bufIdx] < (size_t)requiredSize) {
                                        if (interpolationBuffer[bufIdx]) _aligned_free(interpolationBuffer[bufIdx]);
                                        // 使用 16 字节对齐内存，优化 SSE2 SIMD 性能
                                        //interpolationBuffer[bufIdx] = (uint8_t*)_aligned_malloc(requiredSize, 16);
                                        size_t alignedSize = (requiredSize + 15) & ~15;
                                        interpolationBuffer[bufIdx] = (uint8_t*)_aligned_malloc(alignedSize, 16);

                                        interpolationBufferSize[bufIdx] = alignedSize; // ✅ 记录实际分配大小;

                                        // ✅ P2 修复：扩容或新分配时清零，防止首帧或边界读取到未初始化数据
                                        memset(interpolationBuffer[bufIdx], 0, alignedSize);
                                    }
                                    
                                    interpolatedData = interpolationBuffer[bufIdx];
                                    // Only zero-fill the first time or when buffer was reallocated
                                    // Subsequent frames will overwrite all data in Phase 1, so memset is unnecessary
                                }
                                
                                {
                                    // Phase 1: Place valid data at correct (row, col) positions
                                    // Packet data is compact 2D: each compact row has totalValidCols pixels
                                    // Optimization: keepCols/keepRows pixels are contiguous in both source and destination,
                                    // so we use memcpy instead of per-pixel assignment to reduce loop iterations.
                                    int pixelBytes = bytesPerPixel;
                                    int packetRowIdx = 0;
                                    for (int groupR = 0; groupR < totalRows; groupR += groupSizeRow)
                                    {
                                        int rowsInThisGroup = (groupR + keepRows <= totalRows) ? keepRows : (totalRows - groupR);

                                        for (int kr = 0; kr < rowsInThisGroup; kr++) {
                                            int outputRow = groupR + kr;
                                            if (outputRow >= totalRows || packetRowIdx >= totalValidRows) break;

                                            const uint8_t* compactRow = packet->data + packetRowIdx * compactRowBytes;
                                            uint8_t* outputRowPtr = interpolatedData + outputRow * rowBytes;

                                            // Place valid column groups at correct positions using memcpy
                                            int packetColIdx = 0;
                                            for (int groupC = 0; groupC < totalCols; groupC += groupSizeCol) {
                                                int colsInThisGroup = (groupC + keepCols <= totalCols) ? keepCols : (totalCols - groupC);
                                                if (packetColIdx + colsInThisGroup > totalValidCols) {
                                                    colsInThisGroup = totalValidCols - packetColIdx;
                                                    if (colsInThisGroup <= 0) break;
                                                }

                                                // Source and destination are both contiguous for keepCols pixels
                                                memcpy(outputRowPtr + groupC * pixelBytes,
                                                    compactRow + packetColIdx * pixelBytes,
                                                    colsInThisGroup * pixelBytes);
                                                packetColIdx += colsInThisGroup;
                                            }
                                            packetRowIdx++;
                                        }
                                    }

                                    // 验证 3: packetRowIdx 必须等于 totalValidRows（Phase 1 已完成）
                                    // 提前验证，避免不完整帧执行 Phase 2/3 浪费 CPU
                                    if (packetRowIdx != totalValidRows) {
                                        printf("[UVC] Incomplete frame: consumed %d/%d rows (frame #%ld) - dropping\n",
                                            packetRowIdx, totalValidRows, frameSequenceNumber + 1);
                                        // 严格丢弃不完整帧，避免画面撕裂
                                        // 不执行 Phase 2/3，直接跳过回调
                                    }
                                    else
                                    {
                                        // Phase 2: Column interpolation (within valid rows) - 定点数优化版
                                        // 问题：目标内存不连续（间隔keepCols列），无法直接向量化
                                        // 优化：使用定点数替代浮点数，减少浮点运算开销
                                        // 当colScaleDown >= 3时，每个列组需要插值6列，成为CPU热点
                                        
                                        for (int groupR = 0; groupR < totalRows; groupR += groupSizeRow)
                                        {
                                            int rowsInThisGroup = (groupR + keepRows <= totalRows) ? keepRows : (totalRows - groupR);

                                            for (int kr = 0; kr < rowsInThisGroup; kr++) {
                                                int outputRow = groupR + kr;
                                                if (outputRow >= totalRows) break;

                                                uint8_t* rowPtr = interpolatedData + outputRow * rowBytes;

                                                // Interpolate missing columns within this row
                                                for (int groupC = 0; groupC < totalCols; groupC += groupSizeCol)
                                                {
                                                    int missingStart = groupC + keepCols;
                                                    int missingEnd = min(groupC + groupSizeCol, totalCols);

                                                    if (missingStart >= missingEnd) continue;

                                                    // Pre-calculate reference columns for this group
                                                    int nextGroupC = groupC + groupSizeCol;
                                                    bool hasNextGroupC = (nextGroupC < totalCols);

                                                    int curEvenCol = groupC;
                                                    int curOddCol = min(groupC + 1, totalCols - 1);
                                                    int nextEvenCol = hasNextGroupC ? nextGroupC : curEvenCol;
                                                    int nextOddCol = hasNextGroupC ? min(nextGroupC + 1, totalCols - 1) : curOddCol;

                                                    if (nextEvenCol >= totalCols) nextEvenCol = totalCols - 1;
                                                    if (nextOddCol >= totalCols) nextOddCol = totalCols - 1;

                                                    int evenDist = nextEvenCol - curEvenCol;
                                                    int oddDist = nextOddCol - curOddCol;

                                                    // === 偶数列定点数增量法 ===
                                                    if (evenDist > 0) {
                                                        int refCol1 = curEvenCol;
                                                        int refCol2 = nextEvenCol;
                                                        int firstEven = (missingStart % 2 == 0) ? missingStart : missingStart + 1;

                                                        if (firstEven < missingEnd) {
                                                            // 使用定点数（16.16格式）替代浮点数
                                                            int v1_fixed, v2_fixed, step_fixed, val_fixed;
                                                            if (bytesPerPixel == 2) {
                                                                v1_fixed = *(uint16_t*)(rowPtr + refCol1 * 2) << 16;
                                                                v2_fixed = *(uint16_t*)(rowPtr + refCol2 * 2) << 16;
                                                            }
                                                            else {
                                                                v1_fixed = rowPtr[refCol1] << 16;
                                                                v2_fixed = rowPtr[refCol2] << 16;
                                                            }

                                                            int diff_fixed = v2_fixed - v1_fixed;
                                                            step_fixed = (diff_fixed * 2) / evenDist;
                                                            int d0 = firstEven - refCol1;
                                                            val_fixed = v1_fixed + (diff_fixed * d0) / evenDist;

                                                            if (bytesPerPixel == 2) {
                                                                for (int targetCol = firstEven; targetCol < missingEnd; targetCol += 2) {
                                                                    *(uint16_t*)(rowPtr + targetCol * 2) = (uint16_t)((val_fixed + 0x8000) >> 16);
                                                                    val_fixed += step_fixed;
                                                                }
                                                            }
                                                            else {
                                                                for (int targetCol = firstEven; targetCol < missingEnd; targetCol += 2) {
                                                                    rowPtr[targetCol] = (uint8_t)((val_fixed + 0x8000) >> 16);
                                                                    val_fixed += step_fixed;
                                                                }
                                                            }
                                                        }
                                                    }
                                                    else if (hasNextGroupC == false)
                                                    {
                                                        // Fallback: dist == 0，复制 refCol1
                                                        int firstEven = (missingStart % 2 == 0) ? missingStart : missingStart + 1;
                                                        if (bytesPerPixel == 2) {
                                                            uint16_t fallbackVal = *(uint16_t*)(rowPtr + curEvenCol * 2);
                                                            for (int targetCol = firstEven; targetCol < missingEnd; targetCol += 2) {
                                                                *(uint16_t*)(rowPtr + targetCol * 2) = fallbackVal;
                                                            }
                                                        }
                                                        else {
                                                            uint8_t fallbackVal = rowPtr[curEvenCol];
                                                            for (int targetCol = firstEven; targetCol < missingEnd; targetCol += 2) {
                                                                rowPtr[targetCol] = fallbackVal;
                                                            }
                                                        }
                                                    }

                                                    // === 奇数列定点数增量法 ===
                                                    if (oddDist > 0) {
                                                        int refCol1 = curOddCol;
                                                        int refCol2 = nextOddCol;
                                                        int firstOdd = (missingStart % 2 == 1) ? missingStart : missingStart + 1;

                                                        if (firstOdd < missingEnd) {
                                                            // 使用定点数（16.16格式）替代浮点数
                                                            int v1_fixed, v2_fixed, step_fixed, val_fixed;
                                                            if (bytesPerPixel == 2) {
                                                                v1_fixed = *(uint16_t*)(rowPtr + refCol1 * 2) << 16;
                                                                v2_fixed = *(uint16_t*)(rowPtr + refCol2 * 2) << 16;
                                                            }
                                                            else {
                                                                v1_fixed = rowPtr[refCol1] << 16;
                                                                v2_fixed = rowPtr[refCol2] << 16;
                                                            }

                                                            int diff_fixed = v2_fixed - v1_fixed;
                                                            step_fixed = (diff_fixed * 2) / oddDist;
                                                            int d0 = firstOdd - refCol1;
                                                            val_fixed = v1_fixed + (diff_fixed * d0) / oddDist;

                                                            if (bytesPerPixel == 2) {
                                                                for (int targetCol = firstOdd; targetCol < missingEnd; targetCol += 2) {
                                                                    *(uint16_t*)(rowPtr + targetCol * 2) = (uint16_t)((val_fixed + 0x8000) >> 16);
                                                                    val_fixed += step_fixed;
                                                                }
                                                            }
                                                            else {
                                                                for (int targetCol = firstOdd; targetCol < missingEnd; targetCol += 2) {
                                                                    rowPtr[targetCol] = (uint8_t)((val_fixed + 0x8000) >> 16);
                                                                    val_fixed += step_fixed;
                                                                }
                                                            }
                                                        }
                                                    }
                                                    else if (hasNextGroupC == false) {
                                                        // Fallback: dist == 0，复制 refCol1
                                                        int firstOdd = (missingStart % 2 == 1) ? missingStart : missingStart + 1;
                                                        if (bytesPerPixel == 2) {
                                                            uint16_t fallbackVal = *(uint16_t*)(rowPtr + curOddCol * 2);
                                                            for (int targetCol = firstOdd; targetCol < missingEnd; targetCol += 2) {
                                                                *(uint16_t*)(rowPtr + targetCol * 2) = fallbackVal;
                                                            }
                                                        }
                                                        else {
                                                            uint8_t fallbackVal = rowPtr[curOddCol];
                                                            for (int targetCol = firstOdd; targetCol < missingEnd; targetCol += 2) {
                                                                rowPtr[targetCol] = fallbackVal;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        // Phase 3: Row interpolation (now all rows have full width)
                                        // Same logic as original row-only interpolation
                                        // Note: packetRowIdx validation is done in Phase 1, no need to recount here
                                        for (int groupR = 0; groupR < totalRows; groupR += groupSizeRow)
                                        {
                                            int rowsInThisGroup = (groupR + keepRows <= totalRows) ? keepRows : (totalRows - groupR);

                                            // Interpolate missing rows using distance-weighted linear interpolation
                                            int missingStart = groupR + keepRows;
                                            int missingEnd = min(groupR + groupSizeRow, totalRows);

                                            // Pre-calculate reference rows for this group
                                            int nextGroupR = groupR + groupSizeRow;
                                            bool hasNextGroupR = (nextGroupR < totalRows);

                                            int curEvenRow = groupR;
                                            int curOddRow = min(groupR + 1, totalRows - 1);

                                            int nextEvenRow, nextOddRow;
                                            if (hasNextGroupR) {
                                                // 有下一组，使用下一组的行作为参考
                                                nextEvenRow = nextGroupR;
                                                nextOddRow = min(nextGroupR + 1, totalRows - 1);
                                            }
                                            else {
                                                // 没有下一组，使用镜像插值：以当前组最后一行作为参考
                                                // 这样缺失行会基于最后一行进行插值，而非简单复制
                                                int lastRow = min(groupR + keepRows - 1, totalRows - 1);
                                                nextEvenRow = lastRow;
                                                nextOddRow = max(lastRow - 1, 0);
                                            }

                                            if (nextEvenRow >= totalRows) nextEvenRow = totalRows - 1;
                                            if (nextOddRow >= totalRows) nextOddRow = totalRows - 1;

                                            int evenDist = nextEvenRow - curEvenRow;
                                            int oddDist = nextOddRow - curOddRow;

                                            for (int targetRow = missingStart; targetRow < missingEnd; targetRow++)
                                            {
                                                int refRow1, refRow2, dist;

                                                if ((targetRow % 2) == 0) {
                                                    refRow1 = curEvenRow;
                                                    refRow2 = nextEvenRow;
                                                    dist = evenDist;
                                                }
                                                else {
                                                    refRow1 = curOddRow;
                                                    refRow2 = nextOddRow;
                                                    dist = oddDist;
                                                }

                                                int d = targetRow - refRow1;
                                                int w1 = dist - d;
                                                int w2 = d;

                                                uint8_t* src1 = interpolatedData + refRow1 * rowBytes;
                                                uint8_t* src2 = interpolatedData + refRow2 * rowBytes;
                                                uint8_t* dst = interpolatedData + targetRow * rowBytes;

                                                if (dist > 0 && w1 > 0) {
                                                    // 预计算比例（此行所有像素共用）
                                                    // 使用浮点以便 SSE2 向量化（SSE2 无整数除法指令）
                                                    float ratio = (float)w2 / (float)dist;

                                                    if (bytesPerPixel == 2) {
                                                        // RAW10: SSE2浮点SIMD - 一次处理4个像素
                                                        __m128 vf_ratio = _mm_set1_ps(ratio);
                                                        int p = 0;
                                                        for (; p + 3 < totalCols; p += 4) {
                                                            // 加载 4 x uint16 (8字节)
                                                            __m128i w1_vec = _mm_loadl_epi64((const __m128i*)(src1 + p * 2));
                                                            __m128i w2_vec = _mm_loadl_epi64((const __m128i*)(src2 + p * 2));
                                                            // 扩展到 4 x int32
                                                            __m128i i1 = _mm_unpacklo_epi16(w1_vec, _mm_setzero_si128());
                                                            __m128i i2 = _mm_unpacklo_epi16(w2_vec, _mm_setzero_si128());
                                                            // 转换为浮点
                                                            __m128 f1 = _mm_cvtepi32_ps(i1);
                                                            __m128 f2 = _mm_cvtepi32_ps(i2);
                                                            // result = f1 + (f2 - f1) * ratio
                                                            __m128 fdiff = _mm_sub_ps(f2, f1);
                                                            __m128 fdelta = _mm_mul_ps(fdiff, vf_ratio);
                                                            __m128 fresult = _mm_add_ps(f1, fdelta);
                                                            // 转回 int32（自动四舍五入）
                                                            __m128i iresult = _mm_cvtps_epi32(fresult);
                                                            // 压缩到 4 x uint16（饱和）
                                                            __m128i packed = _mm_packs_epi32(iresult, _mm_setzero_si128());
                                                            _mm_storel_epi64((__m128i*)(dst + p * 2), packed);
                                                        }
                                                        // 标量回退处理剩余像素
                                                        for (; p < totalCols; p++) {
                                                            uint16_t v1 = *(uint16_t*)(src1 + p * 2);
                                                            uint16_t v2 = *(uint16_t*)(src2 + p * 2);
                                                            int diff = (int)v2 - (int)v1;
                                                            *(uint16_t*)(dst + p * 2) = (uint16_t)(v1 + (diff * w2 + dist / 2) / dist);
                                                        }
                                                    }
                                                    else {
                                                        // RAW8: SSE2浮点SIMD - 一次处理4个像素
                                                        __m128 vf_ratio = _mm_set1_ps(ratio);
                                                        int p = 0;
                                                        for (; p + 3 < totalCols; p += 4) {
                                                            // 修复：使用 _mm_cvtsi32_si128 加载 4 字节，避免越界读取
                                                            __m128i b1 = _mm_cvtsi32_si128(*(uint32_t*)(src1 + p));
                                                            __m128i b2 = _mm_cvtsi32_si128(*(uint32_t*)(src2 + p));
                                                            // 字节 -> 16位字（8个）
                                                            __m128i w1 = _mm_unpacklo_epi8(b1, _mm_setzero_si128());
                                                            __m128i w2 = _mm_unpacklo_epi8(b2, _mm_setzero_si128());
                                                            // 前4个 16位 -> 32位整数
                                                            __m128i i1 = _mm_unpacklo_epi16(w1, _mm_setzero_si128());
                                                            __m128i i2 = _mm_unpacklo_epi16(w2, _mm_setzero_si128());
                                                            // 转换为浮点
                                                            __m128 f1 = _mm_cvtepi32_ps(i1);
                                                            __m128 f2 = _mm_cvtepi32_ps(i2);
                                                            // result = f1 + (f2 - f1) * ratio
                                                            __m128 fdiff = _mm_sub_ps(f2, f1);
                                                            __m128 fdelta = _mm_mul_ps(fdiff, vf_ratio);
                                                            __m128 fresult = _mm_add_ps(f1, fdelta);
                                                            // 转回 int32
                                                            __m128i iresult = _mm_cvtps_epi32(fresult);
                                                            // int32 -> int16 -> uint8（饱和压缩）
                                                            __m128i packed16 = _mm_packs_epi32(iresult, _mm_setzero_si128());
                                                            __m128i packed8 = _mm_packus_epi16(packed16, _mm_setzero_si128());
                                                            // 存储低 4 字节
                                                            *(uint32_t*)(dst + p) = _mm_cvtsi128_si32(packed8);
                                                        }
                                                        // 标量回退处理剩余像素
                                                        for (; p < totalCols; p++) {
                                                            uint8_t v1 = src1[p];
                                                            uint8_t v2 = src2[p];
                                                            int diff = v2 - v1;
                                                            dst[p] = (uint8_t)(v1 + (diff * w2 + dist / 2) / dist);
                                                        }
                                                    }
                                                }
                                                else {
                                                    // Fallback: simple copy
                                                    memcpy(dst, src1, rowBytes);
                                                }
                                            }
                                        }

                                        // 验证 4: 帧连续性检查（与上一帧比较）
                                        int currentSeq = InterlockedIncrement(&frameSequenceNumber);
                                        if (lastValidPacketRows > 0 && packetRowIdx != lastValidPacketRows) {
                                            printf("[UVC] Frame continuity warning: rows changed from %d to %d (frame #%d)\n",
                                                lastValidPacketRows, packetRowIdx, currentSeq);
                                        }
                                        lastValidPacketRows = packetRowIdx;

                                        // 帧完整性验证通过，回调插值后的数据
                                        videoDataCallbackFunc(
                                            (void*)interpolatedData,
                                            requiredSize,
                                            pixFmt,
                                            user_data_ptr);
                                    } // end of else (packetRowIdx == totalValidRows)

                                    // Do NOT free buffer (it's from pool, will be reused)
                                }
                            }
                        }
                    }
                    else
                    {
                        // rawScaleDown == 0 && colScaleDown == 0: callback all data
                        dataSize = min(dataSize, packet->size); // 安全检查：确保不超过实际数据大小
                        InterlockedIncrement(&frameSequenceNumber);
                        videoDataCallbackFunc(
                            (void*)packet->data,
                            dataSize,
                            pixFmt,
                            user_data_ptr);
                    }
                }
            }
            // 【修复点】：在此处添加 continue，防止裸数据进入下面的 avcodec_decode_video2
            av_packet_unref(packet);
            continue;
        }
        else if(isMode == 1)
        {
            // 检查是否需要重新配置解码器
            if (consecutiveDecodeFailures >= MAX_DECODE_FAILURES) 
            {
                printf("[DecodeThread] Too many consecutive decode failures, attempting reconfiguration...\n");
                // 尝试重新配置像素格式
                if (codecId == AV_CODEC_ID_MJPEG) {
                    pInStreamCodecCtx->pix_fmt = AV_PIX_FMT_YUVJ422P;
                    pInStreamCodecCtx->color_range = AVCOL_RANGE_JPEG;
                }
                else {
                    pInStreamCodecCtx->pix_fmt = AV_PIX_FMT_YUYV422;
                }

                // 重新创建缩放上下文
                if (scaleContext) 
                {
                    sws_freeContext(scaleContext);
                }
                scaleContext = sws_getContext(pInStreamFormatCtx->streams[videoindex]->codec->width,
                    pInStreamFormatCtx->streams[videoindex]->codec->height,
                    pInStreamFormatCtx->streams[videoindex]->codec->pix_fmt,
                    pInStreamFormatCtx->streams[videoindex]->codec->width,
                    pInStreamFormatCtx->streams[videoindex]->codec->height, AV_PIX_FMT_RGB24,
                    SWS_BICUBIC,
                    NULL,
                    NULL,
                    NULL);

                consecutiveDecodeFailures = 0;  // 重置计数器
                av_packet_unref(packet);
                continue;
            }
        }

        ret = avcodec_decode_video2(pInStreamCodecCtx, pFrame, &got_picture, packet);
        if (ret < 0) {
            // 输出更详细的错误信息，帮助调试
            char errbuf[AV_ERROR_MAX_STRING_SIZE];
            av_strerror(ret, errbuf, AV_ERROR_MAX_STRING_SIZE);
            printf("[DecodeThread] avcodec_decode_video2 failed: %s (ret=%d)\n", errbuf, ret);

            if (isMode == 1)
                consecutiveDecodeFailures++;  // 增加失败计数

            // 跳过这个包，继续处理下一个
            av_packet_unref(packet);
            continue;
        }
        else 
        {
            consecutiveDecodeFailures = 0;  // 成功时重置计数器
        }

        if (got_picture) {
            if (isRecording)
        {
            AVFrame* frameForRecord = av_frame_clone(pFrame);
            assert(pFrame->width > 0);

            lockForRecordingFrameQueue.lock();
            
            // Bounded queue: drop oldest frames if encoder is too slow
            if ((int)recordingFrameQueue.size() >= MAX_RECORDING_QUEUE_SIZE) {
                // Drop the oldest frame to make room
                AVFrame* oldFrame = recordingFrameQueue.front();
                recordingFrameQueue.pop();
                av_frame_free(&oldFrame);
                droppedRecordingFrames++;
                
                if (droppedRecordingFrames == 1 || droppedRecordingFrames % 30 == 0) {
                    printf("[UVC] Recording queue full, dropped frame (queue=%d, total_dropped=%d)\n",
                           (int)recordingFrameQueue.size(), droppedRecordingFrames);
                }
            }
            
            pushFrameEvent.set();
            recordingFrameQueue.push(frameForRecord);

            lockForRecordingFrameQueue.unlock();
        }
            
            if (videoDataCallbackFunc != nullptr && isPlaying)  // Check isPlaying 避免断开后继续回调
            {
                // Yuv回调暂时不使用，注释掉
                //yuvDataCallbackFunc((void**)pFrame->data);

                // ============================================================
                // Compressed formats (MJPEG/H264): decode and convert to RGB24
                int ret = sws_scale(scaleContext,
                    (const uint8_t* const*)pFrame->data,
                    pFrame->linesize,
                    0,
                    pFrame->height,
                    frameForRecordForTranscode->data,
                    frameForRecordForTranscode->linesize);

                // Use actual allocated buffer size (picture_size), NOT linesize[0] * height
                // avpicture_fill may set linesize[0] > width*3 due to alignment padding,
                // causing linesize[0] * height > picture_size → out-of-bounds read in callback.
                videoDataCallbackFunc(
                    (void*)frameForRecordForTranscode->data[0],
                    picture_size,  // Safe: exactly the allocated buffer size
                    pInStreamCodecCtx->pix_fmt,
                    user_data_ptr);
            }

        }
        av_packet_unref(packet);
    }


    if (scaleContext) {
        sws_freeContext(scaleContext);
        scaleContext = nullptr;
    }

    // ✅ 只释放 DecodeThread 本地资源
    // 全局资源（pInStreamFormatCtx 等）由 CloseInput 统一释放
    av_frame_free(&frameForRecordForTranscode);
    av_frame_free(&pFrame);
    av_packet_free(&packet);

    stopPlayingEvent.set();

    // ✅ 通知 CloseInput 资源已释放
    if (playStateChangeCallbackFunc != nullptr)
    {
        playStateChangeCallbackFunc(false);
    }
    
    printf("[DecodeThread] Thread exited cleanly.\n");
    return 0;
}

UVC_API void StopRecord()
{
    InterlockedExchange8((char*)&isRecording, 0);
    pushFrameEvent.set();
    
    // Log dropped frames summary and reset counter
    if (droppedRecordingFrames > 0) {
        printf("[UVC] Recording stopped. Total dropped frames due to queue overflow: %d\n", droppedRecordingFrames);
        droppedRecordingFrames = 0;
    }
}

UVC_API int StartRecord(const char* out_file)
{
    av_register_all();
    pOutputFormatCtx = avformat_alloc_context();
    avformat_alloc_output_context2(&pOutputFormatCtx, NULL, NULL, out_file);

    //Open output URL
    if (avio_open(&pOutputFormatCtx->pb, out_file, AVIO_FLAG_READ_WRITE) < 0){
        printf("Failed to open output file! \n");
        return -1;
    }

    pOutputCodec = avcodec_find_encoder(AV_CODEC_ID_H264);
    if (!pOutputCodec){
        printf("Can not find encoder! \n");
        return -1;
    }

    outStream = avformat_new_stream(pOutputFormatCtx, pOutputCodec);
    //video_st->time_base.num = 1; 
    //video_st->time_base.den = 25;  

    if (outStream == NULL){
        return -1;
    }
    //Param that must set
    pOutputCodecCtx = outStream->codec;
    //pCodecCtx->codec_id =AV_CODEC_ID_HEVC;
    pOutputCodecCtx->codec_id = pOutputFormatCtx->oformat->video_codec;
    pOutputCodecCtx->codec_type = AVMEDIA_TYPE_VIDEO;
    pOutputCodecCtx->pix_fmt = AV_PIX_FMT_YUV420P;
    pOutputCodecCtx->width = pInStreamCodecCtx->width;
    pOutputCodecCtx->height = pInStreamCodecCtx->height;

    AVStream* inStream = pInStreamFormatCtx->streams[videoindex];

    if (pInStreamCodecCtx->framerate.num > 0 && pInStreamCodecCtx->framerate.den > 0) {
        pOutputCodecCtx->time_base = av_inv_q(pInStreamCodecCtx->framerate);
    }
    // based on AVStream.avg_frame_rate
    else if (inStream->avg_frame_rate.num > 0 && inStream->avg_frame_rate.den > 0) {
        pOutputCodecCtx->time_base = av_inv_q(inStream->avg_frame_rate);
    }
    // based on AVStream.r_frame_rate
    else if (inStream->r_frame_rate.num > 0 && inStream->r_frame_rate.den > 0) {
        pOutputCodecCtx->time_base = av_inv_q(inStream->r_frame_rate);
    }
    // or fixed 20fps
    else {
        pOutputCodecCtx->time_base = { 1, 25 };
    }


    //pOutputCodecCtx->time_base = av_inv_q(pInStreamCodecCtx->framerate);

    //pOutputCodecCtx->time_base.num = 1;
    //pOutputCodecCtx->time_base.den = 25;


    //pOutputCodecCtx->bit_rate = 400000;
    //pOutputCodecCtx->gop_size = 8;

    //H264
    //pCodecCtx->me_range = 16;
    //pCodecCtx->max_qdiff = 4;
    //pCodecCtx->qcompress = 0.6;
    //pOutputCodecCtx->qmin = 10;
    //pOutputCodecCtx->qmax = 51;

    //Optional Param
    //pOutputCodecCtx->max_b_frames = 3;


    //H.264
    if (pOutputCodecCtx->codec_id == AV_CODEC_ID_H264)
    {
        av_opt_set(pOutputCodecCtx->priv_data, "preset", "slow", 0);
        av_opt_set(pOutputCodecCtx->priv_data, "tune", "zerolatency", 0);
        av_opt_set(pOutputCodecCtx->priv_data, "profile", "main", 0);
        av_opt_set(pOutputCodecCtx->priv_data, "x264-params", "crf=20", 0);
    }

    //Show some Information
    av_dump_format(pOutputFormatCtx, 0, out_file, 1);

    if (pOutputFormatCtx->oformat->flags & AVFMT_GLOBALHEADER)
        outStream->codec->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;

    if (avcodec_open2(pOutputCodecCtx, pOutputCodec, NULL) < 0){
        printf("Failed to open encoder! \n");
        return -1;
    }

    int ret = avcodec_parameters_from_context(outStream->codecpar, pOutputCodecCtx);
    if (ret < 0) {
        printf("open_output_video_file_for_transcoding: Failed to copy encoder parameters to output stream");
        return -1;
    }

    AVFrame* pFrame = av_frame_alloc();

    //Write File Header
    avformat_write_header(pOutputFormatCtx, NULL);

    if (pInStreamCodecCtx->pix_fmt != pOutputCodecCtx->pix_fmt)
    {
        frameScaleCtx = sws_getContext(pInStreamCodecCtx->width,
            pInStreamCodecCtx->height,
            pInStreamCodecCtx->pix_fmt,
            pOutputCodecCtx->width,
            pOutputCodecCtx->height,
            pOutputCodecCtx->pix_fmt,
            SWS_BICUBIC,
            NULL,
            NULL,
            NULL);
    }

    auto threadParamPtr = new CodecThreadParam();
    threadParamPtr->isNeededTranscode = pInStreamFormatCtx->streams[videoindex]->codec->pix_fmt != AV_PIX_FMT_YUV420P;

    pushFrameEvent.reset();
    InterlockedExchange8((char*)&isRecording, 1);
    _beginthreadex(0, 0, RecordThread, threadParamPtr, 0, 0);

    return 0;
}


UVC_API int OpenInput(const char* filepath, int& videoWidth, int& videoHeight)
{
    if (pInStreamFormatCtx != nullptr || isPlaying) {
        printf("[OpenInput] WARNING: Previous context still exists! Cleaning up...\n");
        CloseInput();
        Sleep(300);
    }
    strncpy(savedDevicePath, filepath, sizeof(savedDevicePath) - 1);

    av_register_all();
    avformat_network_init();
    pInStreamFormatCtx = avformat_alloc_context();
    //pInStreamFormatCtx->flags |= AVFMT_FLAG_NONBLOCK;//读取速度跟不上或者解码器尚未完全就绪，频繁的非阻塞调用可能导致 KSProxy 内部状态机崩溃

    //std::ofstream testFile;
    //testFile.open("test.txt");
    //testFile << "Start Connect. \r\n";
    //testFile.close();

    avdevice_register_all();

    AVDictionary *d = NULL;
    av_dict_set(&d, "stimeout", "5000000", 0);
    
    av_dict_set(&d, "rtbufsize", "10485760", 0);

    if (videoWidth > 0 && videoHeight > 0) {
        char videoSize[32];
        snprintf(videoSize, sizeof(videoSize), "%dx%d", videoWidth, videoHeight);
        av_dict_set(&d, "video_size", videoSize, 0);
    }

    AVInputFormat *ifmt = av_find_input_format("dshow");

    if (avformat_open_input(&pInStreamFormatCtx, filepath, ifmt, &d) != 0){
        printf("Couldn't open input stream.\n");
        return -1;
    }

    av_opt_set(pInStreamFormatCtx->priv_data, "framerate", "30", 0);
    //testFile.open("test.txt");
    //testFile << "Open input success. \r\n";
    //testFile.close();

    if (avformat_find_stream_info(pInStreamFormatCtx, NULL) < 0){
        printf("Couldn't find stream information.\n");
        return -1;
    }

    //testFile.open("test.txt");
    //testFile << "Find input stream info success. \r\n";
    //testFile.close();
    isFirstFrame = 0;
    videoindex = -1;
    for (int i = 0; i < pInStreamFormatCtx->nb_streams; i++)
        if (pInStreamFormatCtx->streams[i]->codec->codec_type == AVMEDIA_TYPE_VIDEO){
            videoindex = i;
            break;
        }
    if (videoindex == -1){
        printf("Didn't find a video stream.\n");
        return -1;
    }

    //testFile.open("test.txt");
    //testFile << "Find video stream success. \r\n";
    //testFile.close();

    pInStreamCodecCtx = pInStreamFormatCtx->streams[videoindex]->codec;

    videoHeight = pInStreamCodecCtx->height;
    videoWidth = pInStreamCodecCtx->width;

    // Print reported formats for debugging
    int par_pix_fmt = pInStreamFormatCtx->streams[videoindex]->codecpar->format;
    const char* par_name = av_get_pix_fmt_name((AVPixelFormat)par_pix_fmt);
    const char* ctx_name = av_get_pix_fmt_name(pInStreamCodecCtx->pix_fmt);
    //printf("Stream codecpar->format = %d (%s)\n", par_pix_fmt, par_name ? par_name : "(unknown)");
    //printf("CodecContext pix_fmt = %d (%s)\n", pInStreamCodecCtx->pix_fmt, ctx_name ? ctx_name : "(unknown)");

    // Prefer the decoder / stream reported values for pixel format and color range.
    // Only fall back to safe defaults if they are not specified.
    if (pInStreamCodecCtx->pix_fmt == AV_PIX_FMT_NONE) {
        // some capture devices/drivers don't report pixel format correctly;
        // choose a commonly used planar format as a safe default
        //pInStreamCodecCtx->pix_fmt = AV_PIX_FMT_YUYV422;//AV_PIX_FMT_YUV420P;
        //printf("pix_fmt��AV_PIX_FMT_YUV420P.\n");
        // 尝试根据 codec_id 推断更安全的默认值
        if (pInStreamCodecCtx->codec_id == AV_CODEC_ID_MJPEG) {
            pInStreamCodecCtx->pix_fmt = AV_PIX_FMT_YUVJ422P;
        }
        else if (pInStreamCodecCtx->codec_id == AV_CODEC_ID_RAWVIDEO) {
            // 对于 RAWVIDEO，保持 NONE 让 FFmpeg 自动处理，或者尝试 YUYV422
            pInStreamCodecCtx->pix_fmt = AV_PIX_FMT_YUYV422;
        }
        else {
            pInStreamCodecCtx->pix_fmt = AV_PIX_FMT_YUYV422;
        }
    }
    
    if (pInStreamCodecCtx->color_range == AVCOL_RANGE_UNSPECIFIED) {
        // if color range is not provided, assume full (JPEG) range as a safer default
        pInStreamCodecCtx->color_range = AVCOL_RANGE_JPEG;
        //printf("color_range��AVCOL_RANGE_JPEG.\n");
    }

    // Also print the codec context color range numeric value
    //printf("CodecContext color_range = %d\n", pInStreamCodecCtx->color_range);

    pInStreamCodec = avcodec_find_decoder(pInStreamCodecCtx->codec_id);
    if (pInStreamCodec == NULL){
        printf("Codec not found.\n");
        return -1;
    }

    //testFile.open("test.txt");
    //testFile << "Find codec success. \r\n";
    //testFile.close();

    if (avcodec_open2(pInStreamCodecCtx, pInStreamCodec, NULL) < 0){
        printf("Could not open codec.\n");
        return -1;
    }
    //Output Info-----------------------------
    printf("---------------- File Information ---------------\n");
    av_dump_format(pInStreamFormatCtx, 0, filepath, 0);
    printf("-------------------------------------------------\n");

    //testFile.open("test.txt");
    //testFile << "Dump format success. \r\n";
    //testFile.close();

    InterlockedExchange8((char*)&isPlaying, 1);
    stopPlayingEvent.reset();
    _beginthreadex(0, 0, DecodeThread, nullptr, 0, 0);

    return 0;
}

UVC_API void SetVideoDataCallback(VideoDataCallbackFunc cb, const void *user_data)
{
    videoDataCallbackFunc = cb;
    user_data_ptr = user_data;
}

UVC_API void SetYuvDataCallback(YuvDataCallbackFunc cb, const void *user_data)
{
    yuvDataCallbackFunc = cb;
}

UVC_API void SetRawDataCallback(RawDataCallbackFunc cb, const void *user_data)
{
    //rawDataCallbackFunc = cb;
    // user_data 已经在全局变量中设置
}

UVC_API void SetPlayStateChangeCallback(PlayStateChangeCallbackFunc cb)
{
    playStateChangeCallbackFunc = cb;
}

UVC_API int CloseInput()
{
    if (isRecording)
    {
        StopRecord();
    }

	if (isPlaying)
	{
		// ✅ 步骤1: 设置退出标志
		//InterlockedExchange8((char*)&isPlaying, 0);
		isPlaying = false;  // 直接设置为 false，DecodeThread 会检测到并退出
		
		// ✅ 步骤2: 等待 DecodeThread 完全退出
		// stopPlayingEvent 会在 DecodeThread 的 while 循环退出后被设置
		//printf("[CloseInput] Waiting for DecodeThread to exit...\n");
		stopPlayingEvent.wait();
		//printf("[CloseInput] DecodeThread exited successfully.\n");
		
		// ✅ 步骤3: 等待额外的时间，确保所有回调都执行完毕
		Sleep(500);  // 500ms 额外等待
		
	}

    // ✅ 步骤4: 释放全局资源（线程安全，因为 DecodeThread 已退出）
    if (pInStreamCodecCtx) {
        //printf("[CloseInput] Closing codec context...\n");
        avcodec_close(pInStreamCodecCtx);
        pInStreamCodecCtx = nullptr;
    }

    if (pInStreamFormatCtx) {
        //printf("[CloseInput] Closing input format context...\n");
        avformat_close_input(&pInStreamFormatCtx);
        avformat_free_context(pInStreamFormatCtx);
        pInStreamFormatCtx = nullptr;  // ✅ 重要：置空防止悬垂指针
    }

    videoindex = -1;  // 重置视频索引
    
    // Free interpolation buffer pool (使用 _aligned_free 释放对齐内存)
    for (int i = 0; i < 4; i++) {
        if (interpolationBuffer[i]) {
            _aligned_free(interpolationBuffer[i]);
            interpolationBuffer[i] = nullptr;
        }
        interpolationBufferSize[i] = 0;
    }
    interpolationBufferIndex = 0;
    
    //printf("[CloseInput] All resources released successfully.\n");
    return 0;
}

UVC_API int ReconfigureResolution(int& videoWidth, int& videoHeight)
{
    if (!pInStreamFormatCtx || !isPlaying) {
        printf("[ReconfigureResolution] ERROR: Not connected or not playing!\n");
        return -1;
    }

    if (savedDevicePath[0] == '\0') {
        printf("[ReconfigureResolution] ERROR: Device path not saved!\n");
        return -1;
    }

    printf("[ReconfigureResolution] Attempting to reconfigure from %dx%d to %dx%d\n", 
           pInStreamCodecCtx->width, pInStreamCodecCtx->height, videoWidth, videoHeight);

    isPlaying = false;
    stopPlayingEvent.wait();
    Sleep(500);

    if (pInStreamCodecCtx) {
        avcodec_close(pInStreamCodecCtx);
        pInStreamCodecCtx = nullptr;
    }

    if (pInStreamFormatCtx) {
        avformat_close_input(&pInStreamFormatCtx);
        avformat_free_context(pInStreamFormatCtx);
        pInStreamFormatCtx = nullptr;
    }

    videoindex = -1;

    for (int i = 0; i < 4; i++) {
        if (interpolationBuffer[i]) {
            _aligned_free(interpolationBuffer[i]);
            interpolationBuffer[i] = nullptr;
        }
        interpolationBufferSize[i] = 0;
    }
    interpolationBufferIndex = 0;

    av_register_all();
    avformat_network_init();
    pInStreamFormatCtx = avformat_alloc_context();

    avdevice_register_all();

    AVDictionary *d = NULL;
    av_dict_set(&d, "stimeout", "5000000", 0);
    av_dict_set(&d, "rtbufsize", "10485760", 0);

    if (videoWidth > 0 && videoHeight > 0) {
        char videoSize[32];
        snprintf(videoSize, sizeof(videoSize), "%dx%d", videoWidth, videoHeight);
        av_dict_set(&d, "video_size", videoSize, 0);
    }

    av_dict_set(&d, "framerate", "30", 0);

    AVInputFormat *ifmt = av_find_input_format("dshow");

    if (avformat_open_input(&pInStreamFormatCtx, savedDevicePath, ifmt, &d) != 0) {
        printf("[ReconfigureResolution] Couldn't open input stream.\n");
        return -1;
    }

    av_opt_set(pInStreamFormatCtx->priv_data, "framerate", "30", 0);

    if (avformat_find_stream_info(pInStreamFormatCtx, NULL) < 0) {
        printf("[ReconfigureResolution] Couldn't find stream information.\n");
        return -1;
    }

    isFirstFrame = 0;
    videoindex = -1;
    for (int i = 0; i < pInStreamFormatCtx->nb_streams; i++)
        if (pInStreamFormatCtx->streams[i]->codec->codec_type == AVMEDIA_TYPE_VIDEO) {
            videoindex = i;
            break;
        }
    if (videoindex == -1) {
        printf("[ReconfigureResolution] Didn't find a video stream.\n");
        return -1;
    }

    pInStreamCodecCtx = pInStreamFormatCtx->streams[videoindex]->codec;

    int requestedWidth = videoWidth;
    int requestedHeight = videoHeight;
    int actualWidth = pInStreamCodecCtx->width;
    int actualHeight = pInStreamCodecCtx->height;

    if (requestedWidth > 0 && requestedHeight > 0 &&
        (requestedWidth != actualWidth || requestedHeight != actualHeight)) {
        printf("[ReconfigureResolution] WARNING: Requested %dx%d, but device returned %dx%d\n",
            requestedWidth, requestedHeight, actualWidth, actualHeight);
    }

    videoWidth = actualWidth;
    videoHeight = actualHeight;

    if (pInStreamCodecCtx->pix_fmt == AV_PIX_FMT_NONE) {
        if (pInStreamCodecCtx->codec_id == AV_CODEC_ID_MJPEG) {
            pInStreamCodecCtx->pix_fmt = AV_PIX_FMT_YUVJ422P;
        }
        else if (pInStreamCodecCtx->codec_id == AV_CODEC_ID_RAWVIDEO) {
            pInStreamCodecCtx->pix_fmt = AV_PIX_FMT_YUYV422;
        }
        else {
            pInStreamCodecCtx->pix_fmt = AV_PIX_FMT_YUYV422;
        }
    }

    av_dump_format(pInStreamFormatCtx, 0, savedDevicePath, 0);

    InterlockedExchange8((char*)&isPlaying, 1);
    stopPlayingEvent.reset();
    _beginthreadex(0, 0, DecodeThread, nullptr, 0, 0);

    printf("[ReconfigureResolution] Successfully reconfigured to %dx%d\n", videoWidth, videoHeight);
    return 0;
}

// һ֡ԭʼRAWݱ棬ϲôAPI
UVC_API void CaptureOneRawFrame(const char* raw_save_path)
{
    if (raw_save_path == NULL || strlen(raw_save_path) == 0)
    {
        printf("RAW save path is empty!\n");
        return;
    }
    if (!isPlaying)
    {
        printf("Video is not playing, can't capture raw frame!\n");
        return;
    }
    // ñ·
    lockForRawCapture.lock();
    strncpy_s(g_rawSavePath, sizeof(g_rawSavePath), raw_save_path, _TRUNCATE);
    lockForRawCapture.unlock();
    // ԭӲλȡ01֤̰߳ȫ
    InterlockedExchange(&isCaptureRawFrame, 1);
    printf("Trigger raw frame capture, save to: %s\n", raw_save_path);
}

UVC_API void SetRawFrameMode(const int mode)
{
    isMode = mode;
}

UVC_API void SetRawScaleDown(const int scale)
{
    if (scale == lastRawScalePacked) return;

    lastRawScalePacked = scale;
    rawScaleDown = scale & 0x0F;
    colScaleDown = (scale >> 4) & 0x0F;
	printf("Set rawScaleDown=%d, colScaleDown=%d (packed: 0x%02X)\n", rawScaleDown, colScaleDown, scale);
}

UVC_API void SetColScaleDown(const int scale)
{
}
