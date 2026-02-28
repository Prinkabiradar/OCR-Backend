using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public interface IUtilityService
    {
        Task<List<DropdownOption>> AllDropdown(string searchTerm, int page, int pageSize, int type, int parentId);
    }
    public class UtilityService : IUtilityService
    {
        private readonly UtilityDBHelper _dbHelper;

        public UtilityService(UtilityDBHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public async Task<List<DropdownOption>> AllDropdown(string searchTerm, int page, int pageSize, int type, int parentId)
        {
            var dt = await _dbHelper.AllDropdown(searchTerm, page, pageSize, type, parentId);

            var list = new List<DropdownOption>();

            foreach (DataRow row in dt.Rows)
            {
                list.Add(new DropdownOption
                {
                    Id = Convert.ToInt32(row["id"]),
                    Text = row["texts"].ToString()
                });
            }

            return list;
        }
    }
}
