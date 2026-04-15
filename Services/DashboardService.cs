using OCR_BACKEND.Modals;

namespace OCR_BACKEND.Services
{
    public interface IDashboardService
    {
        Task<DashboardResponse> GetFullDashboard();
    }

    public class DashboardService : IDashboardService
    {
        private readonly DashboardDBHelper _dashboardDBHelper;

        public DashboardService(DashboardDBHelper dashboardDBHelper)
        {
            _dashboardDBHelper = dashboardDBHelper;
        }

        public async Task<DashboardResponse> GetFullDashboard()
        {
            return await _dashboardDBHelper.GetFullDashboard();
        }
    }
}