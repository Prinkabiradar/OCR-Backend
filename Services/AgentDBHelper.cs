using Npgsql;
using NpgsqlTypes;  
using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public class AgentDBHelper
    {
        private readonly SqlDBHelper _sqlDBHelper;
        public AgentDBHelper(SqlDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }

        
        public async Task<(List<DocumentPageResult> Pages, int TotalCount)> SearchDocumentPages(
            string query,
            int startIndex,
            int pageSize)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_query",      NpgsqlDbType.Text)    { Value = query      },
                new NpgsqlParameter("p_startindex", NpgsqlDbType.Integer) { Value = startIndex },
                new NpgsqlParameter("p_pagesize",   NpgsqlDbType.Integer) { Value = pageSize   }
            };

            string sql = "SELECT * FROM search_document_pages(@p_query, @p_startindex, @p_pagesize)";
            var dt = await _sqlDBHelper.ExecuteDataTableWithParametersAsync(sql, parameters);

            var results = new List<DocumentPageResult>();
            int totalCount = 0;

            foreach (System.Data.DataRow row in dt.Rows)
            {
                if (totalCount == 0)
                    totalCount = Convert.ToInt32(row["TotalCount"]);

                results.Add(new DocumentPageResult
                {
                    DocumentPageId = Convert.ToInt32(row["DocumentPageId"]),
                    DocumentId = Convert.ToInt32(row["DocumentId"]),
                    PageNumber = Convert.ToInt32(row["PageNumber"]),
                    ExtractedText = row["ExtractedText"].ToString(),
                    DocumentName = row["DocumentName"].ToString()
                });
            }

            return (results, totalCount);
        }
 
        public async Task<List<DocumentPageResult>> SearchDocumentPages(string query)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_query",      NpgsqlDbType.Text)    { Value = query },
                new NpgsqlParameter("p_startindex", NpgsqlDbType.Integer) { Value = 1    },
                new NpgsqlParameter("p_pagesize",   NpgsqlDbType.Integer) { Value = 10000 }
            };

            string sql = "SELECT * FROM search_document_pages(@p_query, @p_startindex, @p_pagesize)";
            var dt = await _sqlDBHelper.ExecuteDataTableWithParametersAsync(sql, parameters);

            var results = new List<DocumentPageResult>();

            foreach (System.Data.DataRow row in dt.Rows)
            {
                results.Add(new DocumentPageResult
                {
                    DocumentPageId = Convert.ToInt32(row["DocumentPageId"]),
                    DocumentId = Convert.ToInt32(row["DocumentId"]),
                    PageNumber = Convert.ToInt32(row["PageNumber"]),
                    ExtractedText = row["ExtractedText"].ToString(),
                    DocumentName = row["DocumentName"].ToString()
                });
            }

            return results;
        }

        // ─── Get saved summary from DB ────────────────────────────────────────────────
        public async Task<DocumentSummaryRecord?> GetDocumentSummary(string documentName)
        {
            var parameters = new[]
            {
        new NpgsqlParameter("p_document_name", NpgsqlDbType.Text) { Value = documentName }
    };

            string sql = "SELECT * FROM get_document_summary(@p_document_name)";
            var dt = await _sqlDBHelper.ExecuteDataTableWithParametersAsync(sql, parameters);

            if (dt.Rows.Count == 0) return null;

            var row = dt.Rows[0];
            return new DocumentSummaryRecord
            {
                SummaryId = Convert.ToInt32(row["SummaryId"]),
                DocumentName = row["DocumentName"].ToString()!,
                SummaryText = row["SummaryText"].ToString()!,
                UpdatedAt = Convert.ToDateTime(row["UpdatedAt"]),
                CreatedAt = Convert.ToDateTime(row["CreatedAt"]),
                CreatedBy = row["CreatedBy"] == DBNull.Value ? null : Convert.ToInt32(row["CreatedBy"]),
                UpdatedBy = row["UpdatedBy"] == DBNull.Value ? null : Convert.ToInt32(row["UpdatedBy"]),
                ApprovedBy = row["ApprovedBy"] == DBNull.Value ? null : Convert.ToInt32(row["ApprovedBy"]),
                ApprovedAt = row["ApprovedAt"] == DBNull.Value ? null : Convert.ToDateTime(row["ApprovedAt"])
            };
        }

        public async Task<int> InsertUpdateDocumentSummary(
            int summaryId, string documentName, string summaryText, int userId,int roleId)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_summaryid",    NpgsqlDbType.Integer) { Value = summaryId    },
                new NpgsqlParameter("p_documentname", NpgsqlDbType.Varchar) { Value = documentName },
                new NpgsqlParameter("p_summarytext",  NpgsqlDbType.Text)    { Value = summaryText  },
                new NpgsqlParameter("p_userid",       NpgsqlDbType.Integer) { Value = userId       },
                new NpgsqlParameter("p_roleid",       NpgsqlDbType.Integer) { Value = roleId       }
            };

            string sql = "SELECT public.insertupdate_documentsummary(" +
                         "@p_summaryid, @p_documentname, @p_summarytext, @p_userid,@p_roleid)";

            var dt = await _sqlDBHelper.ExecuteDataTableWithParametersAsync(sql, parameters);
            return Convert.ToInt32(dt.Rows[0][0]);
        }

        public async Task<DataTable> GetSummaryData(SummaryData model)
        {
            DataTable dt = new DataTable();
            string query = @"SELECT * FROM get_document_summary_data( 
                                @p_startindex,
                                @p_pagesize,
                                @p_searchby,
                                @p_searchcriteria)";

            var parameters = new[]
            { 
                new NpgsqlParameter("p_startindex", model.StartIndex),
                new NpgsqlParameter("p_pagesize", model.PageSize),
                new NpgsqlParameter("p_searchby", (object?)model.SearchBy ?? DBNull.Value),
                new NpgsqlParameter("p_searchcriteria", (object?)model.SearchCriteria ?? DBNull.Value)
            };

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);
            dt.Load(reader);
            return dt;
        }
    }
}