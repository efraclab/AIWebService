namespace AIWebservice.Models.coa
{
    public sealed class RegNoReviewHeaderUpdate
    {
        public string? KindAttention { get; set; }  // TRN1COAContPer
        public string? CustomerRef { get; set; }  // TRN1Document
        public DateTime? SampleReceivedDate { get; set; }  // TRN1RECDT
        public DateTime? SampleRegistrationDate { get; set; }  // TRN1DATE
        public string? SampleType { get; set; }  // TRN1PRODALIAS
        public DateTime? MfgDate { get; set; }  // TRN1DATEM
        public string? BatchNo { get; set; }  // TRN1BATCHN

        public bool HasAnyValue() =>
            KindAttention != null ||
            CustomerRef != null ||
            SampleReceivedDate != null ||
            SampleRegistrationDate != null ||
            SampleType != null ||
            MfgDate != null ||
            BatchNo != null;
    }
}