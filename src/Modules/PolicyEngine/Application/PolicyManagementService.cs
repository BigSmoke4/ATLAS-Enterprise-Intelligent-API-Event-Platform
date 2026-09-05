using Atlas.Shared.Application;
using Atlas.Modules.PolicyEngine.Domain;
using Atlas.Modules.PolicyEngine.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.PolicyEngine.Application;

public class PolicyManagementService : IPolicyManagementService
{
    private readonly PolicyEngineDbContext _db;
    private readonly IPolicyEvaluator _evaluator;
    public PolicyManagementService(PolicyEngineDbContext db, IPolicyEvaluator evaluator) { _db = db; _evaluator = evaluator; }

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
        rule.Deactivate();
        await _db.SaveChangesAsync(ct);
        return PolicyOperationResult.Ok();
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
        return _evaluator.Evaluate(rules, facts);
    }
}
