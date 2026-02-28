using Npgsql;
using OCR_BACKEND.Services;
using System.Data;

public class UtilityDBHelper
{
    private readonly SqlDBHelper _sqlDBHelper;

    public UtilityDBHelper(SqlDBHelper sqlDBHelper)
    {
        _sqlDBHelper = sqlDBHelper;
    }

    public async Task<DataTable> AllDropdown(string searchTerm, int page, int pageSize, int type, int parentId)
    {
        NpgsqlParameter[] parameters =
       {
            new NpgsqlParameter("p_searchterm", searchTerm ?? ""),
            new NpgsqlParameter("p_page", page),
            new NpgsqlParameter("p_pagesize", pageSize),
            new NpgsqlParameter("p_type", type),
            new NpgsqlParameter("p_parentid", parentId)
        };

        var result = await _sqlDBHelper.ExecuteFunctionAsync("alldropdown", parameters);
        return result;
    }
}