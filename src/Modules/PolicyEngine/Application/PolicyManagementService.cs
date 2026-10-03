using Atlas.Shared.Application;
using Atlas.Modules.PolicyEngine.Domain;
using Atlas.Modules.PolicyEngine.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.PolicyEngine.Application;

public class PolicyManagementService : IPolicyManagementService
{
    private readonly PolicyEngineDbContext _db;
    private readonly IPolicyEvaluator _evaluator;
    private readonly Atlas.Shared.Contracts.IIncidentAlertSink? _alerts;
    public PolicyManagementService(PolicyEngineDbContext db, IPolicyEvaluator evaluator, Atlas.Shared.Contracts.IIncidentAlertSink? alerts = null) { _db = db; _evaluator = evaluator; _alerts = alerts; }

    public async Task<PolicyOperationResult<Guid>> CreateAsync(Guid organizationId, string name, IReadOnlyList<PolicyCondition> conditions, PolicyAction action, CancellationToken ct = default)
    {
        try
        {
            var rule = PolicyRule.Create(organizationId, name, conditions, action);
            _db.Rules.Add(rule);
            await _db.SaveChangesAsync(ct);
            return PolicyOperationResult<Guid>.Ok(rule.Id);
        }
        catch (ArgumentException ex)
        {
            return PolicyOperationResult<Guid>.Fail(ex.Message);
        }
    }

    public async Task<PolicyOperationResult> DeactivateAsync(Guid organizationId, Guid policyId, CancellationToken ct = default)
    {
        var rule = await _db.Rules.FirstOrDefaultAsync(r => r.Id == policyId && r.OrganizationId == organizationId, ct);
        if (rule is null) return PolicyOperationResult.Fail("Policy not found.");
        rule.Deactivate(); await _db.SaveChangesAsync(ct); return PolicyOperationResult.Ok();
    }

    public async Task<PolicyOperationResult> ActivateAsync(Guid organizationId, Guid policyId, CancellationToken ct = default)
    {
        var rule = await _db.Rules.FirstOrDefaultAsync(r => r.Id == policyId && r.OrganizationId == organizationId, ct);
        if (rule is null) return PolicyOperationResult.Fail("Policy not found.");
        rule.Activate(); await _db.SaveChangesAsync(ct); return PolicyOperationResult.Ok();
    }

    public async Task<PolicyOperationResult<Guid>> CreateVersionAsync(Guid organizationId, Guid policyId, IReadOnlyList<PolicyCondition> conditions, PolicyAction action, CancellationToken ct = default)
    {
        var rule = await _db.Rules.FirstOrDefaultAsync(r => r.Id == policyId && r.OrganizationId == organizationId, ct);
        if (rule is null) return PolicyOperationResult<Guid>.Fail("Policy not found.");
        try
        {
            var version = rule.CreateNewVersion(conditions, action);
            _db.Rules.Add(version); await _db.SaveChangesAsync(ct);
            return PolicyOperationResult<Guid>.Ok(version.Id);
        }
        catch (ArgumentException ex) { return PolicyOperationResult<Guid>.Fail(ex.Message); }
    }

    public async Task<IReadOnlyList<PolicyRule>> ListAsync(Guid organizationId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        return await _db.Rules.Where(r => r.OrganizationId == organizationId)
            .OrderBy(r => r.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .AsNoTracking().ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PolicyEvaluationOutcome>> EvaluateActiveRulesAsync(Guid organizationId, IReadOnlyDictionary<string, double> facts, CancellationToken ct = default)
    {
        var rules = await _db.Rules.Where(r => r.OrganizationId == organizationId && r.IsActive).AsNoTracking().ToListAsync(ct);
        var outcomes = _evaluator.Evaluate(rules, facts);
        if (_alerts is not null)
        {
            foreach (var outcome in outcomes.Where(o => o.Matched && o.Rule.Action.Type == PolicyActionType.RaiseAlert))
                await _alerts.RaiseAsync(organizationId, outcome.Rule.Name, outcome.Rule.Action.AlertSeverity ?? "Sev2", "policy-engine", ct);
        }
        return outcomes;
    }
}
