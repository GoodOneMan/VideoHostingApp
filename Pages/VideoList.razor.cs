using VideoHostingApp.Models;
using VideoHostingApp.Services;

namespace VideoHostingApp.Pages
{
    public partial class VideoList
    {
        private List<VideoFile>? videos;
        private VideoFile? selectedVideo;

        protected override async Task OnInitializedAsync()
        {
            await LoadVideos();
        }

        private async Task LoadVideos()
        {
            videos = await Task.Run(() => VideoService.GetVideoFiles());
            StateHasChanged();
        }

        private async Task RefreshList()
        {
            await LoadVideos();
            if (selectedVideo != null && videos?.Any(v => v.Name == selectedVideo.Name) == false)
            {
                selectedVideo = null;
            }
        }

        private void SelectVideo(VideoFile video)
        {
            selectedVideo = video;
            StateHasChanged();
        }

        private string GetVideoUrl(string filepath)
        {
            return $"api/video/stream?filepath={Uri.EscapeDataString(filepath)}";
        }
    }
}
