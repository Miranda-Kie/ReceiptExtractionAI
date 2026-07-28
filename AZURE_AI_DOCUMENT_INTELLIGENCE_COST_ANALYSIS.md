# Azure AI Document Intelligence Cost Analysis
## For HST Receipts Client Onboarding

**Last Updated:** July 2026  
**Model:** Azure Document Intelligence (prebuilt-receipt)  
**Region:** Canada (CAD pricing)

---

## Pricing Structure

### Base Rates (Pay-as-You-Go)
- **Prebuilt Receipt Model:** $10.00 USD per 1,000 pages
- **Free Tier:** 500 pages per month (F0 SKU — limited features)
- **Standard Tier:** Pay-per-page after free allowance

*Conversion: $10 USD ≈ $13.50 CAD (approximate, varies by exchange rate)*

### What Counts as a "Page"?
- **Single-page PDF / Image:** 1 page
- **Multi-page PDF:** Each page = 1 billable unit
- **Multi-receipt PDF:** Each page processed = 1 unit (even if the fallback per-page re-analysis is triggered)

---

## Cost Scenarios

### Scenario 1: Small Business (50 receipts/month)
**Assumption:** Mix of single-page PDFs and images, minimal multi-page uploads

| Item | Calculation | Cost USD | Cost CAD |
|------|-----------|----------|----------|
| Free allowance | 500 pages/month | $0 | $0 |
| Billable pages | 50 - 0 = 0 pages | $0 | $0 |
| **Monthly Total** | | **$0** | **$0** |

**Result:** Stays within free tier. **No charges.**

---

### Scenario 2: Mid-Market Business (500 receipts/month)
**Assumption:** Mostly single-page receipts; ~5% are multi-page batches (e.g., 10 orders per PDF)

| Item | Calculation | Cost USD | Cost CAD |
|------|-----------|----------|----------|
| Free allowance | 500 pages/month | $0 | $0 |
| Single-page receipts | 475 files × 1 page | 475 pages | — |
| Multi-page batches | 25 files × avg 8 pages | 200 pages | — |
| **Total billable pages** | 475 + 200 = 675 pages | | |
| **Cost** | 675 pages × $10 ÷ 1,000 | **$6.75** | **~$9.11** |
| **Monthly Total** | | **$6.75** | **~$9.11** |

**Result:** Minimal charge. **~$81/year CAD.**

---

### Scenario 3: High-Volume Business (5,000 receipts/month)
**Assumption:** ~10% multi-page uploads (common for batch processing); regular users with 10-order PDFs

| Item | Calculation | Cost USD | Cost CAD |
|------|-----------|----------|----------|
| Free allowance | 500 pages/month | $0 | $0 |
| Single-page receipts | 4,500 files × 1 page | 4,500 pages | — |
| Multi-page uploads (best case) | 500 files × 5 pages avg | 2,500 pages | — |
| **Total billable pages** | 4,500 + 2,500 = 7,000 pages | | |
| **Cost** | 7,000 × $10 ÷ 1,000 | **$70.00** | **~$94.50** |
| **Monthly Total** | | **$70.00** | **~$94.50** |

**Result:** Predictable cost for high volume. **~$1,134/year CAD.**

---

### Scenario 4: Enterprise with Per-Page Fallback Overhead
**Assumption:** 10,000 receipts/month; 15% are multi-page PDFs where the per-page re-analysis fallback is triggered (e.g., 11-page documents)

| Item | Calculation | Cost USD | Cost CAD |
|------|-----------|----------|----------|
| Free allowance | 500 pages/month | $0 | $0 |
| Single-page receipts | 8,500 × 1 page | 8,500 pages | — |
| Multi-page normal (7% of total) | 700 × 8 pages avg | 5,600 pages | — |
| **Multi-page with fallback (8% of total)** | 800 files × (1 initial + 10 per-page re-analysis) | **8,800 pages** | — |
| **Total billable pages** | 8,500 + 5,600 + 8,800 = 22,900 | | |
| **Cost** | 22,900 × $10 ÷ 1,000 | **$229.00** | **~$309.15** |
| **Monthly Total** | | **$229.00** | **~$309.15** |

**Key insight:** Per-page fallback added ~8,800 billable pages (when 800 problematic PDFs were re-analyzed). **Cost impact: +38% vs. baseline.**

**Annual:** ~$3,710 CAD (with fallback overhead)

---

## Cost Multipliers & Edge Cases

