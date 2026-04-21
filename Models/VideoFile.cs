namespace VideoHostingApp.Models
{
    public class VideoFile
    {
        /// <summary>
        /// Id категории
        /// </summary>
        public Guid CategoryId { get; set; }

        /// <summary>
        /// Имя файла (например, "sample.mp4").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Полный путь к файлу на сервере (используется только на сервере).
        /// </summary>
        public string FullPath { get; set; } = string.Empty;

        /// <summary>
        /// Размер файла в байтах.
        /// </summary>
        public long SizeBytes { get; set; }

        /// <summary>
        /// Удобочитаемый размер файла (например, "25.3 MB").
        /// </summary>
        public string FormattedSize => FormatFileSize(SizeBytes);

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
