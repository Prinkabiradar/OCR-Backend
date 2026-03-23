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
    public async Task<bool> DeleteForAll(int typeId, int primaryId, int userId)
    {
        NpgsqlParameter[] parameters =
        {
        new NpgsqlParameter("p_id",   primaryId),
        new NpgsqlParameter("p_type", typeId)
    };

        var result = await _sqlDBHelper.ExecuteFunctionAsync("alldelete", parameters);

        // Read the boolean result from the returned DataTable
        if (result != null && result.Rows.Count > 0)
        {
            return Convert.ToBoolean(result.Rows[0][0]);
        }
        return false;
    }
}