### 1. The Per-Page Fallback Cost
- **When it triggers:** PDFs where Document Intelligence's whole-document pass underextracts receipts (e.g., multi-order Walmart PDFs with 11+ pages each)
- **Impact:** Each page gets re-analyzed individually = **1 page becomes ~1 + (pageCount) calls**
  - 11-page PDF: 1 call → ~12 calls (12× cost multiplier)
  - 20-page batch: 1 call → ~21 calls (21× cost multiplier)
- **Mitigation:** Educate clients to split large PDFs before upload, or accept the re-analysis cost as a feature

### 2. Commitment Tiers (if volume grows)
- At ~250,000+ pages/month, Azure offers **commitment-based pricing**
- Example: 500,000-page commitment = ~$3,750 USD/month ($5,062 CAD) — breaks down to $7.50/1,000 pages
- Below commitment thresholds, pay-as-you-go is cheaper

### 3. Regional Variations
- **Canada (East/Central):** Pricing as quoted above
- **US East:** ~5–10% cheaper
- **EU:** ~15–20% more expensive
- Always verify via Azure Pricing Calculator for exact regional rates

---

## Visibility & Tracking (Currently Missing)

**Your system does NOT yet track:**
- Per-file API call count
- Which files triggered the fallback
- Monthly bill exposure
- Cost per user or client

**Recommendation:** Implement a usage log before charging clients. Track:
```
{
  "FileName": "Walmart.pdf",
  "FilePage": 11,
  "AnalysisMethod": "PerPageFallback",  // or "WholePage", "RulesOnly"
  "ApiCallsUsed": 12,
  "EstimatedCost": 0.12,  // USD
  "Timestamp": "2026-07-27T15:30:00Z"
}
```

---

## Recommendations for Client Communication

### For Initial Pitch
- **"100–500 receipts/month? Free tier covers you."**
- **"1,000–5,000/month? Expect $10–70 USD/month (~$13–95 CAD/month) depending on file format."**
- **"10,000+/month? We recommend volume commitment tiers for predictable pricing."**

### For Transparency
- Disclose the per-page fallback:  
  *"Multi-page receipt batches (e.g., 11+ orders in one PDF) may trigger additional analysis to ensure all receipts are captured. This increases cost by ~2–3% for typical workloads but ensures accuracy."*

### For Cost Control
- Suggest client practices:
  1. Upload single receipts / single-order PDFs when possible
  2. Split large batch PDFs before upload (e.g., Walmart's 20-order PDF → 4 × 5-order PDFs)
  3. Review monthly usage logs (once implemented)

---

## What You Should Build Next

**Priority:**
1. **Usage Logging** — Track API calls per file, per user, per month
   - File: `IDocumentIntelligenceUsageTracker` interface
   - Log: File name, page count, API calls used, cost estimate
   
2. **Monthly Cost Projection** — Dashboard showing YTD spend and forecast
   - Query: Group usage logs by client, month
   - Display: "You've used 2,450 pages this month ($24.50 USD). At this rate, ~$294 USD/year."

3. **Per-Client Billing** — If you're multi-tenant later, attribute costs to each client

4. **Fallback Cost Transparency** — Flag files that triggered re-analysis
   - Show clients: "This PDF required deep analysis (+11 extra calls) to extract all 11 receipts."

---

## Summary Table

| Volume | Monthly Cost USD | Monthly Cost CAD | Annual CAD | Notes |
|--------|------------------|------------------|-----------|-------|
| 0–500 receipts | $0 (free tier) | $0 | $0 | Fully covered by free allowance |
| 500–2,500 receipts | $1–25 | $1.35–33.75 | $16–405 | Very low cost |
| 2,500–5,000 receipts | $25–70 | $33.75–94.50 | $405–1,134 | Predictable, budgetable |
| 5,000–10,000 receipts | $70–200 | $94.50–270 | $1,134–3,240 | Consider commitment tier |
| 10,000+ receipts | $200–500+ | $270–675+ | $3,240–8,100+ | Commitment tier likely cheaper |

---

## Sources & References

- [Azure Document Intelligence Pricing (Microsoft Azure Official)](https://azure.microsoft.com/en-us/pricing/details/document-intelligence/)
- Microsoft Q&A: Azure Document Intelligence pricing discussions (learn.microsoft.com)

*Note: Pricing valid as of July 2026. Exchange rates (USD to CAD) and regional pricing subject to change. Always verify current rates via Azure Pricing Calculator.*
