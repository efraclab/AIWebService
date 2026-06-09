namespace AIWebservice.Models.coa
{

    public sealed class AuditLogEntry
    {
        public int Id { get; set; }
        public string RegNo { get; set; } = string.Empty;

        public string ActionType { get; set; } = string.Empty;

        public string? GroupCode { get; set; }

        public string? Parameter { get; set; }

        public string FieldName { get; set; } = string.Empty;

        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        public string? ChangedBy { get; set; }
        public DateTime ChangedAt { get; set; }

        public string? Notes { get; set; }
    }
}