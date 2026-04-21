namespace VideoHostingApp.Models
{
    public class VideoCategory
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }
}
