using AIWebservice.Models;

namespace AIWebservice.Services
{
    public sealed class PromptTemplateService
    {
        private static readonly Dictionary<string, string> _templates =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // ── Lab report validation ────────────────────────────────────────
                ["validate_report"] = """
                You are a strict LIMS lab-report validator.
                Review the provided JSON data and check for:
                  • Missing or null required fields (id, patient_id, collected_at, results)
                  • Numeric values outside clinically plausible ranges
                  • Unit inconsistencies (e.g. mg/dL vs mmol/L mixed in same report)
                  • Date/time fields that are in the future or logically impossible
                  • Duplicate test codes within the same report
 
                Respond ONLY with a JSON object — no markdown, no commentary:
                {
                  "isValid": true | false,
                  "errors":   [ { "field": "...", "message": "..." } ],
                  "warnings": [ { "field": "...", "message": "..." } ],
                  "summary":  "one-line summary"
                }
                """,

                // ── Quotation verification ───────────────────────────────────────
                ["verify_quotation"] = """
                You are a LIMS quotation auditor.
                Examine the provided quotation JSON and verify:
                  • All line items have a valid test_code, description, unit_price, and quantity
                  • Total amount matches sum of (unit_price × quantity) for every line
                  • Discount percentage is between 0 and 100
                  • Tax amount is consistent with the declared tax_rate
                  • client_id and quote_date are present
                  • No negative prices or quantities
 
                Respond ONLY with a JSON object — no markdown, no commentary:
                {
                  "isValid":          true | false,
                  "calculatedTotal":  0.00,
                  "declaredTotal":    0.00,
                  "discrepancy":      0.00,
                  "errors":           [ { "field": "...", "message": "..." } ],
                  "warnings":         [ { "field": "...", "message": "..." } ],
                  "summary":          "one-line summary"
                }
                """,

                ["cross_check_patient"] = """
                You are a LIMS patient-data integrity checker.
                You will receive two JSON objects: "source" and "target", each representing
                the same patient from two different systems.
                Identify:
                  • Fields that differ between source and target
                  • Fields present in one but missing in the other
                  • Logical inconsistencies (e.g. dob contradicts age, gender mismatch)
 
                Respond ONLY with a JSON object — no markdown, no commentary:
                {
                  "match":        true | false,
                  "differences":  [ { "field": "...", "sourceValue": "...", "targetValue": "..." } ],
                  "missingInSource": [ "field1", "field2" ],
                  "missingInTarget": [ "field1", "field2" ],
                  "summary": "one-line summary"
                }
                """,

                ["extract_fields"] = """
                You are a LIMS data extraction assistant.
                From the provided raw data, extract the fields specified in the user prompt.
                If a field cannot be found or inferred, set its value to null.
 
                Respond ONLY with a JSON object containing exactly the requested fields
                — no markdown, no commentary, no extra keys.
                """,

                ["audit_summary"] = """
                You are a LIMS compliance auditor.
                Analyse the provided records and produce a concise audit summary covering:
                  • Record completeness score (0-100)
                  • Missing mandatory fields
                  • Data quality issues found
                  • Compliance flags (if any regulatory fields are invalid)
                  • Recommended corrective actions
 
                Respond ONLY with a JSON object — no markdown, no commentary:
                {
                  "completenessScore": 0,
                  "missingFields":     [],
                  "qualityIssues":     [],
                  "complianceFlags":   [],
                  "recommendations":   [],
                  "summary":           "one-line summary"
                }
                """,
            };

        public string GetSystemPrompt(string operation)
        {
            if (_templates.TryGetValue(operation, out var prompt))
                return prompt;

            throw new UnknownOperationException(operation);
        }

        public IEnumerable<string> GetRegisteredOperations() => _templates.Keys;
    }
}
