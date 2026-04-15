using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public interface IMenuService
    {
        public Task<List<MenuItem>> GetMenuByRole(int roleId);
        //Task<DataTable> SideMenuGetReports(PaginationRequest model);
    }
    public class MenuService : IMenuService
    {
        private readonly MenuDBHelper _sqlDBHelper;

        public MenuService(MenuDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }


        public async Task<List<MenuItem>> GetMenuByRole(int roleId)
        {
            try
            {
                return await _sqlDBHelper.GetMenuByRole(roleId);
            }
            catch (System.Exception ex)
            {
                throw;
            }
        }
        //public async Task<DataTable> SideMenuGetReports(PaginationRequest model)
        //{
        //    try
        //    {
        //        var dataTable = await _sqlDBHelper.SideMenuGetReports(model);

        //        return dataTable;

        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine(ex.Message);
        //        throw;
        //    }
        //}

    }
}
