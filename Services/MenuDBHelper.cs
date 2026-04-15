using Npgsql;
using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public class MenuDBHelper
    {
        private readonly SqlDBHelper _sqlDBHelper;

        public MenuDBHelper(SqlDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }

        public async Task<List<MenuItem>> GetMenuByRole(int roleId)
        {
            List<MenuItem> menuItems = new();

            NpgsqlParameter[] parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("p_roleid", roleId)
            };

            //using (var reader = await _sqlDBHelper
            //   .ExecuteReaderAsync("public.sidemenugetmenubyrole", parameters))
            using (var reader = await _sqlDBHelper
   .ExecuteReaderAsync(
       "SELECT * FROM public.sidemenugetmenubyrole(@p_roleid)",
       parameters))
            {
                while (await reader.ReadAsync())
                {
                    var menuItem = new MenuItem
                    {
                        MenuId = reader.GetInt32(reader.GetOrdinal("MenuId")),
                        Title = reader.GetString(reader.GetOrdinal("Title")),
                        Route = reader.GetString(reader.GetOrdinal("Route")),
                        Icon = reader.IsDBNull(reader.GetOrdinal("Icon"))
                                ? null
                                : reader.GetString(reader.GetOrdinal("Icon")),
                        ParentId = reader.IsDBNull(reader.GetOrdinal("ParentId"))
                                ? null
                                : reader.GetInt32(reader.GetOrdinal("ParentId")),
                        CanView = reader.GetBoolean(reader.GetOrdinal("CanView")),
                        CanInsert = reader.GetBoolean(reader.GetOrdinal("CanInsert")),
                        CanUpdate = reader.GetBoolean(reader.GetOrdinal("CanUpdate")),
                        CanDelete = reader.GetBoolean(reader.GetOrdinal("CanDelete")),
                        SubMenu = new List<MenuItem>()
                    };

                    menuItems.Add(menuItem);
                }
            }

            return BuildMenuHierarchy(menuItems);
        }

        private List<MenuItem> BuildMenuHierarchy(List<MenuItem> menuItems)
        {
            var menuDictionary = menuItems.ToDictionary(m => m.MenuId);
            var rootMenuItems = new List<MenuItem>();

            foreach (var menuItem in menuItems)
            {
                if (menuItem.ParentId == null)
                {
                    rootMenuItems.Add(menuItem);
                }
                else if (menuDictionary.ContainsKey(menuItem.ParentId.Value))
                {
                    menuDictionary[menuItem.ParentId.Value]
                        .SubMenu.Add(menuItem);
                }
            }

            return rootMenuItems;
        }
    }
}