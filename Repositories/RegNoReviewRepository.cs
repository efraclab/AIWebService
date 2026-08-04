using AIWebservice.Models.coa;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AIWebservice.Repositories
{
    public sealed class RegNoReviewRepository
    {
        private readonly string _connectionString;

        public RegNoReviewRepository(IConfiguration configuration)
        {
            _connectionString = configuration["ConnectionStrings:Connection"]
                ?? throw new InvalidOperationException(
                    "Connection string 'ConnectionStrings:Connection' is not configured.");
        }

        private const string SelectByRegNoSql = @"
SELECT DISTINCT
    c.CUSTNAME               AS [IssuedToClientName],
    c.CUSTUNIT               AS [ClientUnit],
    c.CUSTADD1               AS [ClientAddress1],
    c.CUSTADD2               AS [ClientAddress2],
    c.CUSTADD3               AS [ClientAddress3],
    c.CUSTCITY               AS [ClientCity],
    c.CUSTPIN                AS [ClientPin],
    c.CUST_STATE             AS [ClientState],
    c.CUSTCOUNT              AS [ClientCountry],
    h.TRN1COAContPer         AS [KindAttention],
    h.TRN1REFNO              AS [ReportNo],
    d.TRN2REPODT             AS [IssueDate],
    h.TRN1DOCUMENT           AS [CustomerRef],
    h.TRN1DATE               AS [RefDate],
    h.TRN1RECDT              AS [SampleReceivedDate],
    h.TRN1DATE               AS [SampleRegistrationDate],
    d.TRN2REGREFNO           AS [SampleRegistrationNumber],
    h.TRN1PRODALIAS          AS [SampleType],
    d.TRN2SAMPLINGMETHOD     AS [SamplingMethod],
    h.TRN1DATEM              AS [MfgDate],
    h.TRN1BATCHN             AS [BatchNo],
    d.TRN2QTY                AS [SampleQuantityReceived],
    d.TRN2PRODUNIT           AS [SampleQuantityReceivedUnit],
    d.TRN2QTY                AS [SampleQuantityUsed],
    d.TRN2PRODUNIT           AS [SampleQuantityUsedUnit],
    s.Codedesc               AS [SamplerName],
    d.TRN2_ANA_STARTDT       AS [AnalysisStartDate],
    d.TRN2COMPLETIONDT       AS [AnalysisCompletionDate],
    g.CODECD                 AS [GroupCode],
    g.CODEDESC               AS [GroupName],
    p.HEADDESC               AS [Parameter],
    d.HEAD_TESTUNIT          AS [UOM],
    d.TRN2SAMPLINGMETHOD     AS [Method],
    d.TRN2LOQ                AS [LOQ],
    d.TRN2HEADSPEC           AS [Requirements],
    d.TRN2INPUT              AS [Results],
    d.TRN2REMK               AS [Remarks]
FROM TRN105 h

INNER JOIN TRN205 d
    ON d.TRN2REFNO = h.TRN1REFNO

LEFT JOIN OHEADMST p
    ON p.HEADCD = d.TRN2HEADER

LEFT JOIN OCUSTMST c
    ON c.CUSTACCCODE = h.TRN1COACUSTCD

LEFT JOIN OCODEMST s
    ON s.CODECD = h.TRN1SAMPLERCD
   AND s.CODETYPE = 'SN'

LEFT JOIN OCODEMST g
    ON g.CODECD = d.TRN2GROUPCD 
   AND g.CODETYPE = 'GM'
WHERE h.TRN1REFNO = @RegNo
ORDER BY h.TRN1REFNO;";

        public async Task<IReadOnlyList<RegNoReviewReportRow>> GetByRegNoAsync(
            string regNo, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(regNo))
                throw new ArgumentException("regNo must not be null or whitespace.", nameof(regNo));

            using var conn = new SqlConnection(_connectionString);
            var rows = await conn.QueryAsync<RegNoReviewReportRow>(
                new CommandDefinition(SelectByRegNoSql, new { RegNo = regNo.Trim() }, cancellationToken: ct));

            // Strip HTML tags and decode HTML entities from Results and Remarks
            // (the LIMS database stores these columns as HTML e.g. <p>&lt;0.001</p>)
            var list = rows.AsList();
            foreach (var row in list)
            {
                row.Results = StripHtml(row.Results);
                row.Remarks = StripHtml(row.Remarks);
            }
            return list;
        }


        /// Updates Trn105 header fields and/or Trn205 detail rows for the given regNo.
        /// Only non-null fields in each update object are written; rows with nothing to
        /// update are skipped and reported as such in the response.
        /// All DB writes (including audit logs) run in a single transaction — any error rolls back everything.
        public async Task<RegNoReviewUpdateResponse> UpdateAsync(
            string regNo,
            RegNoReviewHeaderUpdate? header,
            IReadOnlyList<RegNoReviewDetailUpdate> items,
            string? changedBy,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(regNo))
                throw new ArgumentException("regNo must not be null or whitespace.", nameof(regNo));

            regNo = regNo.Trim();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            try
            {
                // ── 1. Header update (Trn105) ─────────────────────────────────
                var headerRowsAffected = 0;

                if (header is not null && header.HasAnyValue())
                {
                    var (headerSql, headerParams) = BuildHeaderUpdateSql(regNo, header);
                    headerRowsAffected = await conn.ExecuteAsync(
                        new CommandDefinition(headerSql, headerParams, transaction: tx, cancellationToken: ct));

                    // Write one audit log entry per changed header field
                    var headerLogs = BuildHeaderAuditLogs(regNo, header, changedBy);
                    foreach (var log in headerLogs)
                        await InsertAuditLogAsync(conn, tx, log, ct);
                }

                // ── 2. Detail updates (Trn205) ────────────────────────────────
                var itemResults = new List<RegNoReviewDetailUpdateResult>(items.Count);
                var detailRowsAffected = 0;

                foreach (var item in items)
                {
                    // Skip rows where the keys are blank
                    if (string.IsNullOrWhiteSpace(item.GroupCode) || string.IsNullOrWhiteSpace(item.Parameter))
                    {
                        itemResults.Add(new RegNoReviewDetailUpdateResult
                        {
                            GroupCode = item.GroupCode,
                            Parameter = item.Parameter,
                            Skipped = true,
                            SkipReason = "GroupCode and Parameter must both be non-empty strings."
                        });
                        continue;
                    }

                    // Skip rows with no data to write
                    if (!item.HasAnyValue())
                    {
                        itemResults.Add(new RegNoReviewDetailUpdateResult
                        {
                            GroupCode = item.GroupCode,
                            Parameter = item.Parameter,
                            Skipped = true,
                            SkipReason = "No updatable fields were provided for this row."
                        });
                        continue;
                    }

                    var (detailSql, detailParams) = BuildDetailUpdateSql(regNo, item);
                    var affected = await conn.ExecuteAsync(
                        new CommandDefinition(detailSql, detailParams, transaction: tx, cancellationToken: ct));

                    detailRowsAffected += affected;

                    // Write one audit log entry per changed field in this detail row
                    var detailLogs = BuildDetailAuditLogs(regNo, item, changedBy);
                    foreach (var log in detailLogs)
                        await InsertAuditLogAsync(conn, tx, log, ct);

                    itemResults.Add(new RegNoReviewDetailUpdateResult
                    {
                        GroupCode = item.GroupCode,
                        Parameter = item.Parameter,
                        RowsAffected = affected,
                        Skipped = false
                    });
                }

                await tx.CommitAsync(ct);

                return new RegNoReviewUpdateResponse
                {
                    RegNo = regNo,
                    Success = true,
                    HeaderRowsAffected = headerRowsAffected,
                    DetailRowsAffected = detailRowsAffected,
                    ItemResults = itemResults,
                };
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }


        /// Returns the current approval record for a regNo, or null if none exists yet.
        public async Task<RegNoReviewApprovalRecord?> GetApprovalAsync(string regNo, CancellationToken ct = default)
        {
            regNo = regNo.Trim();
            using var conn = new SqlConnection(_connectionString);
            return await conn.QuerySingleOrDefaultAsync<RegNoReviewApprovalRecord>(
                new CommandDefinition(
                    @"SELECT id, reg_no AS RegNo, status AS Status,
                             reviewed_by AS ReviewedBy, reviewed_at AS ReviewedAt,
                             created_at AS CreatedAt, updated_at AS UpdatedAt
                      FROM AiReportReviewApproval
                      WHERE reg_no = @RegNo;",
                    new { RegNo = regNo },
                    cancellationToken: ct));
        }

        /// Upserts the approval status for a regNo (INSERT on first call, UPDATE on subsequent calls).
        /// Also writes a StatusChange entry to the audit log.
        public async Task<RegNoReviewApprovalRecord> UpsertApprovalAsync(
            string regNo,
            string status,
            string reviewedBy,
            string? notes,
            CancellationToken ct = default)
        {
            regNo = regNo.Trim();
            var now = DateTime.UtcNow;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            try
            {
                // Fetch the old status so we can log old → new
                var oldStatus = await conn.QuerySingleOrDefaultAsync<string>(
                    new CommandDefinition(
                        "SELECT status FROM AiReportReviewApproval WHERE reg_no = @RegNo;",
                        new { RegNo = regNo },
                        transaction: tx,
                        cancellationToken: ct));

                // UPSERT (MERGE would also work; MERGE has parse overhead so using IF-style)
                await conn.ExecuteAsync(new CommandDefinition(@"
                    IF EXISTS (SELECT 1 FROM AiReportReviewApproval WHERE reg_no = @RegNo)
                        UPDATE AiReportReviewApproval
                        SET    status      = @Status,
                               reviewed_by = @ReviewedBy,
                               reviewed_at = @Now,
                               updated_at  = @Now
                        WHERE  reg_no = @RegNo;
                    ELSE
                        INSERT INTO AiReportReviewApproval
                               (reg_no, status, reviewed_by, reviewed_at, created_at, updated_at)
                        VALUES (@RegNo, @Status, @ReviewedBy, @Now, @Now, @Now);",
                    new { RegNo = regNo, Status = status, ReviewedBy = reviewedBy, Now = now },
                    transaction: tx,
                    cancellationToken: ct));

                // Update TRN105.AI_REVIEW_STATUS: 'Y' = Approved, 'N' = Rejected
                var aiReviewStatus = status.Equals("Approved", StringComparison.OrdinalIgnoreCase) ? "Y"
                                   : status.Equals("Rejected", StringComparison.OrdinalIgnoreCase) ? "N"
                                   : null;

                if (aiReviewStatus is not null)
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        "UPDATE TRN105 SET AI_REVIEW_STATUS = @AiReviewStatus WHERE TRN1REFNO = @RegNo;",
                        new { AiReviewStatus = aiReviewStatus, RegNo = regNo },
                        transaction: tx,
                        cancellationToken: ct));
                }

                // Audit log — StatusChange
                await InsertAuditLogAsync(conn, tx, new AuditLogEntry
                {
                    RegNo = regNo,
                    ActionType = "StatusChange",
                    FieldName = "status",
                    OldValue = oldStatus,
                    NewValue = status,
                    ChangedBy = reviewedBy,
                    ChangedAt = now,
                    Notes = notes,
                }, ct);

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            // Return fresh record from DB
            return (await GetApprovalAsync(regNo, ct))!;
        }


        /// Fetches the full audit trail for a regNo, newest-first.
        public async Task<IReadOnlyList<AuditLogEntry>> GetAuditLogsAsync(
            string regNo, CancellationToken ct = default)
        {
            regNo = regNo.Trim();
            using var conn = new SqlConnection(_connectionString);
            var rows = await conn.QueryAsync<AuditLogEntry>(
                new CommandDefinition(
                    @"SELECT id           AS Id,
                             reg_no       AS RegNo,
                             action_type  AS ActionType,
                             group_code   AS GroupCode,
                             parameter    AS Parameter,
                             field_name   AS FieldName,
                             old_value    AS OldValue,
                             new_value    AS NewValue,
                             changed_by   AS ChangedBy,
                             changed_at   AS ChangedAt,
                             notes        AS Notes
                      FROM AiReportAuditLog
                      WHERE reg_no = @RegNo
                      ORDER BY changed_at DESC;",
                    new { RegNo = regNo },
                    cancellationToken: ct));
            return rows.AsList();
        }

        /// Builds a dynamic UPDATE for Trn105, setting only the non-null fields
        /// present in <paramref name="header"/>.
        private static (string Sql, DynamicParameters Params) BuildHeaderUpdateSql(
            string regNo, RegNoReviewHeaderUpdate header)
        {
            var setClauses = new List<string>();
            var p = new DynamicParameters();
            p.Add("RegNo", regNo);

            if (header.KindAttention is not null) { setClauses.Add("TRN1COAContPer = @KindAttention"); p.Add("KindAttention", NormaliseString(header.KindAttention)); }
            if (header.CustomerRef is not null) { setClauses.Add("TRN1Document = @CustomerRef"); p.Add("CustomerRef", NormaliseString(header.CustomerRef)); }
            if (header.SampleReceivedDate is not null) { setClauses.Add("TRN1RECDT = @SampleReceivedDate"); p.Add("SampleReceivedDate", header.SampleReceivedDate.Value); }
            if (header.SampleRegistrationDate is not null) { setClauses.Add("TRN1DATE = @SampleRegistrationDate"); p.Add("SampleRegistrationDate", header.SampleRegistrationDate.Value); }
            if (header.SampleType is not null) { setClauses.Add("TRN1PRODALIAS = @SampleType"); p.Add("SampleType", NormaliseString(header.SampleType)); }
            if (header.MfgDate is not null) { setClauses.Add("TRN1DATEM = @MfgDate"); p.Add("MfgDate", header.MfgDate.Value); }
            if (header.BatchNo is not null) { setClauses.Add("TRN1BATCHN = @BatchNo"); p.Add("BatchNo", NormaliseString(header.BatchNo)); }

            var sql = $"UPDATE Trn105 SET {string.Join(", ", setClauses)} WHERE TRN1REFNO = @RegNo;";
            return (sql, p);
        }

        /// Builds a dynamic UPDATE for Trn205, setting only the non-null fields
        /// present in <paramref name="item"/>. The WHERE clause keys on
        /// TRN2REFNO + Trn2groupcd + TRN2HEADER.
        private static (string Sql, DynamicParameters Params) BuildDetailUpdateSql(
            string regNo, RegNoReviewDetailUpdate item)
        {
            var setClauses = new List<string>();
            var p = new DynamicParameters();
            p.Add("RegNo", regNo);
            p.Add("GroupCode", item.GroupCode.Trim());
            p.Add("Parameter", item.Parameter.Trim());

            if (item.UOM is not null) { setClauses.Add("Head_TestUnit = @UOM"); p.Add("UOM", NormaliseString(item.UOM)); }
            if (item.Method is not null) { setClauses.Add("TRN2SAMPLINGMETHOD = @Method"); p.Add("Method", NormaliseString(item.Method)); }
            if (item.LOQ is not null) { setClauses.Add("Trn2LOQ = @LOQ"); p.Add("LOQ", NormaliseString(item.LOQ)); }
            if (item.Requirements is not null) { setClauses.Add("TRN2HEADSPEC = @Requirements"); p.Add("Requirements", NormaliseString(item.Requirements)); }
            if (item.Results is not null) { setClauses.Add("Trn2input = @Results"); p.Add("Results", NormaliseString(item.Results)); }
            if (item.Remarks is not null) { setClauses.Add("trn2remk = @Remarks"); p.Add("Remarks", NormaliseString(item.Remarks)); }
            if (item.AnalysisStartDate is not null) { setClauses.Add("TRN2_ANA_STARTDT = @AnalysisStartDate"); p.Add("AnalysisStartDate", item.AnalysisStartDate.Value); }
            if (item.AnalysisCompletionDate is not null) { setClauses.Add("TRN2COMPLETIONDT = @AnalysisCompletionDate"); p.Add("AnalysisCompletionDate", item.AnalysisCompletionDate.Value); }
            if (item.SampleQuantityReceived is not null) { setClauses.Add("TRN2QTY = @SampleQuantityReceived"); p.Add("SampleQuantityReceived", item.SampleQuantityReceived.Value); }
            if (item.SampleQuantityUnit is not null) { setClauses.Add("TRN2PRODUNIT = @SampleQuantityUnit"); p.Add("SampleQuantityUnit", NormaliseString(item.SampleQuantityUnit)); }
            if (item.SamplingMethod is not null && item.Method is null)
            {
                // SamplingMethod and Method both map to TRN2SAMPLINGMETHOD;
                // if both are supplied, Method takes precedence (already added above).
                setClauses.Add("TRN2SAMPLINGMETHOD = @SamplingMethod");
                p.Add("SamplingMethod", NormaliseString(item.SamplingMethod));
            }
            if (item.SampleRegistrationNumber is not null) { setClauses.Add("TRN2REGREFNO = @SampleRegistrationNumber"); p.Add("SampleRegistrationNumber", NormaliseString(item.SampleRegistrationNumber)); }
            if (item.IssueDate is not null) { setClauses.Add("trn2repodt = @IssueDate"); p.Add("IssueDate", item.IssueDate.Value); }

            // NOTE: The SELECT query exposes OHEADMST.HEADDESC as "Parameter" and
            // OCODEMST.CODECD as "GroupCode".  The UPDATE must therefore resolve
            // the human-readable parameter description back to the raw TRN2HEADER
            // code via a subquery, and match GroupCode directly (it already is CODECD).
            var sql = new StringBuilder();
            sql.Append("UPDATE Trn205 SET ");
            sql.Append(string.Join(", ", setClauses));
            sql.Append(" WHERE TRN2REFNO  = @RegNo");
            sql.Append("   AND Trn2groupcd = @GroupCode");
            sql.Append("   AND TRN2HEADER  = (SELECT TOP 1 HEADCD FROM OHEADMST WHERE HEADDESC = @Parameter);");

            return (sql.ToString(), p);
        }


        /// Produces one <see cref="AuditLogEntry"/> for every non-null field in the header update.
        /// Old values are not fetched from DB (would require an extra SELECT per field);
        /// they are left null — if you need before/after you can add a pre-fetch SELECT.
        private static IEnumerable<AuditLogEntry> BuildHeaderAuditLogs(
            string regNo, RegNoReviewHeaderUpdate header, string? changedBy)
        {
            var now = DateTime.UtcNow;

            if (header.KindAttention is not null)
                yield return Log("KindAttention", header.KindAttention);
            if (header.CustomerRef is not null)
                yield return Log("CustomerRef", header.CustomerRef);
            if (header.SampleReceivedDate is not null)
                yield return Log("SampleReceivedDate", header.SampleReceivedDate.Value.ToString("O"));
            if (header.SampleRegistrationDate is not null)
                yield return Log("SampleRegistrationDate", header.SampleRegistrationDate.Value.ToString("O"));
            if (header.SampleType is not null)
                yield return Log("SampleType", header.SampleType);
            if (header.MfgDate is not null)
                yield return Log("MfgDate", header.MfgDate.Value.ToString("O"));
            if (header.BatchNo is not null)
                yield return Log("BatchNo", header.BatchNo);

            AuditLogEntry Log(string fieldName, string newValue) => new()
            {
                RegNo = regNo,
                ActionType = "HeaderUpdate",
                FieldName = fieldName,
                NewValue = newValue,
                ChangedBy = changedBy,
                ChangedAt = now,
            };
        }

        /// Produces one <see cref="AuditLogEntry"/> for every non-null field in the detail update,
        /// tagging each entry with GroupCode + Parameter so the exact row is identifiable.
        private static IEnumerable<AuditLogEntry> BuildDetailAuditLogs(
            string regNo, RegNoReviewDetailUpdate item, string? changedBy)
        {
            var now = DateTime.UtcNow;

            if (item.UOM is not null) yield return Log("UOM", item.UOM);
            if (item.Method is not null) yield return Log("Method", item.Method);
            if (item.LOQ is not null) yield return Log("LOQ", item.LOQ);
            if (item.Requirements is not null) yield return Log("Requirements", item.Requirements);
            if (item.Results is not null) yield return Log("Results", item.Results);
            if (item.Remarks is not null) yield return Log("Remarks", item.Remarks);
            if (item.AnalysisStartDate is not null) yield return Log("AnalysisStartDate", item.AnalysisStartDate.Value.ToString("O"));
            if (item.AnalysisCompletionDate is not null) yield return Log("AnalysisCompletionDate", item.AnalysisCompletionDate.Value.ToString("O"));
            if (item.SampleQuantityReceived is not null) yield return Log("SampleQuantityReceived", item.SampleQuantityReceived.Value.ToString());
            if (item.SampleQuantityUnit is not null) yield return Log("SampleQuantityUnit", item.SampleQuantityUnit);
            if (item.SamplingMethod is not null) yield return Log("SamplingMethod", item.SamplingMethod);
            if (item.SampleRegistrationNumber is not null) yield return Log("SampleRegistrationNumber", item.SampleRegistrationNumber);
            if (item.IssueDate is not null) yield return Log("IssueDate", item.IssueDate.Value.ToString("O"));

            AuditLogEntry Log(string fieldName, string newValue) => new()
            {
                RegNo = regNo,
                ActionType = "DetailUpdate",
                GroupCode = item.GroupCode,
                Parameter = item.Parameter,
                FieldName = fieldName,
                NewValue = newValue,
                ChangedBy = changedBy,
                ChangedAt = now,
            };
        }

        /// Inserts a single row into AiReportAuditLog within the supplied transaction.
        private static Task InsertAuditLogAsync(
            SqlConnection conn,
            SqlTransaction tx,
            AuditLogEntry entry,
            CancellationToken ct)
        {
            return conn.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO AiReportAuditLog
                    (reg_no, action_type, group_code, parameter,
                     field_name, old_value, new_value,
                     changed_by, changed_at, notes)
                VALUES
                    (@RegNo, @ActionType, @GroupCode, @Parameter,
                     @FieldName, @OldValue, @NewValue,
                     @ChangedBy, @ChangedAt, @Notes);",
                new
                {
                    entry.RegNo,
                    entry.ActionType,
                    entry.GroupCode,
                    entry.Parameter,
                    entry.FieldName,
                    entry.OldValue,
                    entry.NewValue,
                    entry.ChangedBy,
                    entry.ChangedAt,
                    entry.Notes,
                },
                transaction: tx,
                cancellationToken: ct));
        }

        /// Trims a string value. Returns null if the result is empty so that
        /// accidental whitespace-only patches do not wipe database values.
        private static string? NormaliseString(string? value)
        {
            if (value is null) return null;
            var trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        /// Strips all HTML tags from a string and decodes HTML entities.
        /// e.g. "<p>&lt;0.001</p>"  →  "<0.001"
        ///      "<p>The Sample <b>Conforms</b></p>"  →  "The Sample Conforms"
        /// Returns null if the input is null or whitespace-only after stripping.

        private static readonly Regex HtmlTagRegex =
            new(@"<[^>]*>", RegexOptions.Compiled | RegexOptions.Singleline);

        private static string? StripHtml(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            var stripped = HtmlTagRegex.Replace(value, string.Empty);
            var decoded = WebUtility.HtmlDecode(stripped);
            var trimmed = decoded.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}