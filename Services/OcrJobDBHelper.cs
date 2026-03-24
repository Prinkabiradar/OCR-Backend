// Services/OcrJobDBHelper.cs
using Npgsql;
using NpgsqlTypes;
using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public class OcrJobDBHelper
    {
        private readonly SqlDBHelper _sqlDBHelper;

        public OcrJobDBHelper(SqlDBHelper sqlDBHelper)
        {
            _sqlDBHelper = sqlDBHelper;
        }

        public async Task<Guid> InsertOcrJob(Guid? jobId, int totalFiles)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_jobid",      (object?)jobId ?? DBNull.Value),
                new NpgsqlParameter("p_totalfiles", totalFiles)
            };

            string query = "SELECT insertupdate_ocrjob(@p_jobid, @p_totalfiles)";

            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);
            if (await reader.ReadAsync())
                return reader.GetGuid(0);

            throw new Exception("Failed to create OCR job");
        }

        public async Task UpdateJobStatus(Guid jobId, string status,
            int processedFiles, string? errorMessage = null)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_jobid",          jobId),
                new NpgsqlParameter("p_status",         status),
                new NpgsqlParameter("p_processedfiles", processedFiles),
                new NpgsqlParameter("p_errormessage",   (object?)errorMessage ?? DBNull.Value)
            };

            string query = "SELECT fn_ocrjob_updatestatus(@p_jobid, @p_status::ocr_job_status, @p_processedfiles, @p_errormessage)";

            await _sqlDBHelper.ExecuteReaderAsync(query, parameters);
        }

        public async Task<DataTable> GetOcrJobs(OcrJobFetchRequest model)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_startindex",     model.StartIndex),
                new NpgsqlParameter("p_pagesize",       model.PageSize),
                new NpgsqlParameter("p_searchby",       (object?)model.SearchBy ?? DBNull.Value),
                new NpgsqlParameter("p_searchcriteria", (object?)model.SearchCriteria ?? DBNull.Value)
            };

            string query = "SELECT * FROM fn_ocrjob_get(@p_startindex, @p_pagesize, @p_searchby, @p_searchcriteria)";

            DataTable dt = new DataTable();
            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);
            dt.Load(reader);
            return dt;
        }

        public async Task<DataTable> GetOcrJobById(Guid jobId)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_jobid", jobId)
            };

            string query = "SELECT * FROM fn_ocrjob_getbyid(@p_jobid)";

            DataTable dt = new DataTable();
            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);
            dt.Load(reader);
            return dt;
        }

        public async Task<DataTable> GetOcrJobResults(Guid jobId)
        {
            var parameters = new[]
            {
                new NpgsqlParameter("p_jobid", jobId)
            };

            string query = "SELECT * FROM fn_ocrjobresult_getbyjobid(@p_jobid)";

            DataTable dt = new DataTable();
            using var reader = await _sqlDBHelper.ExecuteReaderAsync(query, parameters);
            dt.Load(reader);
            return dt;
        }

        public async Task BulkInsertJobResults(List<OcrJobResult> results)
        {
            if (results.Count == 0) return;

            // Use PostgreSQL COPY for high-performance bulk insert
            using var conn = _sqlDBHelper.CreateConnection();
            await conn.OpenAsync();

            await using var writer = await conn.BeginBinaryImportAsync(
                "COPY ocr_job_results (job_id, file_name, ocr_text, success, error) FROM STDIN (FORMAT BINARY)");

            foreach (var r in results)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(r.JobId, NpgsqlDbType.Uuid);
                await writer.WriteAsync(r.FileName, NpgsqlDbType.Text);

                if (r.OcrText is null) await writer.WriteNullAsync();
                else await writer.WriteAsync(r.OcrText, NpgsqlDbType.Text);

                await writer.WriteAsync(r.Success, NpgsqlDbType.Boolean);

                if (r.Error is null) await writer.WriteNullAsync();
                else await writer.WriteAsync(r.Error, NpgsqlDbType.Text);
            }

            await writer.CompleteAsync();
        }
    }
}