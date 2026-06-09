using AIWebservice.Models;

namespace AIWebservice.Services
{
    public sealed class ReportReviewService
    {
        private const long MaxFileBytes = 32 * 1024 * 1024; // Anthropic Files API per-file limit
        private const int MaxFiles = 10;
        private const string PdfMimeType = "application/pdf";

        private const string DefaultSystemPrompt =
            "You are an expert document reviewer. The user will provide one or more PDF " +
            "reports along with a prompt describing what to analyse or extract. Read every " +
            "supplied PDF carefully, ground your answer strictly in their content, and clearly " +
            "indicate which file each finding originates from when multiple PDFs are supplied. " +
            "If the documents do not contain enough information to answer, say so explicitly " +
            "instead of speculating.";

        private readonly ClaudeService _claude;
        private readonly ILogger<ReportReviewService> _logger;

        public ReportReviewService(ClaudeService claude, ILogger<ReportReviewService> logger)
        {
            _claude = claude;
            _logger = logger;
        }

        public async Task<PdfReviewResponse> ReviewAsync(
            PdfReviewRequest request,
            CancellationToken ct = default)
        {
            var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : request.CorrelationId;

            ValidateFiles(request.Files);

            _logger.LogInformation(
                "[{CorrelationId}] PDF review starting | files={Count} | promptLen={PromptLen}",
                correlationId, request.Files.Count, request.Prompt.Length);

            // 1. Upload every PDF to Anthropic (no base64 — multipart upload via Files API).
            var uploaded = new List<ClaudeFileUploadResponse>(request.Files.Count);
            try
            {
                foreach (var formFile in request.Files)
                {
                    await using var stream = formFile.OpenReadStream();
                    var uploadResp = await _claude.UploadFileAsync(
                        content: stream,
                        fileName: formFile.FileName,
                        mimeType: PdfMimeType,
                        correlationId: correlationId,
                        ct: ct);

                    uploaded.Add(uploadResp);
                }

                // 2. Send prompt + document references to Claude.
                var systemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt)
                    ? DefaultSystemPrompt
                    : request.SystemPrompt;

                var fileIds = uploaded.Select(u => u.Id).ToArray();

                var (reviewText, usage, modelUsed) = await _claude.SendWithDocumentsAsync(
                    systemPrompt: systemPrompt,
                    userPrompt: request.Prompt,
                    fileIds: fileIds,
                    model: request.ModelOverride,
                    maxTokens: request.MaxTokensOverride,
                    correlationId: correlationId,
                    ct: ct);

                _logger.LogInformation(
                    "[{CorrelationId}] PDF review completed | model={Model} | tokens={Total}",
                    correlationId, modelUsed, usage.InputTokens + usage.OutputTokens);

                // 3. Optionally clean up uploaded files (best-effort; never throws).
                var deleted = false;
                if (request.DeleteFilesAfter)
                {
                    foreach (var u in uploaded)
                        await _claude.DeleteFileAsync(u.Id, correlationId, ct);
                    deleted = true;
                }

                return new PdfReviewResponse
                {
                    CorrelationId = correlationId,
                    Success = true,
                    Review = reviewText,
                    Files = uploaded
                        .Select(u => new UploadedPdfInfo
                        {
                            FileId = u.Id,
                            Filename = u.Filename,
                            SizeBytes = u.SizeBytes,
                            Deleted = deleted,
                        })
                        .ToList(),
                    Usage = new TokenUsage
                    {
                        InputTokens = usage.InputTokens,
                        OutputTokens = usage.OutputTokens,
                    },
                    Model = modelUsed,
                    ProcessedAt = DateTimeOffset.UtcNow,
                };
            }
            catch
            {
                // If anything fails after uploads, try to clean up to avoid orphaned files.
                foreach (var u in uploaded)
                    await _claude.DeleteFileAsync(u.Id, correlationId, CancellationToken.None);
                throw;
            }
        }

        private static void ValidateFiles(IReadOnlyList<IFormFile> files)
        {
            if (files is null || files.Count == 0)
                throw new ArgumentException("At least one PDF file must be supplied.");

            if (files.Count > MaxFiles)
                throw new ArgumentException(
                    $"A maximum of {MaxFiles} PDF files may be submitted per request.");

            foreach (var f in files)
            {
                if (f.Length <= 0)
                    throw new ArgumentException($"File '{f.FileName}' is empty.");

                if (f.Length > MaxFileBytes)
                    throw new ArgumentException(
                        $"File '{f.FileName}' is {f.Length / (1024 * 1024)} MB which exceeds the 32 MB per-file limit.");

                var contentType = f.ContentType?.ToLowerInvariant() ?? string.Empty;
                var hasPdfExtension = f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

                if (contentType != PdfMimeType && !hasPdfExtension)
                    throw new ArgumentException(
                        $"File '{f.FileName}' is not a PDF (content-type='{contentType}'). Only application/pdf is supported.");
            }
        }
    }
}
