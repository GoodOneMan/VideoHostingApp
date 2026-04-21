using Microsoft.AspNetCore.Mvc;
using System.Text;
using VideoHostingApp.Services;

namespace VideoHostingApp.Controllers
{
    [ApiController]
    [Route("api/video")]
    public class VideoController : ControllerBase
    {
        private readonly VideoService _videoService;
        private readonly ILogger<VideoController> _logger;

        public VideoController(VideoService videoService, ILogger<VideoController> logger)
        {
            _videoService = videoService;
            _logger = logger;
        }

        [HttpGet("stream")]
        public async Task StreamVideo(string filepath, CancellationToken cancellationToken)
        {

            // ========== 1. Инициализация и проверки ==========
            const int bufferSize = 81920; // 80 КБ – оптимально для большинства дисковых I/O
            var fileInfo = new FileInfo(filepath);

            if (!fileInfo.Exists)
            {
                Response.StatusCode = 404;
                await Response.WriteAsync("File not found", cancellationToken);
                return;
            }

            var fileLength = fileInfo.Length;
            var lastModified = fileInfo.LastWriteTimeUtc;
            var etag = $"\"{lastModified.Ticks:x}\""; // ETag в hex для краткости

            // ========== 2. Обработка HEAD-запроса ==========
            if (HttpMethods.IsHead(Request.Method))
            {
                Response.StatusCode = 200;
                Response.Headers.ContentType = "video/mp4";
                Response.Headers.ContentLength = fileLength;
                Response.Headers.AcceptRanges = "bytes";
                Response.Headers.ETag = etag;
                Response.Headers.LastModified = lastModified.ToString("R");
                return;
            }

            // ========== 3. Проверка If-Match / If-None-Match (опционально) ==========
            var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == etag)
            {
                Response.StatusCode = 304;
                return;
            }

            // ========== 4. Разбор заголовка Range ==========
            var rangeHeader = Request.Headers.Range.ToString();
            var isRangeRequest = false;
            List<(long start, long end)> ranges = new List<(long, long)>();

            // Если есть заголовок If-Range, проверяем, что файл не изменился
            var ifRange = Request.Headers.IfRange.ToString();
            if (!string.IsNullOrEmpty(ifRange) && ifRange != etag && ifRange != lastModified.ToString("R"))
            {
                // Файл изменился – игнорируем Range и отдаём весь файл
                rangeHeader = null;
            }

            if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
            {
                var rangeValue = rangeHeader.Substring(6);
                var rawRanges = rangeValue.Split(',');

                foreach (var rawRange in rawRanges)
                {
                    var parts = rawRange.Trim().Split('-');
                    long start = 0, end = fileLength - 1;
                    bool valid = false;

                    if (parts.Length == 2)
                    {
                        // Обработка: bytes=100-200, bytes=100-, bytes=-200
                        var startStr = parts[0];
                        var endStr = parts[1];

                        if (!string.IsNullOrEmpty(startStr))
                        {
                            // bytes=100-200 или bytes=100-
                            if (long.TryParse(startStr, out start))
                            {
                                if (!string.IsNullOrEmpty(endStr) && long.TryParse(endStr, out end))
                                {
                                    // bytes=100-200
                                    valid = true;
                                }
                                else if (string.IsNullOrEmpty(endStr))
                                {
                                    // bytes=100- (до конца)
                                    end = fileLength - 1;
                                    valid = true;
                                }
                            }
                        }
                        else if (!string.IsNullOrEmpty(endStr))
                        {
                            // bytes=-200 (последние 200 байт)
                            if (long.TryParse(endStr, out var suffixLength))
                            {
                                start = Math.Max(0, fileLength - suffixLength);
                                end = fileLength - 1;
                                valid = true;
                            }
                        }
                    }

                    if (valid && start <= end && start < fileLength)
                    {
                        end = Math.Min(end, fileLength - 1);
                        ranges.Add((start, end));
                        isRangeRequest = true;
                    }
                }

                // Если ни один диапазон не корректен – 416
                if (isRangeRequest && ranges.Count == 0)
                {
                    Response.StatusCode = 416; // Range Not Satisfiable
                    Response.Headers.ContentRange = $"bytes */{fileLength}";
                    return;
                }
            }

            // ========== 5. Подготовка ответа ==========
            Response.Headers.AcceptRanges = "bytes";
            Response.Headers.ETag = etag;
            Response.Headers.LastModified = lastModified.ToString("R");
            Response.Headers.CacheControl = "private, max-age=86400"; // кэш на сутки

            // Если диапазоны не запрошены или запрос некорректен – отдаём весь файл
            if (!isRangeRequest || ranges.Count == 0)
            {
                Response.StatusCode = 200;
                Response.Headers.ContentType = "video/mp4";
                Response.Headers.ContentLength = fileLength;
                await using var fileStream = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
                await fileStream.CopyToAsync(Response.Body, bufferSize, cancellationToken);
                return;
            }

            // ========== 6. Обработка одного диапазона ==========
            if (ranges.Count == 1)
            {
                var (start, end) = ranges[0];
                var contentLength = end - start + 1;
                Response.StatusCode = 206; // Partial Content
                Response.Headers.ContentType = "video/mp4";
                Response.Headers.ContentRange = $"bytes {start}-{end}/{fileLength}";
                Response.Headers.ContentLength = contentLength;

                // Эффективная отправка части файла через SendFileAsync (нулевое копирование)
                await using var fileStream = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
                fileStream.Seek(start, SeekOrigin.Begin);
                await fileStream.CopyToAsync(Response.Body, bufferSize, cancellationToken);
                return;
            }

            // ========== 7. Обработка нескольких диапазонов (multipart/byteranges) ==========
            var boundary = $"batch_{Guid.NewGuid():N}";
            var contentType = $"multipart/byteranges; boundary={boundary}";
            Response.StatusCode = 206;
            Response.Headers.ContentType = contentType;

            // Пишем multipart-ответ напрямую в поток
            foreach (var (start, end) in ranges)
            {
                var rangeHeaderValue = $"--{boundary}\r\nContent-Type: video/mp4\r\nContent-Range: bytes {start}-{end}/{fileLength}\r\n\r\n";
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(rangeHeaderValue), cancellationToken);

                // Отправляем бинарный фрагмент
                await using var fileStream = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
                fileStream.Seek(start, SeekOrigin.Begin);
                var bytesToSend = end - start + 1;
                var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(bufferSize);
                try
                {
                    while (bytesToSend > 0)
                    {
                        var toRead = (int)Math.Min(bufferSize, bytesToSend);
                        var read = await fileStream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
                        if (read == 0) break;
                        await Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        bytesToSend -= read;
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }

                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), cancellationToken);
            }

            // Завершающий boundary
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"--{boundary}--\r\n"), cancellationToken);
        }
    }
}
