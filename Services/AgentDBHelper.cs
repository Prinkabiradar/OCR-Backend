using Npgsql;
using NpgsqlTypes;  
using OCR_BACKEND.Modals;

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
    }
}