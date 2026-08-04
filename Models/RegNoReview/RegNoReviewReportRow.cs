namespace AIWebservice.Models.coa
{
    public sealed class RegNoReviewReportRow
    {
        public string? IssuedToClientName { get; set; }
        public string? ClientUnit { get; set; }
        public string? ClientAddress1 { get; set; }
        public string? ClientAddress2 { get; set; }
        public string? ClientAddress3 { get; set; }
        public string? ClientCity { get; set; }
        public string? ClientPin { get; set; }
        public string? ClientState { get; set; }
        public string? ClientCountry { get; set; }
        public string? KindAttention { get; set; }
        public string? ReportNo { get; set; }
        public DateTime? IssueDate { get; set; }
        public string? CustomerRef { get; set; }
        public DateTime? RefDate { get; set; }
        public DateTime? SampleReceivedDate { get; set; }
        public DateTime? SampleRegistrationDate { get; set; }
        public string? SampleType { get; set; }
        public DateTime? MfgDate { get; set; }
        public string? BatchNo { get; set; }
        public string? SampleRegistrationNumber { get; set; }
        public string? SamplingMethod { get; set; }
        public decimal? SampleQuantityReceived { get; set; }
        public string? SampleQuantityReceivedUnit { get; set; }
        public decimal? SampleQuantityUsed { get; set; }
        public string? SampleQuantityUsedUnit { get; set; }
        public string? SamplerName { get; set; }
        public DateTime? AnalysisStartDate { get; set; }
        public DateTime? AnalysisCompletionDate { get; set; }

        public string? GroupCode { get; set; }
        public string? GroupName { get; set; }
        public string? Parameter { get; set; }
        public string? UOM { get; set; }
        public string? Method { get; set; }
        public string? LOQ { get; set; }
        public string? Requirements { get; set; }
        public string? Results { get; set; }
        public string? Remarks { get; set; }
    }
}