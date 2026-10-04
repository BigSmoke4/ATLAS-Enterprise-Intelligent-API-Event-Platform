using Atlas.Modules.AIOperations.Application;
using Atlas.Modules.Audit.Application;
using Atlas.Modules.DeploymentIntelligence.Application;
using Atlas.Modules.EventPlatform.Application;
using Atlas.Modules.IncidentManagement.Domain;
using Atlas.Modules.PolicyEngine.Application;
using Atlas.Modules.Reliability.Application;
using Atlas.Modules.ServiceRegistry.Application;
using Atlas.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>
/// Razor pages for the operations consoles that sit beside the Command Centre:
/// event pipelines, deployments, policies, reliability and AI operations.
///
/// Every action is thin — it resolves tenant scope (OrganizationScopeResolver),
/// calls one or two application services, and returns a view. No queries, no
/// business rules, no infrastructure.
/// </summary>
[Authorize]
public sealed class OperationsPagesController : Controller
{
    private const int MaxRows = 100;

    private readonly IDeadLetterService _deadLetters;
    private readonly IDeploymentRegressionService _deployments;
    private readonly IServiceHealthService _services;
    private readonly IPolicyManagementService _policies;
    private readonly ICircuitBreakerRegistry _circuitBreakers;
    private readonly IEnumerable<IAtlasTool> _tools;
    private readonly IAuditQueryService _audit;
    private readonly IConfiguration _configuration;

    public OperationsPagesController(
        IDeadLetterService deadLetters,
        IDeploymentRegressionService deployments,
        IServiceHealthService services,
        IPolicyManagementService policies,
        ICircuitBreakerRegistry circuitBreakers,
        IEnumerable<IAtlasTool> tools,
        IAuditQueryService audit,
        IConfiguration configuration)
    {
        _deadLetters = deadLetters;
        _deployments = deployments;
        _services = services;
        _policies = policies;
        _circuitBreakers = circuitBreakers;
        _tools = tools;
        _audit = audit;
        _configuration = configuration;
    }

    [HttpGet("/Events")]
    public async Task<IActionResult> Events([FromQuery] Guid organizationId, [FromQuery] string? topic, [FromQuery] string? eventType, CancellationToken ct)
    {
        if (!OrganizationScopeResolver.TryResolve(User, ref organizationId)) return Forbid();

        var deadLetters = await _deadLetters.ListAsync(topic, 1, MaxRows, ct, eventType);

        return View("~/Views/Events/Index.cshtml", new EventsViewModel
        {
            // Dead-letter storage is platform-level (events are not tenant-attributed),
            // so the page is readable by any authenticated operator; replay stays role-gated.
            HasOrganizationContext = true,
            OrganizationId = organizationId,
            DeadLetters = deadLetters,
            CanReplay = User.IsInRole(Atlas.Modules.Identity.Domain.AtlasRoles.PlatformAdmin),
            CanPublish = User.IsInRole(Atlas.Modules.Identity.Domain.AtlasRoles.Developer) || User.IsInRole(Atlas.Modules.Identity.Domain.AtlasRoles.PlatformAdmin)
        });
    }

    [HttpGet("/Deployments")]
    public async Task<IActionResult> Deployments([FromQuery] Guid organizationId, CancellationToken ct)
    {
        if (!OrganizationScopeResolver.TryResolve(User, ref organizationId)) return Forbid();

        var model = new DeploymentsViewModel { HasOrganizationContext = organizationId != Guid.Empty, OrganizationId = organizationId };
        if (model.HasOrganizationContext)
        {
            model.Deployments = await _deployments.ListAsync(organizationId, null, 1, MaxRows, ct);
            model.Services = await _services.GetStatusAsync(organizationId, 1, MaxRows, ct);
        }

        return View("~/Views/Deployments/Index.cshtml", model);
    }

    [HttpGet("/Policies")]
    public async Task<IActionResult> Policies([FromQuery] Guid organizationId, CancellationToken ct)
    {
        if (!OrganizationScopeResolver.TryResolve(User, ref organizationId)) return Forbid();

        var model = new PoliciesViewModel { HasOrganizationContext = organizationId != Guid.Empty, OrganizationId = organizationId };
        if (model.HasOrganizationContext)
        {
            model.Policies = await _policies.ListAsync(organizationId, 1, MaxRows, ct);
        }

        return View("~/Views/Policies/Index.cshtml", model);
    }

    [HttpGet("/Reliability")]
    public async Task<IActionResult> Reliability([FromQuery] Guid organizationId, CancellationToken ct)
    {
        if (!OrganizationScopeResolver.TryResolve(User, ref organizationId)) return Forbid();

        // SnapshotStates returns the live in-process state of every breaker
        // that has actually been exercised — an empty dictionary means no
        // guarded downstream call has run yet, which the view states plainly.
        var states = _circuitBreakers.SnapshotStates();
        return View("~/Views/Reliability/Index.cshtml", new ReliabilityViewModel
        {
            HasOrganizationContext = organizationId != Guid.Empty,
            OrganizationId = organizationId,
            CircuitBreakers = states
        });
    }

    [HttpGet("/AiOps")]
    public IActionResult AiOps([FromQuery] Guid organizationId)
    {
        if (!OrganizationScopeResolver.TryResolve(User, ref organizationId)) return Forbid();

        return View("~/Views/AiOps/Index.cshtml", new AiOpsViewModel
        {
            HasOrganizationContext = organizationId != Guid.Empty,
            OrganizationId = organizationId,
            CompletionProviderConfigured = !string.IsNullOrWhiteSpace(_configuration["AI:AnthropicApiKey"]),
            Tools = _tools
                .Select(tool => new AiToolDescriptor(tool.Name, tool.Description, tool.Kind.ToString()))
                .OrderBy(tool => tool.Kind)
                .ThenBy(tool => tool.Name)
                .ToList()
        });
    }

    [HttpGet("/Audit")]
    [Authorize(Policy = "AuditRead")]
    public async Task<IActionResult> Audit([FromQuery] Guid organizationId, [FromQuery] string? action, [FromQuery] string? resourceType, CancellationToken ct)
    {
        if (!OrganizationScopeResolver.TryResolve(User, ref organizationId)) return Forbid();

        return View("~/Views/Audit/Index.cshtml", new AuditViewModel
        {
            HasOrganizationContext = organizationId != Guid.Empty,
            OrganizationId = organizationId,
            ActionFilter = action,
            ResourceTypeFilter = resourceType,
            CanRead = true,
            Entries = organizationId == Guid.Empty
                ? Array.Empty<Atlas.Modules.Audit.Domain.AuditEntry>()
                : await _audit.ListAsync(organizationId, resourceType, action, 1, MaxRows, ct)
        });
    }
}
