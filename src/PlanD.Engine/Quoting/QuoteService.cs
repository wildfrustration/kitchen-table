namespace PlanD.Engine.Quoting;

/// <summary>
/// Builds a ranked quote: every PDP in the county, its estimated yearly cost (premium + drugs) at the
/// cheapest pharmacy type the person's preference allows, and the reasons a broker needs.
/// </summary>
public sealed class QuoteService(IQuoteData data)
{
    public async Task<Quote> QuoteAsync(QuoteRequest request, CancellationToken ct = default)
    {
        var parameters = BenefitYearParameters.For(request.Year);
        var plans = await data.PlansForCountyAsync(request.Year, request.CountyCode, ct);

        var rxcuis = request.Drugs.Select(d => d.Rxcui).Distinct().ToList();
        var infos = (await data.DrugInfoAsync(rxcuis, ct)).ToDictionary(i => i.Rxcui);
        var drugData = await data.PlanDrugDataAsync(request.Year, plans, rxcuis, request.PharmacyNpis, ct);

        var prescriptions = request.Drugs.Select(d => ToPrescription(d, infos.GetValueOrDefault(d.Rxcui), request.Pharmacy)).ToList();
        var options = new SimulationOptions
        {
            Parameters = parameters,
            StartMonth = request.StartMonth,
            ExtraHelp = request.ExtraHelp,
            DeemedDeductibleRule = request.DeemedDeductibleRule,
        };
        var months = 12 - request.StartMonth + 1;

        var quotes = new List<PlanQuote>();
        foreach (var plan in plans)
        {
            if (!drugData.Benefits.TryGetValue(plan.Key, out var benefit)) continue;

            var drugs = prescriptions
                .Select(rx => (rx, drugData.Drugs.GetValueOrDefault((plan.Key, rx.Rxcui))
                                   ?? PlanDrug.NotCovered(rx.Rxcui, drugData.MarketUnitCost.GetValueOrDefault(rx.Rxcui))))
                .ToList();

            var notes = new List<string>();
            var statuses = request.PharmacyNpis
                .Select(npi => drugData.NamedPharmacies.TryGetValue((plan.Key, npi), out var p)
                    ? new PharmacyStatus(npi, InNetwork: true, Preferred: p.PreferredRetail || p.PreferredMail)
                    : new PharmacyStatus(npi, InNetwork: false, Preferred: false))
                .ToList();

            // Like Plan Finder's plan card: the lowest cost among the pharmacies the person could use.
            var (best, pricedAt) = Candidates(benefit, plan.Key, drugData, request, notes)
                .Select(c => (Estimate: DrugCostSimulator.Simulate(benefit, drugs, c.Fees, c.Type, options), c.Npi))
                .MinBy(x => x.Estimate.Total);

            var monthly = plan.MonthlyPremium(request.ExtraHelp);
            var annualPremium = monthly * months;
            var allCovered = best.Drugs.All(d => d.Status is CoverageStatus.Covered or CoverageStatus.CoveredSupplemental);
            quotes.Add(new PlanQuote(plan, best, monthly, annualPremium, annualPremium + best.Total,
                Explain(plan, best, pricedAt, notes), allCovered, pricedAt, statuses));
        }

        var ranked = quotes
            .OrderBy(q => q.EstimatedAnnualCost)
            .ThenByDescending(q => decimal.TryParse(q.Plan.StarRating, out var s) ? s : 0m)
            .ToList();
        return new Quote(request, infos.Values.ToList(), ranked);
    }

    /// <summary>
    /// Plan Finder turns a 30-day mail-order request into 90-day fills; the quantity scales with it.
    /// </summary>
    private static Prescription ToPrescription(DrugRequest d, DrugInfo? info, PharmacyPreference pharmacy)
    {
        var (quantity, days) = pharmacy == PharmacyPreference.Mail && d.DaysSupply == 30
            ? (d.Quantity * 3, 90)
            : (d.Quantity, d.DaysSupply);
        return new Prescription(
            d.Rxcui, info?.Name ?? $"RXCUI {d.Rxcui}", quantity, days,
            info?.Kind ?? DrugKind.Generic, info?.IsInsulin ?? false, info?.IsVaccine ?? false);
    }

    private sealed record Candidate(PharmacyType Type, DispensingFees Fees, string? Npi);

