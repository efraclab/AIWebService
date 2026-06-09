using System.ComponentModel.DataAnnotations;

namespace AIWebservice.Models.coa
{
    public sealed class RegNoReviewDetailUpdate
    {
        [Required(ErrorMessage = "GroupCode is required to identify the row.")]
        public string GroupCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Parameter is required to identify the row.")]
        public string Parameter { get; set; } = string.Empty;

        public string? UOM { get; set; }  // Head_TestUnit
        public string? Method { get; set; }  // TRN2SAMPLINGMETHOD
        public string? LOQ { get; set; }  // Trn2LOQ
        public string? Requirements { get; set; }  // TRN2HEADSPEC
        public string? Results { get; set; }  // Trn2input
        public string? Remarks { get; set; }  // trn2remk
        public DateTime? AnalysisStartDate { get; set; }  // TRN2_ANA_STARTDT
        public DateTime? AnalysisCompletionDate { get; set; }  // TRN2COMPLETIONDT
        public decimal? SampleQuantityReceived { get; set; }  // TRN2QTY
        public string? SampleQuantityUnit { get; set; }  // TRN2PRODUNIT
        public string? SamplingMethod { get; set; }  // TRN2SAMPLINGMETHOD
        public string? SampleRegistrationNumber { get; set; }  // TRN2REGREFNO
        public DateTime? IssueDate { get; set; }  // trn2repodt

        public bool HasAnyValue() =>
            UOM != null ||
            Method != null ||
            LOQ != null ||
            Requirements != null ||
            Results != null ||
            Remarks != null ||
            AnalysisStartDate != null ||
            AnalysisCompletionDate != null ||
            SampleQuantityReceived != null ||
            SampleQuantityUnit != null ||
            SamplingMethod != null ||
            SampleRegistrationNumber != null ||
            IssueDate != null;
    }
}