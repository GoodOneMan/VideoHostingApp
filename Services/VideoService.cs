using VideoHostingApp.Models;

namespace VideoHostingApp.Services
{
    public class VideoService
    {
        private readonly string _videoStorage;
        private readonly ILogger<VideoService> _logger;

        public VideoService(IConfiguration configuration, ILogger<VideoService> logger)
        {
            _logger = logger;
            // Читаем путь к папке из конфигурации
            _videoStorage = configuration["VideoStorage"] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_videoStorage))
            {
                throw new InvalidOperationException("Папка с видео не задана в настройках (VideoFolder).");
            }

            // Создаём папку, если её нет (чтобы избежать ошибок)
            if (!Directory.Exists(_videoStorage))
            {
                _logger.LogWarning("Папка {Folder} не существует. Будет создана.", _videoStorage);
                Directory.CreateDirectory(_videoStorage);
            }
        }

        /// <summary>
        /// Возвращает список всех категорий.
        /// </summary>
        public List<VideoCategory> GetVideoCategories()
        {
            var videoCategories = new List<VideoCategory>();
            var categories = Directory.GetDirectories(_videoStorage);
            foreach (var category in categories)
            {
                videoCategories.Add(new VideoCategory
                {
                    Id = new Guid(),
                    Name = new DirectoryInfo(category).Name,
                    Path = category
                });
            }
            return videoCategories;
        }

        /// <summary>
        /// Возвращает список всех MP4-файлов в указанной папке.
        /// </summary>
        public List<VideoFile> GetVideoFiles()
        {
            var videoFiles = new List<VideoFile>();

            try
            {
                // Ищем все файлы с расширением .mp4 (без учёта регистра)
                var mp4Files = Directory.GetFiles(_videoStorage, "*.mp4", SearchOption.AllDirectories);

                foreach (var filePath in mp4Files)
                {
                    var fileInfo = new FileInfo(filePath);
                    videoFiles.Add(new VideoFile
                    {
                        Name = fileInfo.Name,
                        FullPath = fileInfo.FullName,
                        SizeBytes = fileInfo.Length
                    });
                }

                // Сортируем по имени для удобства
                videoFiles = videoFiles.OrderBy(f => f.Name).ToList();
                _logger.LogInformation("Найдено {Count} видеофайлов.", videoFiles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сканировании папки {Folder}", _videoStorage);
            }

            return videoFiles;
        }

        /// <summary>
        /// Проверяет, существует ли файл с указанным именем в разрешённой папке.
        /// Возвращает полный путь, если файл безопасен и существует.
        /// </summary>
        public string? GetSafeFilePath(string fileName)
        {
            // Защита от попыток выйти за пределы корневой папки (path traversal)
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") || fileName.Contains(Path.DirectorySeparatorChar))
            {
                _logger.LogWarning("Попытка доступа к недопустимому имени файла: {FileName}", fileName);
                return null;
            }

            var fullPath = Path.GetFullPath(Path.Combine(_videoStorage, fileName));
            // Дополнительная проверка: путь должен начинаться с корневой папки
            if (!fullPath.StartsWith(_videoStorage, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Попытка выхода за пределы разрешённой папки: {FullPath}", fullPath);
                return null;
            }

            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            _logger.LogWarning("Файл не найден: {FullPath}", fullPath);
            return null;
        }
    }
}