    /// <summary>
    /// The client's in-network retail pharmacies when they named some, priced with each one's own status and fees;
    /// otherwise (or when none is in network) the plan's typical preferred/standard pharmacy.
    /// </summary>
    private static IEnumerable<Candidate> Candidates(PlanBenefit benefit, PlanKey key, PlanDrugData data, QuoteRequest request, List<string> notes)
    {
        if (request.Pharmacy == PharmacyPreference.Retail && request.PharmacyNpis.Count > 0)
        {
            var named = request.PharmacyNpis
                .Where(npi => data.NamedPharmacies.TryGetValue((key, npi), out var p) && p.Retail)
                .Select(npi =>
                {
                    var p = data.NamedPharmacies[(key, npi)];
                    var type = p.PreferredRetail && benefit.Offers(PharmacyType.PreferredRetail)
                        ? PharmacyType.PreferredRetail
                        : PharmacyType.StandardRetail;
                    return new Candidate(type, p.Fees, npi);
                })
                .ToList();
            if (named.Count > 0) return named;
            notes.Add("None of the client's pharmacies are in this plan's network; priced at a typical in-network pharmacy");
        }

        return PharmacyTypes(benefit, key, data, request.Pharmacy, notes).Select(t => new Candidate(t, Fees(data, key, t), null));
    }

    private static IEnumerable<PharmacyType> PharmacyTypes(
        PlanBenefit benefit, PlanKey key, PlanDrugData data, PharmacyPreference preference, List<string> notes)
    {
        bool Usable(PharmacyType t, bool needsNetwork) =>
            benefit.Offers(t) && (!needsNetwork || data.Networks.TryGetValue((key, t), out var n) && n.PharmacyCount > 0);

        var candidates = new List<PharmacyType>();
        if (preference == PharmacyPreference.Mail)
        {
            if (Usable(PharmacyType.PreferredMail, needsNetwork: true)) candidates.Add(PharmacyType.PreferredMail);
            if (Usable(PharmacyType.StandardMail, needsNetwork: false)) candidates.Add(PharmacyType.StandardMail);
            if (candidates.Count > 0) return candidates;
            notes.Add("No mail-order benefit; priced at retail");
        }

        if (Usable(PharmacyType.PreferredRetail, needsNetwork: true)) candidates.Add(PharmacyType.PreferredRetail);
        if (Usable(PharmacyType.StandardRetail, needsNetwork: false)) candidates.Add(PharmacyType.StandardRetail);
        return candidates.Count > 0 ? candidates : [PharmacyType.StandardRetail];
    }

    private static DispensingFees Fees(PlanDrugData data, PlanKey key, PharmacyType type) =>
        data.Networks.TryGetValue((key, type), out var n) ? n.Fees : DispensingFees.None;

    private static List<string> Explain(PlanOffer plan, DrugCostEstimate estimate, string? pricedAtNpi, List<string> notes)
    {
        foreach (var line in estimate.Drugs)
        {
            var d = line with { Name = DrugNames.Short(line.Name) };
            switch (d.Status)
            {
                case CoverageStatus.NotOnFormulary when d.PriceUnavailable:
                    notes.Add($"{d.Name}: not covered, and CMS publishes no price for it — left out of the total (the client pays the pharmacy's cash price)");
                    break;
                case CoverageStatus.NotOnFormulary:
                    notes.Add($"{d.Name}: not covered — full price, doesn't count toward the cap");
                    break;
                case CoverageStatus.DaysSupplyNotCovered:
                    notes.Add($"{d.Name}: this days supply isn't covered at this pharmacy type — full price");
                    break;
                case CoverageStatus.CoveredSupplemental:
                    notes.Add($"{d.Name}: covered as a supplemental (Part D-excluded) drug");
                    break;
            }

            var hurdles = new List<string>();
            if (d.PriorAuth) hurdles.Add("prior authorization");
            if (d.StepTherapy) hurdles.Add("step therapy");
            if (d.ExceedsQuantityLimit) hurdles.Add("over the plan's quantity limit");
            if (hurdles.Count > 0) notes.Add($"{d.Name}: {string.Join(", ", hurdles)}");
            if (d.PriceUnavailable && d.Status is not CoverageStatus.NotOnFormulary) notes.Add($"{d.Name}: no price data — left out of the total");
            else if (d.PriceEstimated && d.Status is CoverageStatus.Covered) notes.Add($"{d.Name}: price estimated");
        }

        var where = pricedAtNpi is null ? "a" : "the client's";
        notes.Add(estimate.Pharmacy switch
        {
            PharmacyType.PreferredRetail => $"Priced at {where} preferred retail pharmacy",
            PharmacyType.StandardRetail => $"Priced at {where} standard retail pharmacy",
            PharmacyType.PreferredMail => "Priced at preferred mail order",
            _ => "Priced at standard mail order",
        });
        if (estimate.Pharmacy is PharmacyType.PreferredMail or PharmacyType.StandardMail)
            notes.Add("Mail-order drug prices are estimated from retail prices (CMS publishes no mail-order prices); generics are often cheaper by mail");
        if (estimate.CatastrophicMonth is { } m) notes.Add($"Reaches the out-of-pocket cap in month {m}");
        if (plan.BenefitType is null) notes.Add("Benefit type unknown (no landscape row)");
        return notes;
    }
}
